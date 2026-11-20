using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0156: every card shows a ticket quality star beside its id, red for a bare card; Improve is told the
/// score, and its rewrite of the card moves the star.</summary>
public class TicketQualityTests
{
    static TextBlock? Star(DargApp app, string id) => app.Window.GetVisualDescendants().OfType<TextBlock>()
        .FirstOrDefault(t => t.Classes.Contains("quality") && t.DataContext is CardViewModel c && c.Id == id);

    static IBrush? Theme(string key) => Avalonia.Application.Current!.TryGetResource(key, null, out var b) ? b as IBrush : null;

    static string Colour(TextBlock t) => t.Foreground is ISolidColorBrush b ? b.Color.ToString() : "?";

    [AvaloniaFact]
    public async Task A_bare_card_is_red_and_Improve_s_rewrite_turns_it_green()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix logo aliasing");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        var star = Star(app, card.Id);
        Assert.NotNull(star);
        Assert.Equal("★0", star.Text);
        Assert.Equal(Colour(new TextBlock { Foreground = Theme("Bad") }), Colour(star));
        Assert.Contains("Ticket quality 0/3", ToolTip.GetTip(star) as string);

        // Improve is told the score and what's missing
        await app.Api("improve-card", new { project = "proj", card = card.Id });
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "message"), "the Improve agent's prompt");
        var prompt = app.Prompts().Single(p => p.Contains("Improve this card"));
        Assert.Contains("ticket quality score is 0/3 (red)", prompt);

        // the Improve agent rewrites the card: the board reloads it and the star goes green
        var file = Path.Combine(app.BoardFolder, "items", card.File);
        File.WriteAllText(file, File.ReadAllText(file).Replace("## Description\n",
            "## Description\n\n> fix logo aliasing\n\nThe Darg logo in the header has jagged edges at 100% scale; draw it smoothly at every scale so it looks crisp.\n\n" +
            "**Where:** the header, `src/Darg.Desktop/Views/MainWindow.axaml`.\n\n**Done when:**\n- the logo's edges are smooth at 100%, 150% and 200%\n"));
        await app.Until(() => Star(app, card.Id)?.Text == "★3", "the star to read ★3 after the rewrite");
        star = Star(app, card.Id)!;
        Assert.Contains("green", star.Classes);
        Assert.Equal(Colour(new TextBlock { Foreground = Theme("Ok") }), Colour(star));

        // the card's panel shows it too, and follows the description as it's typed
        var vm = app.Board.Rows.SelectMany(r => r.Lists).SelectMany(l => l.Cards).First(c => c.Id == card.Id);
        app.Board.Open(vm);
        await DargApp.Settle();
        var editor = app.Board.Editor!;
        Assert.Equal("★ 3/3", editor.Quality.Long);
        editor.Description = "fix logo aliasing";
        Assert.Equal(0, editor.Quality.Stars);
        await DargApp.Settle();
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("quality") && t.Text == "★ 0/3");
        app.Shot("panel-red");
    }

    [AvaloniaFact]
    public async Task Two_stars_is_amber()
    {
        using var app = DargApp.Make();
        var card = app.Card("remember folded columns", description:
            "When the board opens it should remember which columns were folded last time, so I don't have to fold them again after every restart.\n\n" +
            "**Where:** the Board section's column headers.");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var star = Star(app, card.Id)!;
        Assert.Equal("★2", star.Text);
        Assert.Contains("amber", star.Classes);
        Assert.Equal(Colour(new TextBlock { Foreground = Theme("Warn") }), Colour(star));
        app.Shot("board-amber");
    }

    static Button? ImproveButton(DargApp app, string id) => app.Window.GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(b => b.IsEffectivelyVisible && b.Content as string == "✦ Improve" && b.DataContext is CardViewModel c && c.Id == id);

    [AvaloniaFact] // WOR-0162: Improve beside the score of a thin card, and not beside a green one
    public async Task A_thin_card_has_Improve_beside_its_score_and_a_green_one_has_none()
    {
        using var app = DargApp.Make();
        var thin = app.Card("remember folded columns", description:
            "When the board opens it should remember which columns were folded last time, so I don't have to fold them again after every restart.\n\n" +
            "**Where:** the Board section's column headers.");
        var full = app.Card("smooth logo", description:
            "The Darg logo in the header has jagged edges at 100% scale; draw it smoothly at every scale so it looks crisp.\n\n" +
            "**Where:** the header, `src/Darg.Desktop/Views/MainWindow.axaml`.\n\n**Done when:**\n- the logo's edges are smooth at 100%, 150% and 200%\n");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await DargApp.Settle();

        Assert.Equal("★3", Star(app, full.Id)!.Text);
        Assert.Null(ImproveButton(app, full.Id));
        Assert.Equal("★2", Star(app, thin.Id)!.Text);
        var button = ImproveButton(app, thin.Id);
        Assert.NotNull(button);
        app.Shot("improve-beside-score");

        // the panel offers it too, and it goes as the description is typed to three stars
        var tile = app.Board.Show(thin.Id)!;
        app.Board.Open(tile);
        await DargApp.Settle();
        var editor = app.Board.Editor!;
        Assert.True(editor.ShowImprove);
        editor.Description += "\n\n**Done when:**\n- a folded column is still folded after a restart\n";
        Assert.False(editor.ShowImprove);
        app.Board.Editor = null;

        // pressing it does what the card's Improve button does
        tile.ImproveCommand.Execute(null);
        await app.Until(() => app.Agents.ForCard(thin.Id)?.Purpose == AgentPurposes.Improve, "an Improve agent on the thin card");
        Assert.False(app.Board.Show(thin.Id)!.ShowImprove); // an agent is on it now
    }
}
