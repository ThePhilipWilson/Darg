using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0166: a board card's header (id, quality star, type badge, priority, Run QA, Work on) in a 290px column
/// with everything showing at once. Each part gets its full width and none overlaps another; what doesn't fit wraps.</summary>
public class CardHeaderTests
{
    [AvaloniaFact]
    public async Task A_full_card_header_has_no_overlap_in_any_theme()
    {
        using var app = DargApp.Make();
        var card = app.Card("Android: decide the approach and scope", "QA Ready", type: "Story");
        BoardStore.Update(app.BoardFolder, card.Id, priority: "High");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        foreach (var theme in Themes.All)
        {
            await app.Api("theme", new { theme = theme.Id });
            await DargApp.Settle();

            var tile = app.Window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("card") && b.DataContext is CardViewModel c && c.Id == card.Id);
            app.Shot($"header-{theme.Id}");
            var parts = Parts(tile).ToList();
            Assert.True(parts.Count == 6, $"{theme.Id}: expected id, star, type, priority, Run QA and Work on; found {string.Join(", ", parts.Select(p => p.Name))}");

            foreach (var (name, control, box) in parts)
            {
                // DesiredSize carries the margin, Bounds doesn't
                Assert.True(control.Bounds.Width + control.Margin.Left + control.Margin.Right + 0.5 >= control.DesiredSize.Width,
                    $"{theme.Id}: {name} is squeezed to {control.Bounds.Width:0.#} of the {control.DesiredSize.Width - control.Margin.Left - control.Margin.Right:0.#} it needs");
                var inner = tile.Bounds.Deflate(tile.BorderThickness);
                var inTile = control.TranslatePoint(default, tile)!.Value;
                Assert.True(inTile.X >= -0.5 && inTile.X + control.Bounds.Width <= inner.Width + 0.5,
                    $"{theme.Id}: {name} runs out of the card ({inTile.X:0.#} to {inTile.X + control.Bounds.Width:0.#} in {inner.Width:0.#})");
            }
            for (var i = 0; i < parts.Count; i++)
                for (var j = i + 1; j < parts.Count; j++)
                {
                    var a = parts[i].Box.Inflate(1.5);
                    Assert.False(a.Intersects(parts[j].Box),
                        $"{theme.Id}: {parts[i].Name} {parts[i].Box} touches {parts[j].Name} {parts[j].Box}; tile {tile.Bounds}; all: {string.Join(" | ", parts.Select(q => q.Name + " " + q.Box))}");
                }
        }
    }

    /// <summary>The header's parts as drawn, each with its box in the card's coordinates.</summary>
    static IEnumerable<(string Name, Control Control, Rect Box)> Parts(Border tile)
    {
        var all = tile.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible).ToList();
        Rect Box(Control c) => new(c.TranslatePoint(default, tile)!.Value, c.Bounds.Size);
        var vm = (CardViewModel)tile.DataContext!;
        foreach (var t in all.OfType<TextBlock>())
        {
            if (t.Text == vm.Id && t.Classes.Contains("mono")) yield return ("id", t, Box(t));
            else if (t.Classes.Contains("quality")) yield return ("star", t, Box(t));
        }
        foreach (var b in all.OfType<Border>())
        {
            if (b.Classes.Contains("kind")) yield return ("type", b, Box(b));
            else if (b.Child is TextBlock { Text: "High" }) yield return ("priority", b, Box(b));
        }
        foreach (var b in all.OfType<Button>().Where(b => b.Classes.Contains("workon")))
            yield return ((string)b.Content! == "▶ Work on" ? "Work on" : "Run QA", b, Box(b));
    }
}
