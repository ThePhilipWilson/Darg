using Avalonia;
using Darg.Desktop.Views;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.Text.Json.Nodes;
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
        app.Window.Width = 1400; // WOR-0213: room for two tabs beside the sort buttons
        await DargApp.Settle();
        var board = app.Board;
        Assert.Equal(14, board.Tabs.Count);

        var row = RowTabs(app);
        var more = More(app);
        Assert.NotNull(more);
        Assert.InRange(row.Count, 1, 13);
        // the All tab (WOR-0214) is in the menu with the boards that don't fit
        Assert.Equal($"▾ {15 - row.Count} more", more!.Content);
        var width = app.Window.ClientSize.Width;
        Assert.All(row.Append(more), b => Assert.True(b.TranslatePoint(new Avalonia.Point(b.Bounds.Width, 0), app.Window)!.Value.X <= width, "inside the window"));
        foreach (var name in new[] { "+ Card", "Show completed" })
        {
            var c = app.Window.GetVisualDescendants().OfType<Control>().First(x => x is ContentControl { Content: string t } && t == name && x.IsEffectivelyVisible);
            Assert.True(c.TranslatePoint(new Avalonia.Point(c.Bounds.Width, 0), app.Window)!.Value.X <= width, name + " on screen");
        }
        app.Shot("board-tabs-overflow");

        // the button sits in the tab bar, level with the tabs and right after them, and opens a list of the hidden boards
        var tabsBox = app.Window.GetVisualDescendants().OfType<OverflowTabsPanel>().Single();
        var tp = tabsBox.TranslatePoint(default, app.Window)!.Value;
        var mp = more.TranslatePoint(default, app.Window)!.Value;
        var tabBox = row[0].TranslatePoint(default, app.Window)!.Value;
        Assert.InRange(mp.Y + more.Bounds.Height / 2, tabBox.Y + row[0].Bounds.Height / 2 - 1, tabBox.Y + row[0].Bounds.Height / 2 + 1);
        Assert.InRange(mp.X, tp.X + tabsBox.Bounds.Width, tp.X + tabsBox.Bounds.Width + 40);
        var centre = more.TranslatePoint(new Avalonia.Point(more.Bounds.Width / 2, more.Bounds.Height / 2), app.Window)!.Value;
        Assert.Same(more, app.Window.GetVisualAt(centre) is { } hit ? hit.FindAncestorOfType<Button>() : null);
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(app.Window, centre, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(app.Window, centre, Avalonia.Input.MouseButton.Left);
        await DargApp.Settle();
        Assert.True(more.Flyout!.IsOpen, "the list is open");
        Assert.Equal(15 - row.Count, ((MenuFlyout)more.Flyout).Items.Count);
        Assert.Equal("All", ((MenuItem)((MenuFlyout)more.Flyout).Items[0]!).Header);
        var popup = ((Avalonia.Controls.Primitives.PopupFlyoutBase)more.Flyout).Popup;
        var presenter = Assert.IsAssignableFrom<ItemsControl>(popup.Child);
        Assert.Equal(15 - row.Count, presenter.ItemCount);
        Assert.All(presenter.GetVisualDescendants().OfType<MenuItem>(), m => Assert.True(m.Bounds.Height > 0));
        Assert.Equal(15 - row.Count, presenter.GetVisualDescendants().OfType<MenuItem>().Count());
        app.Shot("board-tabs-overflow-open");
        more.Flyout.Hide();

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

    /// <summary>WOR-0213: a tab dragged along the row moves there, without clicking it; the order is kept, the rows
    /// follow it, a new board goes at the end, and A-Z and Old-new sort the tabs once.</summary>
    [AvaloniaFact]
    public async Task Tabs_drag_into_an_order_that_is_kept_and_sort_once()
    {
        using var app = DargApp.Make();
        var android = BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
        var site = BoardStore.CreateBoard(app.Boards, "Site", DargApp.Columns, "S");
        // made Site, then Main, then Android
        Directory.SetCreationTimeUtc(site, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Directory.SetCreationTimeUtc(app.BoardFolder, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        Directory.SetCreationTimeUtc(android, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await DargApp.Settle();
        var board = app.Board;
        string Names() => string.Join(",", board.Tabs.Select(t => t.Name));
        Assert.Equal("Android,Main,Site", Names());
        Assert.Equal("Android", Rows(board));

        // drag Android's tab past Site with the mouse: it moves to the end, and isn't clicked on the way
        Button Tab(string name) => RowTabs(app).Single(b => ((BoardTab)b.DataContext!).Name == name);
        var top = app.Window;
        Point Mid(Button b) => b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), top)!.Value;
        var from = Mid(Tab("Android"));
        var to = Mid(Tab("Site")) + new Point(Tab("Site").Bounds.Width / 2 - 2, 0);
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, from);
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, from, Avalonia.Input.MouseButton.Left);
        // the button held down as it moves, as a real drag is
        for (var x = 1; x <= 4; x++)
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, from + (to - from) * (x / 4.0), Avalonia.Input.RawInputModifiers.LeftMouseButton);
        app.Shot("board-tab-dragging");
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, to, Avalonia.Input.MouseButton.Left);
        await DargApp.Settle();
        Assert.Equal("Main,Site,Android", Names());
        Assert.Equal("Main,Site,Android", string.Join(",", board.Boards));
        Assert.Equal("Android", Rows(board)); // a drag, not a click: what shows is as it was
        Assert.Equal(["Main", "Site", "Android"], BoardOrder.Load("proj"));
        app.Shot("board-tab-dragged");

        // a plain click on a tab still picks it
        var main = Mid(Tab("Main"));
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, main, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, main, Avalonia.Input.MouseButton.Left);
        await DargApp.Settle();
        Assert.Equal("Main", Rows(board));

        // the rows below the picked one follow the tabs' order
        board.ShowAllBoardsCommand.Execute(null);
        Assert.Equal("Main,Site,Android", Rows(board));

        // a new board goes at the end, and a reopened board section has the same order
        BoardStore.CreateBoard(app.Boards, "Docs", DargApp.Columns, "D");
        await app.Until(() => board.Tabs.Count == 4, "the new board's tab");
        Assert.Equal("Main,Site,Android,Docs", Names());
        using (var again = new BoardViewModel(app.Project.Config))
            Assert.Equal("Main,Site,Android,Docs", string.Join(",", again.Tabs.Select(t => t.Name)));

        // the buttons sort once, and the sort is what's kept
        var docs = Path.Combine(app.Boards, "Docs");
        Directory.SetCreationTimeUtc(docs, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var byAge = top.GetVisualDescendants().OfType<Button>().Single(b => b.Command == board.SortBoardsByAgeCommand);
        Assert.True(byAge.IsEffectivelyVisible);
        byAge.Command!.Execute(null);
        Assert.Equal("Site,Main,Android,Docs", Names());
        Assert.Equal(["Site", "Main", "Android", "Docs"], BoardOrder.Load("proj"));
        board.SortBoardsByNameCommand.Execute(null);
        Assert.Equal("Android,Docs,Main,Site", Names());
        Assert.Equal(["Android", "Docs", "Main", "Site"], BoardOrder.Load("proj"));
        board.MoveBoard(3, 0);
        Assert.Equal("Site,Android,Docs,Main", Names());
        app.Shot("board-tabs-sorted");
    }

    /// <summary>WOR-0219: right-clicking a board tab offers Set as default; picked, Fettle's next card and the next
    /// note's ticket land on that board (and Haiku is asked from there, Main now one of the stories), and it's kept.</summary>
    [AvaloniaFact]
    public async Task Set_as_default_on_a_tab_makes_new_cards_land_there()
    {
        using var app = DargApp.Make();
        var art = BoardStore.CreateBoard(app.Boards, "Art", DargApp.Columns, "T");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await DargApp.Settle();
        var board = app.Board;
        BoardTab Tab(string name) => board.Tabs.Single(t => t.Name == name);

        // none set: Main, by its name, as before
        Assert.True(Tab("Main").IsDefault);
        Assert.False(Tab("Art").IsDefault);
        Assert.Equal(app.BoardFolder, board.InboxFolder);

        // right-click Art's tab: its menu has Set as default, unticked
        var artTab = RowTabs(app).Single(b => b.DataContext == Tab("Art"));
        var at = artTab.TranslatePoint(new Point(artTab.Bounds.Width / 2, artTab.Bounds.Height / 2), app.Window)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(app.Window, at, Avalonia.Input.MouseButton.Right);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(app.Window, at, Avalonia.Input.MouseButton.Right);
        await DargApp.Settle();
        var menu = artTab.ContextMenu!;
        Assert.True(menu.IsOpen, "the tab's menu is open");
        var item = menu.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "Set as default");
        Assert.False(((TextBlock)item.Icon!).IsVisible);
        app.Shot("board-tab-menu");
        item.Command!.Execute(null);
        menu.Close();
        await DargApp.Settle();
        Assert.True(Tab("Art").IsDefault);
        Assert.False(Tab("Main").IsDefault);
        Assert.Equal("Art", DefaultBoard.Load("proj"));
        Assert.Equal(art, board.InboxFolder);
        // opened again, the menu marks it
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(app.Window, at, Avalonia.Input.MouseButton.Right);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(app.Window, at, Avalonia.Input.MouseButton.Right);
        await DargApp.Settle();
        Assert.True(menu.IsOpen);
        Assert.True(((TextBlock)item.Icon!).IsVisible);
        app.Shot("board-tab-menu-default");
        menu.Close();
        await DargApp.Settle();

        // Fettle's next card lands on Art, and Haiku is asked whether it belongs on Main, the other board
        var reply = JsonNode.Parse(await app.Api("new-card", new { project = "proj", title = "Paint the splash screen" }))!.AsObject();
        Assert.Equal("Art", (string?)reply["board"]);
        Assert.Contains(BoardStore.ReadCards(art), c => c.Title == "Paint the splash screen");
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "ask" && ((string?)c["text"] ?? "").Contains("Board: Main")
            && ((string?)c["text"] ?? "").Contains("Paint the splash screen")), "Haiku asked from the default board, offering Main");

        // and so does the next note's ticket
        var notes = app.Project.Notes!;
        notes.NewNoteCommand.Execute(null);
        notes.Editor!.Text = "the logo is blurry on the start screen";
        notes.Editor.MakeTicketCommand.Execute(null);
        await DargApp.Settle();
        Assert.Contains(BoardStore.ReadCards(art), c => c.Title.Contains("the logo is blurry"));
        Assert.DoesNotContain(BoardStore.ReadCards(app.BoardFolder), c => c.Title.Contains("the logo is blurry"));

        // kept for the project: a board section opened again (a restart) has Art as the default
        using (var again = new BoardViewModel(app.Project.Config))
        {
            Assert.True(again.Tabs.Single(t => t.Name == "Art").IsDefault);
            Assert.Equal(art, again.InboxFolder);
        }

        // a default whose board has since gone or been renamed: back to Main by name
        DefaultBoard.Save("proj", "Artwork");
        using (var renamed = new BoardViewModel(app.Project.Config))
        {
            Assert.Equal(app.BoardFolder, renamed.InboxFolder);
            Assert.True(renamed.Tabs.Single(t => t.Name == "Main").IsDefault);
        }
    }
}
