using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0214: an All board with every card of the project, its columns every board's merged by name, and a
/// By state switch on any board that splits each column into a narrow sub-column per state.</summary>
public class AllBoardTests
{
    static ListViewModel List(BoardViewModel row, string name) => row.Lists.Single(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    static string Ids(IEnumerable<CardViewModel> cards) => string.Join(",", cards.Select(c => c.Id).Order());
    static string Groups(ListViewModel list) => string.Join(" | ", list.Groups.Select(g => $"{g.Name}: {Ids(g.Cards)}"));
    static IEnumerable<TextBlock> Shown(DargApp app) => app.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible);

    [AvaloniaFact]
    public async Task The_All_tab_shows_every_boards_cards_on_one_board_and_each_card_stays_on_its_own()
    {
        using var app = DargApp.Make();
        var android = BoardStore.CreateBoard(app.Boards, "Android", ["To Do", "In Progress", "Design", "Done"], "A");
        var m1 = app.Card("main in backlog");
        var m2 = app.Card("main at work", "In Progress");
        var a1 = BoardStore.Create(android, "android at work", "");
        BoardStore.Move(android, a1.Id, BoardStore.Load(android).Columns.Single(c => c.Name == "In Progress").Id);
        var a2 = BoardStore.Create(android, "android in design", "");
        BoardStore.Move(android, a2.Id, BoardStore.Load(android).Columns.Single(c => c.Name == "Design").Id);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var board = app.Board;
        Assert.True(board.HasAllTab);

        board.PickAllBoardCommand.Execute(null);
        await DargApp.Settle(200);
        var all = Assert.Single(board.ShownRows);
        Assert.Same(board.AllRow, all);
        // a column only one board has sits between its neighbours there, each name once
        var names = all.Lists.Select(l => l.Name).ToList();
        Assert.InRange(names.IndexOf("Design"), names.IndexOf("In Progress") + 1, names.IndexOf("Done") - 1);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(string.Join(",", new[] { m2.Id, a1.Id }.Order()), Ids(List(all, "In Progress").Cards));
        Assert.Equal(m1.Id, Ids(List(all, "Backlog").Cards));
        Assert.Equal("4 of 4 cards on 2 boards", board.CountText);
        // each tile names its board, and the All board's lists can't be renamed, removed or minimized
        var a1Tile = List(all, "In Progress").Cards.Single(c => c.Id == a1.Id);
        Assert.Equal("Android", a1Tile.BoardName);
        Assert.True(a1Tile.ShowsBoard);
        Assert.Contains(Shown(app), t => t.Text == "Android" && t.DataContext == a1Tile);
        Assert.All(all.Lists, l => Assert.False(l.IsColumn || l.CanMinimize));
        app.Shot("all-board");

        // a drop on an All list moves each card on its own board; one whose board lacks the list stays, named
        all.Drop([a1Tile], List(all, "Done"), null);
        await app.Until(() => BoardStore.ReadCards(android).Single(c => c.Id == a1.Id).Status == "Done", "the Android card to reach Android's Done");
        Assert.DoesNotContain(BoardStore.ReadCards(app.BoardFolder), c => c.Id == a1.Id);
        all.Drop([List(all, "Backlog").Cards.Single()], List(all, "Design"), null);
        Assert.True(board.Status.Contains($"{m1.Id} (Main has no Design)"), board.Status);
        Assert.Equal("Backlog", app.Read(m1.Id).Status);

        // a board's tab leaves the All board; it is kept, so the app opens to it again
        Assert.True(BoardViews.Load("proj").All);
        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        Assert.False(board.ShowingAll);
        Assert.Equal(["Main"], board.ShownRows.Select(r => r.SelectedBoard));
        Assert.False(BoardViews.Load("proj").All);
        board.PickAllBoardCommand.Execute(null);
        Assert.True(board.ShowingAll);
        // a board picked another way (Design's link to a plan's board) shows that board
        board.SelectedBoard = "Android";
        Assert.False(board.ShowingAll);
        Assert.Equal(["Android"], board.ShownRows.Select(r => r.SelectedBoard));
        board.PickAllBoardCommand.Execute(null);
        using var again = new BoardViewModel(app.Project.Config);
        Assert.True(again.ShowingAll);
        Assert.Equal("", app.Crashes());
    }

