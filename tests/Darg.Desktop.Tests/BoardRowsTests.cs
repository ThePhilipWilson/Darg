using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0160: boards shown together have a divider each that drags to resize them (kept per board), the
/// rows scroll once taller than the screen, and more than two fit at once.</summary>
public class BoardRowsTests
{
    static Border Divider(DargApp app, string board) =>
        app.Window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("rowdivider") && b.Tag is BoardViewModel r && r.SelectedBoard == board && b.IsEffectivelyVisible);

    static Point Middle(Visual v, Visual top) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), top)!.Value;

    static void Settle() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public async Task Dividers_drag_rows_taller_three_fit_and_the_rows_scroll()
    {
        using var app = DargApp.Make();
        foreach (var name in new[] { "Android", "Site" })
        {
            var folder = BoardStore.CreateBoard(app.Boards, name, DargApp.Columns, name[..1]);
            for (var i = 0; i < 12; i++) BoardStore.Create(folder, $"{name} card {i}");
        }
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var board = app.Board;
        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        foreach (var name in new[] { "Android", "Site" }) { board.TabCtrl = true; board.Tabs.Single(t => t.Name == name).PickCommand.Execute(null); }
        Settle();
        Assert.Equal(["Main", "Android", "Site"], board.Rows.Select(r => r.SelectedBoard));

        var scroll = app.Window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "RowsScroll");
        await app.Until(() => scroll.Bounds.Height > 0 && board.Rows.All(r => r.RowHeight > 0), "the rows are laid out");
        var space = scroll.Bounds.Height;
        // three share the screen: none scrolls off (it was two, at 320 each)
        Assert.True(board.Rows.Sum(r => r.RowHeight) <= space + 1, $"rows {string.Join(",", board.Rows.Select(r => r.RowHeight))} in {space}");
        app.Shot("three-boards");

        // drag Main's divider down 200: Main grows, the others share what is left
        var top = app.Window;
        var main = board.Rows[0];
        var before = main.RowHeight;
        var at = Middle(Divider(app, "Main"), top);
        top.MouseMove(at);
        top.MouseDown(at, MouseButton.Left);
        top.MouseMove(at + new Point(0, 100));
        top.MouseMove(at + new Point(0, 200));
        top.MouseUp(at + new Point(0, 200), MouseButton.Left);
        Settle();
        Assert.Equal(before + 200, main.RowHeight, 1);
        Assert.Equal(before + 200, BoardRowHeights.Load("proj")["Main"], 1);
        Assert.All(board.Rows.Skip(1), r => Assert.True(r.RowHeight >= BoardRowHeights.FitMin));

        // now taller than the screen: the rows scroll
        await app.Until(() => scroll.Extent.Height > scroll.Viewport.Height + 1, "the rows scroll");
        app.Shot("main-dragged-taller");

        // the wheel over a board's empty column (nothing of its own to scroll) scrolls the rows
        var empty = app.Window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("list") && b.IsEffectivelyVisible && b.Tag is string key && key.StartsWith("Main/") && b.DataContext is ListViewModel { Name: "To Do" });
        var offset = scroll.Offset.Y;
        top.MouseWheel(Middle(empty, top), new Vector(0, -3));
        Settle();
        Assert.True(scroll.Offset.Y > offset, $"the rows scrolled from {offset} to {scroll.Offset.Y}");

        // a reopened board section keeps the height it was dragged to
        board.Tabs.Single(t => t.Name == "Android").PickCommand.Execute(null);
        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        board.TabCtrl = true; board.Tabs.Single(t => t.Name == "Site").PickCommand.Execute(null);
        Settle();
        Assert.Equal(before + 200, board.Rows[0].RowHeight, 1);

        // a double-click on the divider shares the height again (the headless pointer never counts a second click, so its command)
        board.Rows[0].ResetRowHeightCommand.Execute(null);
        Settle();
        Assert.False(BoardRowHeights.Load("proj").ContainsKey("Main"));
        Assert.Equal(board.Rows[0].RowHeight, board.Rows[1].RowHeight, 1);
    }
}
