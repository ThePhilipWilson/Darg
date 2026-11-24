using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0195 (sent back from QA): a column's cards are virtualized, so the card just below the view has no
/// control. A drop on the lower half of the bottom card shown, whose bottom edge is cut off, found no card after it
/// among the drawn ones and landed at the very end of the column. "Before" now comes from the list's items.</summary>
public class LongColumnDropTests
{
    [AvaloniaFact]
    public async Task Dropping_on_the_cut_off_bottom_card_of_a_long_column_lands_right_after_it()
    {
        using var app = DargApp.Make();
        var todo = BoardStore.Load(app.BoardFolder).Columns.First(c => c.Name == "To Do").Id;
        for (var i = 0; i < 60; i++) { var c = BoardStore.Create(app.BoardFolder, $"Card {i}"); BoardStore.Move(app.BoardFolder, c.Id, todo); }
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        var win = app.Window;
        var list = win.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("list") && b.IsEffectivelyVisible && b.DataContext is ListViewModel l && l.Name == "To Do");
        var sv = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        List<string> Order() => app.Board.Lists.First(l => l.Name == "To Do").Cards.Select(c => c.Id).ToList();
        double Y(Visual v, double dy = 0) => v.TranslatePoint(new Point(0, dy), win)!.Value.Y;
        List<Border> Shown()
        {
            var top = Y(sv); var bottom = top + sv.Viewport.Height;
            return list.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible)
                .Where(b => Y(b) + b.Bounds.Height > top && Y(b) < bottom).OrderBy(b => Y(b)).ToList();
        }

        sv.Offset = new Vector(0, 1500);
        await DargApp.Settle(); await DargApp.Settle();
        var viewBottom = Y(sv, sv.Viewport.Height);
        // scroll so the bottom card shown has its middle 22 px above the view's edge and its bottom cut off
        var last0 = Shown()[^1];
        sv.Offset = new Vector(0, sv.Offset.Y + (Y(last0, last0.Bounds.Height / 2) - (viewBottom - 22)));
        await DargApp.Settle(); await DargApp.Settle();
        var shown = Shown();
        var mover = (CardViewModel)shown[1].Tag!;
        var last = (CardViewModel)shown[^1].Tag!;
        Assert.True(Y(shown[^1], shown[^1].Bounds.Height) > viewBottom, "the bottom card should be cut off");
        var before = Order();
        var expected = before.Where(id => id != mover.Id).ToList();
        expected.Insert(expected.IndexOf(last.Id) + 1, mover.Id);

        var from = shown[1].TranslatePoint(new Point(shown[1].Bounds.Width / 2, shown[1].Bounds.Height / 2), win)!.Value;
        var to = new Point(from.X, viewBottom - 15);
        win.MouseMove(from);
        win.MouseDown(from, MouseButton.Left);
        win.MouseMove(from + new Point(0, 10));
        win.MouseMove(new Point(from.X, (from.Y + to.Y) / 2));
        win.MouseMove(to);
        await DargApp.Settle(0);
        win.MouseUp(to, MouseButton.Left);
        await DargApp.Settle();
        await app.Until(() => !Order().SequenceEqual(before), "the card moved");

        var after = Order();
        Assert.Equal(expected.IndexOf(mover.Id), after.IndexOf(mover.Id));
    }
}
