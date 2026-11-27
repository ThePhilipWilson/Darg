using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0222: a card on the board drew narrower than the others, and a thin Bug card's Bug pill sat on a line
/// of its own under ✦ Improve and ▶ Work on.</summary>
public class TileWidthTests
{
    static IEnumerable<Border> Tiles(DargApp app) => app.Window.GetVisualDescendants().OfType<Border>()
        .Where(b => b.Classes.Contains("card") && b.DataContext is CardViewModel && b.IsEffectivelyVisible);

    /// <summary>The cards' list was docked before the hidden By state list, the DockPanel's last child and so the one it
    /// fills, so the list was as wide as its widest card: a column of short cards drew them narrower, and a card with a
    /// longer title landing in it (as this card's own did) widened them all again.</summary>
    [AvaloniaFact]
    public async Task Every_tile_fills_its_column_whatever_its_cards_hold()
    {
        using var app = DargApp.Make();
        var shortOne = app.Card("short", "In Progress", "x");
        var longOne = app.Card("a card whose title is long enough to wrap onto a second line in its column", description: "x");
        app.Card("short too", description: "x");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await DargApp.Settle(300);

        var tiles = Tiles(app).ToList();
        Assert.Equal(3, tiles.Count);
        foreach (var t in tiles)
        {
            // the column's box, not the list's scroll viewer: that sat inside the list and shrank with it
            var column = t.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("list") && b.DataContext is ListViewModel);
            var full = column.Bounds.Width - 2 - 16; // its 1 px border and the list's 8 px margin, each side
            Assert.True(Math.Abs(t.Bounds.Width - full) < 0.5,
                $"{((CardViewModel)t.DataContext!).Id} is {t.Bounds.Width:0.#} px wide in a column with room for {full:0.#}");
        }

        // a card with a long title joining the short one's column changes nothing
        double Width() => Tiles(app).Single(t => ((CardViewModel)t.DataContext!).Id == shortOne.Id).Bounds.Width;
        var before = Width();
        await app.Api("work-on", new { project = "proj", card = longOne.Id });
        await DargApp.Settle(300);
        Assert.Equal(before, Width(), 0.5);
        app.Shot("tiles");
        Assert.Equal("", app.Crashes());
    }

    /// <summary>A thin card in a 290 px column: id, star, ✦ Improve and its type's pill fit beside ▶ Work on on one
    /// line. A Bug's missed by 1 px, the 6 px right margin after the last item; then Story's by 2 px and Feature's by
    /// 12, until the header's gaps and its buttons' padding were trimmed.</summary>
    [AvaloniaTheory]
    [InlineData("Bug")]
    [InlineData("Story")]
    [InlineData("Feature")]
    public async Task A_thin_cards_type_pill_stays_on_its_ids_line(string type)
    {
        using var app = DargApp.Make();
        var board = BoardStore.CreateBoard(app.Boards, "Wide", DargApp.Columns, "WOR");
        var bug = BoardStore.Create(board, "Agents on In Progress cards stop and don't restart", "Cards in In Progress stop.", type: type);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        app.Board.Tabs.Single(t => t.Name == "Wide").PickCommand.Execute(null);
        await DargApp.Settle(300);

        var tile = Tiles(app).Single(t => ((CardViewModel)t.DataContext!).Id == bug.Id);
        var vm = (CardViewModel)tile.DataContext!;
        Assert.True(vm.ShowImprove && vm.CanStart && vm.ShowsKind, $"the card shows ✦ Improve, ▶ Work on and its {type} pill");
        var head = tile.GetVisualDescendants().OfType<Views.TileHead>().Single();
        Assert.False(head.Stacked);
        Rect Box(Control c) => new(c.TranslatePoint(default, tile)!.Value, c.Bounds.Size);
        var id = tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == bug.Id);
        var pill = tile.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("kind"));
        var improve = tile.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "✦ Improve");
        app.Shot($"{type.ToLowerInvariant()}-header");
        string Parts() => $"tile {tile.Bounds.Width:0.#}, head {head.Bounds}, left {head.Children[0].Bounds}, right {head.Children[1].Bounds}; "
            + string.Join(" | ", ((Panel)head.Children[0]).Children.Where(c => c.IsVisible).Select(c => $"{c.GetType().Name} {Box(c)} wants {c.DesiredSize.Width:0.#}"));
        Assert.True(Box(pill).Top < Box(id).Bottom, $"the {type} pill {Box(pill)} is on a line under the id {Box(id)}: {Parts()}");
        Assert.True(Box(pill).Left > Box(improve).Right, $"the {type} pill {Box(pill)} comes after ✦ Improve {Box(improve)}");
        Assert.Equal("", app.Crashes());
    }
}
