using Avalonia;
using Darg.Desktop.Views;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0149: each board has a tab; a plain click shows only that board, Ctrl+click adds or removes boards.</summary>
public class BoardTabsTests
{
    static string Rows(BoardViewModel b) => string.Join(",", b.Rows.Select(r => r.SelectedBoard));
    static string Lit(BoardViewModel b) => string.Join(",", b.Tabs.Where(t => t.IsShown).Select(t => t.Name));

    [AvaloniaFact]
    public async Task A_tab_per_board_click_shows_one_ctrl_click_adds_and_removes()
    {
        using var app = DargApp.Make();
        BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
        BoardStore.CreateBoard(app.Boards, "Site", DargApp.Columns, "S");
        await app.Start();
        var board = app.Board;
        Assert.Equal(["Android", "Main", "Site"], board.Tabs.Select(t => t.Name));
        Assert.Equal("Android", Lit(board));

        // plain click: just that board
        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        Assert.Equal("Main", Rows(board));
        Assert.Equal("Main", Lit(board));

        // Ctrl+click adds, side by side (the view tells the tab Ctrl was down)
        void CtrlClick(string name) { board.TabCtrl = true; board.Tabs.Single(t => t.Name == name).PickCommand.Execute(null); }
        CtrlClick("Site");
        CtrlClick("Android");
        Assert.Equal("Main,Android,Site", Rows(board));
        Assert.Equal("Android,Main,Site", Lit(board));

        // Ctrl+click on a showing board takes it off, the picked one included
        CtrlClick("Android");
        Assert.Equal("Main,Site", Rows(board));
        CtrlClick("Main");
        Assert.Equal("Site", Rows(board));
        CtrlClick("Site"); // never the last one
        Assert.Equal("Site", Rows(board));

        // a plain click after several shows only that one, and the choice is kept
        CtrlClick("Main");
        board.Tabs.Single(t => t.Name == "Android").PickCommand.Execute(null);
        Assert.Equal("Android", Rows(board));
        Assert.Empty(ShownBoards.Load("proj"));
    }

    [AvaloniaFact]
    public async Task The_board_dropdown_is_gone_and_tabs_are_drawn()
    {
        using var app = DargApp.Make();
        BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var tabs = app.Window.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is BoardTab).ToList();
        Assert.Equal(2, tabs.Count);
        Assert.DoesNotContain(app.Window.GetVisualDescendants().OfType<ComboBox>(), c => c.ItemsSource == app.Board.Boards);
        app.Shot("board-tabs");
    }

    static List<Button> RowTabs(DargApp app) => app.Window.GetVisualDescendants().OfType<Button>()
        .Where(b => b.DataContext is BoardTab && b.IsEffectivelyVisible && b.Bounds.Right > 0 && b.TranslatePoint(default, app.Window) is { X: >= 0 }).ToList();
    static Button? More(DargApp app) => app.Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "MoreBoards" && b.IsVisible);

    /// <summary>WOR-0193: boards that don't fit the row go in a ▾ n more menu; lit ones stay in the row.</summary>
    [AvaloniaFact]
    public async Task Tabs_that_dont_fit_go_in_a_more_menu()
    {
        using var app = DargApp.Make();
        var names = Enumerable.Range(1, 13).Select(i => $"Story board number {i:00} with a long name").ToList();
        foreach (var n in names) BoardStore.CreateBoard(app.Boards, n, DargApp.Columns, "B" + n.Substring(20, 2));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        app.Window.Width = 1280;
        await DargApp.Settle();
        var board = app.Board;
        Assert.Equal(14, board.Tabs.Count);

        var row = RowTabs(app);
        var more = More(app);
        Assert.NotNull(more);
        Assert.InRange(row.Count, 1, 13);
        Assert.Equal($"▾ {14 - row.Count} more", more!.Content);
        var width = app.Window.ClientSize.Width;
        Assert.All(row.Append(more), b => Assert.True(b.TranslatePoint(new Avalonia.Point(b.Bounds.Width, 0), app.Window)!.Value.X <= width, "inside the window"));
        foreach (var name in new[] { "+ Card", "Show completed" })
        {
            var c = app.Window.GetVisualDescendants().OfType<Control>().First(x => x is ContentControl { Content: string t } && t == name && x.IsEffectivelyVisible);
            Assert.True(c.TranslatePoint(new Avalonia.Point(c.Bounds.Width, 0), app.Window)!.Value.X <= width, name + " on screen");
        }
        app.Shot("board-tabs-overflow");

        // a hidden board is reached from the menu: it shows alone and is then a lit tab in the row
        var hidden = board.Tabs.Where(t => !row.Any(b => b.DataContext == t)).ToList();
        Assert.Equal(14 - row.Count, hidden.Count);
        var pick = hidden.Last();
        var panel = app.Window.GetVisualDescendants().OfType<OverflowTabsPanel>().Single();
        Assert.Equal(hidden.Select(t => t.Name), panel.Hidden.Select(t => t.Name));
        pick.PickCommand.Execute(null);
        await DargApp.Settle();
        Assert.Equal(pick.Name, Rows(board));
        Assert.Contains(RowTabs(app), b => b.DataContext == pick);
        Assert.DoesNotContain(panel.Hidden, t => t == pick);

        // ctrl+click on a row tab still adds
        var other = RowTabs(app).Select(b => (BoardTab)b.DataContext!).First(t => t != pick);
        board.TabCtrl = true; other.PickCommand.Execute(null);
        await DargApp.Settle();
        Assert.Equal(2, board.Tabs.Count(t => t.IsShown));
        Assert.All(board.Tabs.Where(t => t.IsShown), t => Assert.Contains(RowTabs(app), b => b.DataContext == t));

        // a narrower window moves tabs into the menu, a wider one back
        var before = RowTabs(app).Count;
        app.Window.Width = 1000; await DargApp.Settle();
        Assert.True(RowTabs(app).Count < before, "narrower: fewer in the row");
        Assert.Contains(RowTabs(app), b => ((BoardTab)b.DataContext!).IsShown);
        app.Window.Width = 5000; await DargApp.Settle();
        Assert.Null(More(app));
        Assert.Equal(14, RowTabs(app).Count);
    }

    [AvaloniaFact]
    public async Task Few_boards_have_no_more_button()
    {
        using var app = DargApp.Make();
        BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        Assert.Null(More(app));
        Assert.Equal(2, RowTabs(app).Count);
    }
}
