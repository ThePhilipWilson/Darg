using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0217: a story's panel lists the cards it holds, each a link that opens the card; a named card no board
/// has shows as not found; a card that names the story in its Story: line shows too; a plain card's panel has no list.</summary>
public class StoryCardsTests
{
    static CardViewModel Vm(DargApp app, string id) =>
        app.Board.Rows.SelectMany(r => r.Lists).SelectMany(l => l.Cards).First(c => c.Id == id);

    static IReadOnlyList<Button> StoryButtons(DargApp app) => app.Window.GetVisualDescendants().OfType<Button>()
        .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("member")).ToList();

    [AvaloniaFact]
    public async Task A_story_lists_its_cards_as_links_that_open_them()
    {
        using var app = DargApp.Make();
        var logo = app.Card("Logo drawn", "Done");
        var themes = app.Card("Themes picker", "QA Ready");
        var story = app.Card("Look and feel", "Done", type: "Story",
            description: $"Made the app look like itself.\n\nCards: {logo.Id}, {themes.Id}, ZZ-0099");
        var tagged = app.Card("Fonts", "Done", description: $"Fonts set.\n\nStory: {story.Id} Look and feel");
        var plain = app.Card("Something else", "To Do");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        app.Board.Open(Vm(app, story.Id));
        await DargApp.Settle();
        var editor = app.Board.Editor!;
        Assert.True(editor.IsStoryCard);
        Assert.Equal([logo.Id, themes.Id, "ZZ-0099", tagged.Id], editor.StoryLinks.Select(l => l.Id));
        Assert.Equal($"{logo.Id}: Logo drawn (Done on Main)", editor.StoryLinks[0].Text);
        Assert.Equal($"{themes.Id}: Themes picker (QA Ready on Main)", editor.StoryLinks[1].Text);
        Assert.Equal("ZZ-0099: no board of this project has it", editor.StoryLinks[2].Text);
        Assert.True(editor.StoryLinks[2].IsWarning);
        Assert.Equal("Contains 4 cards", editor.StoryHeading);

        // drawn in the panel, one button per card
        var buttons = StoryButtons(app);
        Assert.Equal(4, buttons.Count);
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == $"{themes.Id}: Themes picker (QA Ready on Main)");
        app.Shot("story-panel");

        // each link opens its card
        buttons[1].Command!.Execute(null);
        await DargApp.Settle();
        Assert.Equal(themes.Id, app.Board.Editor!.CardId);
        Assert.False(app.Board.Editor.IsStoryCard);
        Assert.Empty(StoryButtons(app));

        app.Board.Open(Vm(app, story.Id));
        await DargApp.Settle();
        app.Board.Editor!.StoryLinks[0].Open.Execute(null);
        await DargApp.Settle();
        Assert.Equal(logo.Id, app.Board.Editor!.CardId);

        // a plain card's panel has no list
        app.Board.Open(Vm(app, plain.Id));
        await DargApp.Settle();
        Assert.False(app.Board.Editor!.IsStoryCard);
        Assert.Empty(app.Board.Editor.StoryLinks);
        Assert.Empty(StoryButtons(app));
        Assert.DoesNotContain(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Contains ") == true);
    }
}
