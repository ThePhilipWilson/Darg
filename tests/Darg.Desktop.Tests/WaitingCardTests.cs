using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0187: a card that waits on the person outside For Human Attention is outlined on the board, so it shows at a glance.</summary>
public class WaitingCardTests
{
    static string Decision() =>
        "## Decision\n\n**Question:** Does it work?\n**Asked by:** agent abc, 2026-11-22 00:00\n\n" +
        "- **Works** (recommended) → done: fine.\n- **Fails** → back: say why.\n\n**Context:** needs eyes.\n";

    static Border Tile(DargApp app, string id) => app.Window.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("card") && b.DataContext is CardViewModel c && c.Id == id);

    static string Colour(IBrush? b) => b is ISolidColorBrush s ? s.Color.ToString() : "?";
    static string Outline() => Avalonia.Application.Current!.TryGetResource("Accent", null, out var b) ? Colour(b as IBrush) : "?";

    [AvaloniaFact]
    public async Task A_card_with_an_open_decision_outside_attention_is_outlined()
    {
        using var app = DargApp.Make();
        var waiting = app.Card("asks from in progress", "In Progress", "Do it.\n\n" + Decision());
        var plain = app.Card("just working", "In Progress");
        var inAttention = app.Card("already in attention", BoardStore.AttentionName, "Do it.\n\n" + Decision());
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await DargApp.Settle();

        var tile = Tile(app, waiting.Id);
        Assert.Equal(Outline(), Colour(tile.BorderBrush));
        Assert.Equal(2, tile.BorderThickness.Left);
        Assert.NotEqual(Outline(), Colour(Tile(app, plain.Id).BorderBrush));
        Assert.NotEqual(Outline(), Colour(Tile(app, inAttention.Id).BorderBrush)); // the column already says so
        app.Shot("waiting");
    }
}
