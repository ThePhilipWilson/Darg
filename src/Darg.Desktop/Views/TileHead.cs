using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Darg.Desktop.Views;

/// <summary>A card tile's top line (WOR-0214): its first child (the id, star, type…, a wrapping panel) on the left
/// and its second (Run QA, Work on) on the right, as long as the first child's first item (the card's id) and each
/// of its items that can't trim fit beside them whole (<see cref="MustFit"/>). In a narrower tile (a state sub-column, a squeezed column) the buttons go on a line of their
/// own under it, so the id, which is what tells cards apart, is never cut.</summary>
public sealed class TileHead : Panel
{
    const double Gap = 4;
    const double LineGap = 4;

    /// <summary>The buttons went under the left part at the last measure.</summary>
    public bool Stacked { get; private set; }

    Control? Left => Children.Count > 0 ? Children[0] : null;
    Control? Right => Children.Count > 1 ? Children[1] : null;

    /// <summary>The width the left part can't go below: its first visible item, whole, and the gap a wrapping panel
    /// keeps after it; and every other item that can't trim (✦ Improve, a type or priority pill), whole, since a
    /// wrapping panel clips an item wider than its line. A trimming text (the board name) fits any width.
    /// WOR-0222: once the header's buttons were trimmed to fit a Feature pill, the id alone fit beside Run QA and
    /// Work on in a narrow sub-column, and ✦ Improve, wrapped under it in a strip as wide as the id, was cut.</summary>
    static double MustFit(Control left)
    {
        if (left is not Panel p) { left.Measure(Size.Infinity); return left.DesiredSize.Width; }
        var shown = p.Children.Where(c => c.IsVisible).ToList();
        if (shown.Count == 0) return 0;
        double Whole(Control c) { c.Measure(Size.Infinity); return c.DesiredSize.Width; }
        var need = Whole(shown[0]) + (p is WrapPanel w ? w.ItemSpacing : 0);
        foreach (var c in shown.Skip(1))
            if (c is not TextBlock { TextTrimming: var t } || t == TextTrimming.None)
                need = Math.Max(need, Whole(c));
        return need;
    }

    protected override Size MeasureOverride(Size available)
    {
        var left = Left;
        var right = Right;
        right?.Measure(Size.Infinity);
        var rw = right is { IsVisible: true } ? right.DesiredSize.Width : 0;
        var rh = right is { IsVisible: true } ? right.DesiredSize.Height : 0;
        if (left is null) { Stacked = false; return new Size(rw, rh); }
        var beside = Math.Max(0, available.Width - rw - Gap);
        Stacked = rw > 0 && !double.IsInfinity(available.Width) && beside < MustFit(left);
        if (Stacked)
        {
            left.Measure(new Size(available.Width, double.PositiveInfinity));
            return new Size(Math.Max(left.DesiredSize.Width, rw), left.DesiredSize.Height + LineGap + rh);
        }
        left.Measure(new Size(rw > 0 ? beside : available.Width, double.PositiveInfinity));
        var lw = left.DesiredSize.Width;
        return new Size(rw > 0 ? lw + Gap + rw : lw, Math.Max(left.DesiredSize.Height, rh));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var left = Left;
        var right = Right;
        var rw = right is { IsVisible: true } ? right.DesiredSize.Width : 0;
        var rh = right is { IsVisible: true } ? right.DesiredSize.Height : 0;
        if (Stacked)
        {
            var lh = left?.DesiredSize.Height ?? 0;
            left?.Arrange(new Rect(0, 0, final.Width, lh));
            right?.Arrange(new Rect(0, lh + LineGap, rw, rh));
            return final;
        }
        var lwidth = rw > 0 ? Math.Max(0, final.Width - rw - Gap) : final.Width;
        left?.Arrange(new Rect(0, 0, lwidth, final.Height));
        right?.Arrange(new Rect(final.Width - rw, 0, rw, rh));
        return final;
    }
}