    /// <summary>The All tab leads the board tabs; when they overflow it goes into the ▾ more menu with them, unless it's showing.</summary>
    [AvaloniaFact]
    public async Task The_All_tab_goes_into_the_more_menu_when_the_tabs_overflow_unless_it_is_showing()
    {
        using var app = DargApp.Make();
        foreach (var i in Enumerable.Range(1, 6)) BoardStore.CreateBoard(app.Boards, $"Story board number {i:00} with a long name", DargApp.Columns, $"B{i}");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var row = app.Window.GetVisualDescendants().OfType<Darg.Desktop.Views.OverflowRow>().Single();
        Button AllTab() => app.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "All" && b.DataContext is BoardViewModel);
        bool InRow(Button b) => b.IsHitTestVisible && b.TranslatePoint(default, app.Window) is { X: >= 0 };

        app.Window.Width = 5000; await DargApp.Settle();
        Assert.True(InRow(AllTab()), "wide: All leads the row");
        Assert.False(row.More.IsVisible);
        // All comes before the first tab
        var first = app.Window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is BoardTab && b.IsHitTestVisible);
        Assert.True(AllTab().TranslatePoint(default, app.Window)!.Value.X < first.TranslatePoint(default, app.Window)!.Value.X);

        app.Window.Width = 1000; await DargApp.Settle();
        Assert.False(InRow(AllTab()), "narrow: All is in the menu");
        Assert.True(row.More.IsVisible);
        Assert.Contains(app.Window.GetVisualDescendants().OfType<Button>(), b => b.DataContext is BoardTab t && t.IsShown && InRow(b));
        var menu = (MenuFlyout)row.More.Flyout!;
        var item = Assert.IsType<MenuItem>(menu.Items[0]);
        Assert.Equal("All", item.Header);
        item.Command!.Execute(null);
        await DargApp.Settle();
        Assert.True(app.Board.ShowingAll);
        Assert.True(InRow(AllTab()), "showing, All stays in the row");
        app.Shot("all-tab-narrow");
        Assert.Equal("", app.Crashes());
    }

    /// <summary>QA found ids cut to "WOR-0:" in a sub-column, where Run QA and Work on took the line: there the buttons
    /// go under the id, which shows whole; a long board name is trimmed; the footer says all boards on All.</summary>
    [AvaloniaFact]
    public async Task In_a_narrow_sub_column_a_tiles_id_shows_whole_and_the_footer_says_all_boards()
    {
        using var app = DargApp.Make();
        var other = BoardStore.CreateBoard(app.Boards, "Agents and Fettle chats travel between machines", DargApp.Columns, "AGE");
        var qa = app.Card("waiting for its check", "QA Ready");
        var far = BoardStore.Create(other, "a card from the long-named board", "");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var board = app.Board;
        app.Window.Width = 1500;
        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        await DargApp.Settle(200);
        Assert.EndsWith(" · board Main", app.Main.FooterLine);

        TextBlock IdOf(string id) => Shown(app).Single(t => t.Text == id);
        Views.TileHead? HeadOf(TextBlock t) => t.GetVisualAncestors().OfType<Views.TileHead>().FirstOrDefault();
        // unsplit, the column is wide enough: the buttons sit beside the id
        Assert.False(HeadOf(IdOf(qa.Id))?.Stacked ?? false);

        board.PickAllBoardCommand.Execute(null);
        board.ActiveSplit = true;
        await DargApp.Settle(200);
        Assert.EndsWith(" · all boards", app.Main.FooterLine);
        Assert.True(List(board.AllRow!, "QA Ready").IsSplit);
        foreach (var id in new[] { qa.Id, far.Id })
        {
            // the id and the board name sit in a wrapping panel that clips what runs past its edge
            var t = IdOf(id);
            var box = (Control)t.GetVisualParent()!;
            double RightEdge(Control c) => c.TranslatePoint(new Point(c.Bounds.Width, 0), box)!.Value.X;
            t.Measure(Size.Infinity);
            var whole = t.DesiredSize.Width - t.Margin.Left - t.Margin.Right; // DesiredSize carries the margin, Bounds doesn't
            Assert.True(t.Bounds.Width >= whole - 0.5, $"{id} is cut: {t.Bounds.Width} of {whole}");
            Assert.True(RightEdge(t) <= box.Bounds.Width + 0.5, $"{id} is cut at the edge of its part of the line: {RightEdge(t)} of {box.Bounds.Width}");
            // the long board name is trimmed to fit, not run under the edge
            var name = Shown(app).Single(b => b.DataContext is CardViewModel c && c.Id == id && b.Text == c.BoardName);
            Assert.True(RightEdge(name) <= box.Bounds.Width + 0.5, $"{id}'s board name is cut at the edge: {RightEdge(name)} of {box.Bounds.Width}");
        }
        app.Shot("narrow-sub-column");
        var head = HeadOf(IdOf(qa.Id))!;
        Assert.True(head.Stacked, $"Run QA and Work on go under the id in a sub-column: head {head.Bounds}, "
            + string.Join(" | ", head.Children.Select(c => $"{c.GetType().Name} {c.Bounds} wants {c.DesiredSize.Width:0.#}")));

        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        Assert.EndsWith(" · board Main", app.Main.FooterLine);
        Assert.Equal("", app.Crashes());
    }

    /// <summary>QA round 2: with All showing, the API's work-on started the agent on the board picked underneath (Main),
    /// so a card on another board stayed in its backlog and its agent was given a card file that doesn't exist.</summary>
    [AvaloniaFact]
    public async Task With_All_showing_the_APIs_card_commands_act_on_each_cards_own_board()
    {
        using var app = DargApp.Make();
        var android = BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
        string Column(string name) => BoardStore.Load(android).Columns.Single(c => c.Name == name).Id;
        var one = BoardStore.Create(android, "android card worked on alone", "");
        var two = BoardStore.Create(android, "android card worked on in a list", "");
        var mainCard = app.Card("main card worked on in a list");
        var checking = BoardStore.Create(android, "android card in QA", "The logo's edges are jagged.\n\n## How to check\n\nLook at the logo.\n");
        BoardStore.Move(android, checking.Id, Column("QA Ready"));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var board = app.Board;
        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        board.PickAllBoardCommand.Execute(null);
        await DargApp.Settle(200);
        Assert.True(board.ShowingAll);
        Assert.Equal("Main", board.SelectedBoard); // the board picked underneath isn't the cards' board
        string Status(string id) => BoardStore.ReadCards(android).Single(c => c.Id == id).Status;
        var androidItems = Path.Combine(android, "items");

        await app.Api("work-on", new { project = "proj", card = one.Id, force = true });
        Assert.Equal("In Progress", Status(one.Id));
        await app.Until(() => app.Prompts().Any(p => p.Contains(one.Id)), $"{one.Id}'s agent to be sent its prompt");
        Assert.Contains(Path.Combine(androidItems, one.File), app.Prompts().First(p => p.Contains(one.Id)));
        Assert.DoesNotContain("Couldn't move", board.Status);

        // the list form finds each card on its own board too
        await app.Api("work-on", new { project = "proj", cards = new[] { two.Id, mainCard.Id } });
        Assert.Equal("In Progress", Status(two.Id));
        Assert.Equal("In Progress", app.Read(mainCard.Id).Status);
        Assert.NotNull(app.Agents.ForCard(two.Id));
        Assert.NotNull(app.Agents.ForCard(mainCard.Id));
        await app.Until(() => app.Prompts().Any(p => p.Contains(Path.Combine(androidItems, two.File))), $"{two.Id}'s agent to be given its own card file");

        // send-back checks the card's own board's QA column, and moves it there
        await app.Api("send-back", new { project = "proj", card = checking.Id, notes = "still jagged at 150% zoom" });
        Assert.Equal("In Progress", Status(checking.Id));
        Assert.Contains("still jagged at 150% zoom", File.ReadAllText(Path.Combine(androidItems, checking.File)));
        Assert.Equal(AgentPurposes.Rework, app.Agents.ForCard(checking.Id)?.Purpose);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task By_state_splits_each_column_into_a_sub_column_per_state_kept_per_board()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
            var blocker = app.Card("the api", "In Progress");
            var waits = app.Card("the screen that needs the api");
            BoardStore.SetBlockedBy(app.BoardFolder, waits.Id, [blocker.Id]);
            var free = app.Card("a free card");
            var worker = app.Card("busy", "In Progress");
            var asker = app.Card("FAKE_ASK asks first", "In Progress", "FAKE_ASK");
            var waiting = app.Card("waits on a decision", BoardStore.AttentionName);
            var done = app.Card("finished", "Done");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });
            var board = app.Board;
            board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
            Assert.True(board.ShowsSectionSplit);
            board.ActiveSplit = true;
            await DargApp.Settle(100);
            Assert.Equal($"Ready to go: {blocker.Id},{worker.Id},{asker.Id}", Groups(List(board, "In Progress")));
            await app.Api("work-on", new { project = "proj", card = worker.Id });
            await app.Api("work-on", new { project = "proj", card = asker.Id });
            Assert.Equal($"Blocked: {waits.Id} | Ready to go: {free.Id}", Groups(List(board, "Backlog")));
            Assert.Equal("Needs you: " + waiting.Id, Groups(List(board, BoardStore.AttentionName)));
            Assert.False(List(board, "Done").IsSplit); // finished cards don't split
            Assert.Equal(done.Id, Ids(List(board, "Done").Cards));
            // the asker's agent asks a moment after it starts: its card moves over without a reload of the board
            await app.Until(() => Groups(List(board, "In Progress")) == $"Ready to go: {blocker.Id} | Agent working: {worker.Id} | Needs you: {asker.Id}",
                "In Progress split into ready, working and needs you: " + Groups(List(board, "In Progress")));
            Assert.True(List(board, "In Progress").Width > 3 * StateGroup.Width);
            // drawn: a sub-column heading per state
            Assert.Contains(Shown(app), t => t.Classes.Contains("statehead") && t.Text == "Blocked");
            Assert.Contains(Shown(app), t => t.Classes.Contains("statehead") && t.Text == "Agent working");
            app.Shot("split-by-state");

            // kept for that board only
            Assert.Contains("Main", BoardViews.Load("proj").Split);
            board.Tabs.Single(t => t.Name == "Android").PickCommand.Execute(null);
            Assert.False(board.ActiveSplit);
            Assert.All(board.Lists, l => Assert.False(l.IsSplit));
            board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
            Assert.True(board.ActiveSplit);
            Assert.True(List(board, "Backlog").IsSplit);

            // the All board keeps its own
            board.PickAllBoardCommand.Execute(null);
            await DargApp.Settle(100);
            Assert.False(board.ActiveSplit);
            board.ActiveSplit = true;
            Assert.Equal($"Blocked: {waits.Id} | Ready to go: {free.Id}", Groups(List(board.AllRow!, "Backlog")));
            Assert.Contains(BoardViews.AllKey, BoardViews.Load("proj").Split);
            app.Shot("all-split");
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); } // the Sandbox default: unset, fake claude turns run 60 s
    }
}
