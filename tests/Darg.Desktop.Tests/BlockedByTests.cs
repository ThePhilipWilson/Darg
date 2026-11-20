using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0172: a card that waits on another shows a chip naming it on its tile until the blocker reaches QA or done;
/// the panel lists Blocked by and Blocks as links and edits Blocked by; the API carries both.</summary>
public class BlockedByTests
{
    static IReadOnlyList<TextBlock> Chips(DargApp app, string id) => app.Window.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.IsEffectivelyVisible && t.DataContext is BlockerState && t.FindAncestorOfType<Border>() is { } b && b.Classes.Contains("blocker")
                    && t.FindAncestorOfType<ItemsControl>()?.DataContext is CardViewModel c && c.Id == id).ToList();

    static IBrush? Theme(string key) => Avalonia.Application.Current!.TryGetResource(key, null, out var b) ? b as IBrush : null;
    static string Colour(IBrush? b) => b is ISolidColorBrush s ? s.Color.ToString() : "?";

    [AvaloniaFact]
    public async Task A_tile_names_its_open_blocker_until_it_reaches_QA()
    {
        using var app = DargApp.Make();
        var blocker = app.Card("remote api", "In Progress");
        var waits = app.Card("pairing in the phone app", "To Do");
        var free = app.Card("icon");
        BoardStore.SetBlockedBy(app.BoardFolder, waits.Id, [blocker.Id, "ZZ-0001"]);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        var chips = Chips(app, waits.Id);
        Assert.Equal([$"waits on {blocker.Id}", "ZZ-0001: no such card"], chips.Select(t => t.Text));
        Assert.All(chips, t => Assert.Equal(Colour(Theme("Warn")), Colour(t.Foreground)));
        Assert.Contains("remote api", ToolTip.GetTip(chips[0].FindAncestorOfType<Border>()!) as string);
        Assert.Empty(Chips(app, free.Id));
        Assert.Empty(Chips(app, blocker.Id));
        app.Shot("blocked");

        // the API says so too, both ways
        var cards = JsonNode.Parse(await app.Api("cards", new { project = "proj" }))!.AsArray();
        var row = cards.Single(c => (string?)c!["id"] == waits.Id)!;
        Assert.True((bool)row["blocked"]!);
        Assert.Equal([blocker.Id, "ZZ-0001"], row["blockedBy"]!.AsArray().Select(b => (string?)b!["id"]));
        Assert.False((bool)row["blockedBy"]![1]!["found"]!);
        Assert.Equal([waits.Id], cards.Single(c => (string?)c!["id"] == blocker.Id)!["blocks"]!.AsArray().Select(b => (string?)b!["id"]));
        var boards = JsonNode.Parse(await app.Api("boards", new { project = "proj" }))!.AsArray();
        var onPhone = boards[0]!["lists"]!.AsArray().SelectMany(l => l!["cards"]!.AsArray()).Single(c => (string?)c!["id"] == waits.Id)!;
        Assert.Equal(blocker.Id, (string?)onPhone["blockedBy"]![0]!["id"]);
        var one = JsonNode.Parse(await app.Api("card", new { project = "proj", id = blocker.Id }))!;
        Assert.Equal(waits.Id, (string?)one["blocks"]![0]!["id"]);

        // the blocker reaches QA: its chip goes, the unknown id's warning stays
        await app.Api("move-card", new { project = "proj", card = blocker.Id, column = "qa" });
        await app.Until(() => Chips(app, waits.Id).Count == 1, "the cleared blocker's chip to go");
        Assert.Equal("ZZ-0001: no such card", Chips(app, waits.Id).Single().Text);
        app.Shot("cleared");
    }

    [AvaloniaFact]
    public async Task A_blocker_on_another_board_clears_when_it_moves_there()
    {
        using var app = DargApp.Make();
        var phone = BoardStore.CreateBoard(app.Boards, "Phone", DargApp.Columns, "T");
        var scaffold = BoardStore.Create(phone, "scaffold");
        var waits = app.Card("board view", "To Do");
        BoardStore.SetBlockedBy(app.BoardFolder, waits.Id, [scaffold.Id]);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        app.Board.SelectedBoard = "Main";
        await DargApp.Settle();
        Assert.Equal($"waits on {scaffold.Id}", Chips(app, waits.Id).Single().Text);

        // moved on a board that isn't showing: Main's tile follows
        BoardStore.Move(phone, scaffold.Id, Board.Completed);
        await app.Until(() => Chips(app, waits.Id).Count == 0, "the chip to go when the blocker on Phone is done");
    }

    [AvaloniaFact]
    public async Task The_panel_lists_both_ways_as_links_and_edits_Blocked_by()
    {
        using var app = DargApp.Make();
        var api = app.Card("remote api", "In Progress");
        var scaffold = app.Card("scaffold", "QA Ready");
        var waits = app.Card("pairing", "To Do");
        BoardStore.SetBlockedBy(app.BoardFolder, waits.Id, [api.Id]);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        CardViewModel Vm(string id) => app.Board.Rows.SelectMany(r => r.Lists).SelectMany(l => l.Cards).First(c => c.Id == id);
        app.Board.Open(Vm(waits.Id));
        await DargApp.Settle();
        var editor = app.Board.Editor!;
        Assert.Equal(api.Id, editor.BlockedByText);
        Assert.Contains($"{api.Id}: remote api (In Progress", editor.BlockedByLinks.Single().Text);
        Assert.True(editor.BlockedByLinks.Single().IsOpen);
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith($"Blocked by {api.Id}: remote api") == true);
        app.Shot("panel");

        // typed in the panel and saved: the line is written, the tile shows both
        editor.BlockedByText = $"{api.Id}, {scaffold.Id.ToLowerInvariant()}";
        editor.SaveCommand.Execute(null);
        Assert.Equal([api.Id, scaffold.Id], app.Read(waits.Id).BlockedBy);
        await app.Until(() => Chips(app, waits.Id).Count == 1, "the tile to show the open blocker (scaffold is in QA, so cleared)");
        Assert.Equal($"waits on {api.Id}", Chips(app, waits.Id).Single().Text);

        // a card can't wait on itself
        app.Board.Open(Vm(waits.Id));
        await DargApp.Settle();
        app.Board.Editor!.BlockedByText = waits.Id;
        app.Board.Editor.SaveCommand.Execute(null);
        Assert.Contains("can't wait on itself", app.Board.Editor!.Error);
        Assert.Equal([api.Id, scaffold.Id], app.Read(waits.Id).BlockedBy);
        app.Board.Editor.CancelCommand.Execute(null);

        // the blocker's panel lists what it blocks, and the link opens that card
        app.Board.Open(Vm(api.Id));
        await DargApp.Settle();
        var link = app.Board.Editor!.BlocksLinks.Single();
        Assert.Equal(waits.Id, link.Id);
        Assert.Contains("waiting on this", link.Text);
        link.Open.Execute(null);
        await DargApp.Settle();
        Assert.Equal(waits.Id, app.Board.Editor!.CardId);
    }
}
