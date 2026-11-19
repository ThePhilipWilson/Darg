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
}
