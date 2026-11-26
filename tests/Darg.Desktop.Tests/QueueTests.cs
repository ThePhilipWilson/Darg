using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0212: a named queue of cards, worked one at a time. Made from a board in dependency order with no model,
/// run, the next card starting once the one before is done or in QA with nothing waiting on that QA, paused when a card's
/// agent stops without finishing, and carried on past it with Resume.</summary>
public class QueueTests
{
    static void BlockedBy(DargApp app, Card card, params string[] ids)
    {
        var file = Path.Combine(app.BoardFolder, "items", card.File);
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        var at = text.IndexOf("- **Labels:**", StringComparison.Ordinal);
        var end = text.IndexOf('\n', at);
        File.WriteAllText(file, text.Insert(end, $"\n- **Blocked by:** {string.Join(", ", ids)}"));
    }

    /// <summary>The card's tile, in its list or (queued, WOR-0220) in its queue's lane.</summary>
    static CardViewModel? Tile(DargApp app, string id) => InLists(app, id) ?? app.Board.LaneTile(id);
    static CardViewModel? InLists(DargApp app, string id) => app.Board.Rows.SelectMany(r => r.Lists).SelectMany(l => l.Cards).FirstOrDefault(c => c.Id == id);
    static AgentViewModel? Live(DargApp app, string id) => app.Agents.ForCard(id);
    static CardQueue Queue(DargApp app) => app.Project.Queues!.All.Single();

    /// <summary>WOR-0239: a queue's lane shows only while a board holding one of its cards is showing, or the All board is.</summary>
    [AvaloniaFact]
    public async Task A_queue_shows_only_when_its_board_or_the_All_board_is_showing()
    {
        using var app = DargApp.Make();
        var android = BoardStore.CreateBoard(app.Boards, "Android", ["To Do", "In Progress", "Done"], "A");
        var main = app.Card("a main card", "Backlog");
        var other = BoardStore.Create(android, "an android card", "");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Travel", cards = new[] { other.Id } });
        var qs = app.Project.Queues!;
        var lane = qs.Queues.Single();
        var board = app.Board;

        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        await app.Until(() => !lane.IsShown, "the Android queue to hide while Main shows");
        Assert.False(qs.ShowsStrip);
        board.Tabs.Single(t => t.Name == "Android").PickCommand.Execute(null);
        await app.Until(() => lane.IsShown && qs.ShowsStrip, "the queue to show on its own board");
        board.Tabs.Single(t => t.Name == "Main").PickCommand.Execute(null);
        await app.Until(() => !lane.IsShown, "it hides again");
        board.PickAllBoardCommand.Execute(null);
        await app.Until(() => lane.IsShown && qs.ShowsStrip, "the queue to show on the All board");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_queue_runs_its_cards_in_dependency_order_one_at_a_time_and_pauses_on_a_card_left_unfinished()
    {
        using var app = DargApp.Make();
        using var turns = app.HoldTurns(); // the first card's agent at work until its card is in QA
        {
            var second = app.Card("builds on the first", "Backlog");
            var first = app.Card("the first piece", "Backlog");
            var third = app.Card("separate work", "Backlog");
            BlockedBy(app, second, first.Id);
            await app.Start();
            var asked = new List<string>();
            app.Project.Queues!.Ask = prompt => { asked.Add(prompt); return Task.FromResult<string?>("NOT BLOCKING: separate pieces"); };
            await app.Api("show", new { project = "proj", section = "Board" });

            // made from the board: the blocker first, though it's later on the board; it waits for Run
            var made = JsonNode.Parse(await app.Api("queue-new", new { project = "proj", name = "Travel" }))!;
            Assert.Equal([first.Id, second.Id, third.Id], made["cards"]!.AsArray().Select(c => (string)c!["id"]!));
            Assert.True((bool)made["paused"]!);
            await app.Until(() => Tile(app, first.Id)?.QueueChips.SequenceEqual(["⏵ Travel 1"]) == true, "the tile's queue chip");
            Assert.Null(Live(app, first.Id));

            // Run: only the first starts, moved to In Progress as Work on does
            await app.Api("queue-run", new { project = "proj", queue = "Travel" });
            await app.Until(() => Live(app, first.Id) is not null, "the queue to start the first card");
            await app.Until(() => app.Read(first.Id).Status == "In Progress", "the first card to move to In Progress");
            Assert.Null(Live(app, second.Id));
            Assert.Null(Live(app, third.Id));
            app.Shot("running");

            // it lands in QA: once its agent's turn is over the check is asked, says nothing waits on it, and the next starts
            BoardStore.Move(app.BoardFolder, first.Id, app.ColumnId("QA Ready"));
            await DargApp.Settle(1000);
            turns.Release(); // its turn ends; the cards started from here take one step
            await app.Until(() => Live(app, second.Id) is not null, "the second card to start once the first is in QA and the check says go", 40);
            Assert.Contains(asked, p => p.Contains(first.Id) && p.Contains(second.Id));
            Assert.Null(Live(app, third.Id));

            // its agent ends its turn with the card still In Progress: the queue pauses rather than carry on
            await app.Until(() => Queue(app).Paused, "the queue to pause on the unfinished card", 40);
            Assert.Contains(second.Id, Queue(app).Why);
            Assert.Null(Live(app, third.Id));
            var listed = JsonNode.Parse(await app.Api("queues", new { project = "proj" }))!.AsArray().Single()!;
            Assert.Equal("failed", (string)listed["cards"]!.AsArray().Single(c => (string)c!["id"]! == second.Id)!["state"]!);
            app.Shot("paused");

            // Resume moves on past it
            await app.Api("queue-run", new { project = "proj", queue = "Travel" });
            await app.Until(() => Live(app, third.Id) is not null, "Resume to start the third card");
            Assert.Contains(second.Id, Queue(app).Released);
        }
    }

    [AvaloniaFact]
    public async Task A_card_that_fails_to_start_pauses_the_queue_and_Resume_goes_on_to_the_next()
    {
        using var app = DargApp.Make();
        var first = app.Card("cannot start", "Backlog");
        var second = app.Card("starts fine", "Backlog");
        await app.Start();
        var queues = app.Project.Queues!;
        var start = queues.Start!;
        var tried = new List<string>();
        queues.Start = id => { tried.Add(id); return id == first.Id ? "no agent started" : start(id); };
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Picks" });
        // never run: pausing says so rather than ending on an empty colon
        Assert.Equal("Picks is paused already: it hasn't been run yet.", await app.Api("queue-run", new { project = "proj", queue = "Picks", pause = true }));

        await app.Api("queue-run", new { project = "proj", queue = "Picks" });
        await app.Until(() => Queue(app).Paused && Queue(app).PausedOn == first.Id, "the queue to pause on the card that couldn't start");
        Assert.Contains("Couldn't start", Queue(app).Why);
        Assert.Null(Live(app, second.Id));

        // Resume moves on past it, as its tip says, rather than trying it again
        await app.Api("queue-run", new { project = "proj", queue = "Picks" });
        await app.Until(() => Live(app, second.Id) is not null, "Resume to start the next card");
        Assert.Equal(1, tried.Count(id => id == first.Id));
        Assert.False(Queue(app).Paused);
    }

    /// <summary>QA round 2: a card the queue had moved on from stayed "moved on" even once taken out and added back, so
    /// it could never be run again. Taking it out forgets it; added back, the queue starts it.</summary>
    [AvaloniaFact]
    public async Task A_card_moved_on_from_runs_once_taken_out_and_added_back()
    {
        using var app = DargApp.Make();
        var card = app.Card("cannot start yet", "Backlog");
        await app.Start();
        var queues = app.Project.Queues!;
        var start = queues.Start!;
        var fixedUp = false;
        queues.Start = id => fixedUp ? start(id) : "no agent started";
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Picks" });
        await app.Api("queue-run", new { project = "proj", queue = "Picks" });
        await app.Until(() => Queue(app).Paused && Queue(app).PausedOn == card.Id, "the queue to pause on the card that couldn't start");
        await app.Api("queue-run", new { project = "proj", queue = "Picks" });
        await app.Until(() => queues.Queues[0].Doing.StartsWith("Finished", StringComparison.Ordinal), "the queue to finish, having moved on");
        Assert.Equal("moved on", queues.Queues[0].Cards.Single().State);

        fixedUp = true;
        await app.Api("queue-remove", new { project = "proj", queue = "Picks", cards = card.Id });
        await app.Api("queue-add", new { project = "proj", queue = "Picks", cards = card.Id });
        await app.Until(() => Live(app, card.Id) is not null, "the card added back to be started");
        app.Shot("started-again");
    }

    [AvaloniaFact]
    public async Task Plus_Queue_makes_one_from_the_board_showing_and_the_strip_and_card_menu_show_it()
    {
        using var app = DargApp.Make();
        var a = app.Card("one", "Backlog");
        var b = app.Card("two", "To Do");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var queues = app.Project.Queues!;
        Assert.False(queues.ShowsStrip);

        app.Board.NewQueueCommand.Execute(null);
        Assert.True(queues.IsMaking);
        Assert.Equal("Main", queues.NewName);
        queues.NewName = "Next up";
        queues.MakeNewCommand.Execute(null);
        await app.Until(() => queues.Queues.Count == 1 && queues.Queues[0].Summary.StartsWith("0 of 2 done", StringComparison.Ordinal), "the strip's line");
        Assert.Equal([a.Id, b.Id], Queue(app).Cards);
        Assert.True(queues.Queues[0].IsOpen); // a lane opens with its cards showing (WOR-0220)
        await DargApp.Settle(200);
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Next up");
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "two");
        app.Shot("strip");

        // the tile's menu takes it out; a new card's menu adds it
        await app.Until(() => Tile(app, b.Id)?.MoveTargets.Any(m => m.Label == "⏵ Take out of queue Next up") == true, "the menu's take-out entry");
        Tile(app, b.Id)!.MoveTargets.First(m => m.Label == "⏵ Take out of queue Next up").Command.Execute(null);
        Assert.Equal([a.Id], Queue(app).Cards);
        await app.Until(() => Tile(app, b.Id)?.MoveTargets.Any(m => m.Label == "⏵ Add to queue Next up") == true, "the menu's add entry");
        Assert.False(Tile(app, b.Id)!.InQueue);

        // ↓ on the strip moves a card down; nothing is started by any of it
        queues.Add(Queue(app), [b.Id]);
        queues.Queues[0].Cards[0].DownCommand.Execute(null);
        Assert.Equal([b.Id, a.Id], Queue(app).Cards);
        Assert.Null(app.Agents.ForCard(a.Id));
        Assert.True(File.Exists(Path.Combine(ProjectData.Root("proj"), QueueStore.FileName)));
    }

    /// <summary>The lanes of the window's board: each queue's boxed lane, top to bottom.</summary>
    static List<Border> Lanes(DargApp app) => app.Window.GetVisualDescendants().OfType<Border>()
        .Where(b => b.Classes.Contains("queuelane") && b.IsEffectivelyVisible).OrderBy(b => b.TranslatePoint(new Point(0, 0), app.Window)!.Value.Y).ToList();

    /// <summary>The ids of the card tiles drawn inside a lane, left to right.</summary>
    static List<string> LaneIds(DargApp app, Border lane) => lane.GetVisualDescendants().OfType<Border>()
        .Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible && b.Tag is CardViewModel)
        .OrderBy(b => b.TranslatePoint(new Point(0, 0), app.Window)!.Value.X).Select(b => ((CardViewModel)b.Tag!).Id).ToList();

    /// <summary>The ids of the card tiles drawn in the board's lists (and their state sub-columns).</summary>
    static HashSet<string> ListIds(DargApp app) => app.Window.GetVisualDescendants().OfType<Border>()
        .Where(b => b.Classes.Contains("list") && !b.Classes.Contains("queuelane") && b.IsEffectivelyVisible)
        .SelectMany(l => l.GetVisualDescendants().OfType<Border>()).Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible && b.Tag is CardViewModel)
        .Select(b => ((CardViewModel)b.Tag!).Id).ToHashSet();

    /// <summary>WOR-0220: a queue reads as one boxed group: its cards leave their lists (by state or not) for its lane,
    /// drawn as the board's own tiles in the queue's order, not the board's; taken out, a card goes back to its list.</summary>
    [AvaloniaFact]
    public async Task A_queue_is_one_boxed_lane_of_its_cards_in_queue_order_and_they_leave_their_lists()
    {
        using var app = DargApp.Make();
        var a = app.Card("in the backlog", "Backlog");
        var b = app.Card("being worked", "In Progress");
        var c = app.Card("waiting for a check", "QA Ready");
        var other = app.Card("not queued", "Backlog");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Travel", cards = $"{c.Id} {a.Id} {b.Id}" });
        var queues = app.Project.Queues!;
        Assert.Equal([c.Id, a.Id, b.Id], Queue(app).Cards);

        // the queued cards are in the lane, as the board's own tiles, in the queue's order; the other stays in its list
        await app.Until(() => Lanes(app).Count == 1 && LaneIds(app, Lanes(app)[0]).SequenceEqual([c.Id, a.Id, b.Id]), "the lane's tiles in queue order");
        Assert.All(queues.Queues[0].Cards, s => Assert.True(s.HasTile));
        Assert.Equal(["Main · QA Ready", "Main · Backlog", "Main · In Progress"], queues.Queues[0].Cards.Select(s => s.Where));
        var listed = ListIds(app);
        Assert.Contains(other.Id, listed);
        Assert.DoesNotContain(a.Id, listed);
        Assert.DoesNotContain(b.Id, listed);
        Assert.DoesNotContain(c.Id, listed);
        Assert.Null(InLists(app, a.Id));
        app.Shot("lane");

        // split by state, the same: the queue's cards stay together in the lane rather than in the state columns
        app.Board.ActiveSplit = true;
        await DargApp.Settle(200);
        Assert.DoesNotContain(app.Board.Rows.SelectMany(r => r.Lists).SelectMany(l => l.Groups).SelectMany(g => g.Cards), t => Queue(app).Has(t.Id));
        Assert.Equal([c.Id, a.Id, b.Id], LaneIds(app, Lanes(app)[0]));
        Assert.Contains(other.Id, ListIds(app));
        app.Shot("lane-by-state");
        app.Board.ActiveSplit = false;

        // reordered in the lane, it draws in the new order
        queues.Queues[0].Cards[2].UpCommand.Execute(null);
        await app.Until(() => LaneIds(app, Lanes(app)[0]).SequenceEqual([c.Id, b.Id, a.Id]), "the lane in its new order");

        // taken out: back in its list
        queues.Queues[0].Cards.Single(s => s.Id == b.Id).RemoveCommand.Execute(null);
        await app.Until(() => ListIds(app).Contains(b.Id) && LaneIds(app, Lanes(app)[0]).SequenceEqual([c.Id, a.Id]), "the card back in its list");

        // folded, the lane is one line of its cards in order
        queues.Queues[0].OpenCommand.Execute(null);
        await DargApp.Settle(100);
        Assert.Empty(LaneIds(app, Lanes(app)[0]));
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == $"1 {c.Id}  →  2 {a.Id}");
    }

    /// <summary>WOR-0220: a queue is renamed in place, dragged by its grip among the queues, and deleted keeping its cards
    /// (they go back to their lists) or with its unfinished cards (to the board's .deleted folder; a done one stays).</summary>
    [AvaloniaFact]
    public async Task A_queue_renames_drags_among_the_queues_and_deletes_with_or_without_its_cards()
    {
        using var app = DargApp.Make();
        var a = app.Card("first queue's card", "Backlog");
        var b = app.Card("second queue's open card", "To Do");
        var done = app.Card("second queue's done card", "Done");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "One", cards = a.Id });
        await app.Api("queue-new", new { project = "proj", name = "Two", cards = $"{b.Id} {done.Id}" });
        var queues = app.Project.Queues!;
        var file = QueueStore.PathFor("proj");
        string Names() => string.Join(",", queues.All.Select(q => q.Name));

        // rename: another queue's name is refused, a free one is kept on disk
        var one = queues.Queues[0];
        one.StartRenameCommand.Execute(null);
        await DargApp.Settle(100);
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible && t.Classes.Contains("rename") && t.Text == "One");
        one.RenameText = "two";
        one.CommitRenameCommand.Execute(null);
        Assert.Equal("There's a queue called two already.", one.RenameError);
        Assert.True(one.IsRenaming);
        one.RenameText = "Trips";
        one.CommitRenameCommand.Execute(null);
        Assert.False(one.IsRenaming);
        Assert.Equal("Trips,Two", Names());
        Assert.Equal(["Trips", "Two"], QueueStore.Load(file).Select(q => q.Name));
        Assert.Contains("Renamed Trips to Days", await app.Api("queue-rename", new { project = "proj", queue = "Trips", name = "Days" }));

        // drag Two's lane by its grip above the first: it moves there, and the order is kept
        await app.Until(() => Lanes(app).Count == 2, "both lanes");
        var top = app.Window;
        Point Grip(Border lane)
        {
            var g = lane.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("queuegrip"));
            return g.TranslatePoint(new Point(g.Bounds.Width / 2, g.Bounds.Height / 2), top)!.Value;
        }
        var from = Grip(Lanes(app)[1]);
        var to = Lanes(app)[0].TranslatePoint(new Point(10, 2), top)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, from);
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, from, Avalonia.Input.MouseButton.Left);
        for (var i = 1; i <= 4; i++)
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, from + (to - from) * (i / 4.0), Avalonia.Input.RawInputModifiers.LeftMouseButton);
        app.Shot("lane-dragging");
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, to, Avalonia.Input.MouseButton.Left);
        await DargApp.Settle(100);
        Assert.Equal("Two,Days", Names());
        Assert.Equal(["Two", "Days"], QueueStore.Load(file).Select(q => q.Name));
        await app.Until(() => Lanes(app).Count == 2 && LaneIds(app, Lanes(app)[0]).Contains(b.Id), "Two's lane drawn first");
        // and back, through the API
        Assert.Contains("Two is queue 2 of 2", await app.Api("queue-move", new { project = "proj", queue = "Two", to = 2 }));
        Assert.Equal("Days,Two", Names());
        app.Shot("lanes-moved");

        // delete keeping its cards: the queue goes, its card back to its list, on its board still
        var days = queues.Queues[0];
        days.AskDeleteCommand.Execute(null);
        Assert.Equal("Delete Days? Its cards stay on their boards.", days.DeleteQuestion);
        days.DeleteCommand.Execute(null);
        Assert.Equal("Two", Names());
        await app.Until(() => ListIds(app).Contains(a.Id), "the kept card back in its list");
        Assert.Equal("Backlog", app.Read(a.Id).Status);

        // delete with its cards: the unfinished one goes to .deleted, the done one stays
        var two = queues.Queues[0];
        two.AskDeleteCardsCommand.Execute(null);
        Assert.Contains($"its 1 unfinished card ({b.Id})", two.DeleteQuestion);
        app.Shot("delete-with-cards");
        two.DeleteCommand.Execute(null);
        Assert.Empty(queues.All);
        Assert.DoesNotContain(BoardStore.ReadCards(app.BoardFolder), x => x.Id == b.Id);
        Assert.True(File.Exists(Path.Combine(app.BoardFolder, ".deleted", b.File)));
        Assert.Contains(BoardStore.ReadCards(app.BoardFolder), x => x.Id == done.Id);
        Assert.Contains(a.Id, BoardStore.ReadCards(app.BoardFolder).Select(x => x.Id));
    }

    /// <summary>WOR-0220 QA: a tile in a queue's lane Ctrl+clicked is picked like any other, so ▶ Work on N counts it,
    /// keeps the pick through a reload and starts it; Pick all still leaves the queue's cards to the queue.</summary>
    [AvaloniaFact]
    public async Task A_lane_tile_picked_with_Ctrl_counts_for_Work_on_N_and_starts()
    {
        using var app = DargApp.Make();
        var queued = app.Card("queued card", "Backlog");
        var free = app.Card("free card", "Backlog");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Q", cards = queued.Id });
        var board = app.Board;
        await app.Until(() => board.LaneTile(queued.Id) is not null, "the queued card's lane tile");

        // Pick all picks the free card only
        board.PickAllCommand.Execute(null);
        Assert.Equal("1 picked", board.PickedText);
        Assert.False(board.LaneTile(queued.Id)!.IsPicked);
        board.ClearPickedCommand.Execute(null);

        // Ctrl+click on the lane tile picks it, and Work on N counts it
        var tile = board.LaneTile(queued.Id)!;
        tile.Board.Click(tile, Avalonia.Input.KeyModifiers.Control);
        Assert.True(tile.IsPicked);
        Assert.Equal("1 picked", board.PickedText);
        Assert.Equal("▶ Work on 1", board.WorkOnPickedText);
        Assert.True(board.CanWorkOnPicked);

        // a reload keeps the pick
        board.Reload();
        await DargApp.Settle(200);
        Assert.Equal("▶ Work on 1", board.WorkOnPickedText);
        Assert.True(board.LaneTile(queued.Id)!.IsPicked);
        app.Shot("lane-tile-picked");

        // and Work on N starts it
        board.WorkOnPickedCommand.Execute(null);
        await app.Until(() => app.Agents.ForCard(queued.Id) is not null && app.Read(queued.Id).Status == "In Progress", "an agent on the picked lane card");
        Assert.Null(app.Agents.ForCard(free.Id));
        Assert.False(board.HasPicked);
    }

    /// <summary>WOR-0241: no add-by-id box on a lane; Ctrl+picked cards join a queue from a right-click (every picked card, not just
    /// the one clicked) or by being dropped on its lane, which is a drop target of the drag.</summary>
    [AvaloniaFact]
    public async Task Picked_cards_join_a_queue_from_the_menu_or_a_lane_drop_and_there_is_no_add_by_id_box()
    {
        using var app = DargApp.Make();
        var seed = app.Card("seed", "Backlog");
        var a = app.Card("alpha", "Backlog");
        var b = app.Card("beta", "Backlog");
        var c = app.Card("gamma", "To Do");
        var d = app.Card("delta", "To Do");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Q", cards = seed.Id });
        var board = app.Board;
        await app.Until(() => board.LaneTile(seed.Id) is not null && InLists(app, a.Id) is not null && InLists(app, d.Id) is not null, "tiles");
        await DargApp.Settle(200);

        // no text box for ids, and the lane is a drop target keyed by its queue
        Assert.DoesNotContain(app.Window.GetVisualDescendants().OfType<TextBox>(), t => t.Text is not null && (t.Name?.Contains("queue") == true || Avalonia.Automation.AutomationProperties.GetName(t)?.StartsWith("Add cards to") == true));
        var lane = app.Window.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("queuelane"));
        Assert.True(lane.Classes.Contains("list"));
        Assert.Equal("queue:Q", lane.Tag);

        // Ctrl+click two cards, right-click a third picked one: all three go in
        foreach (var t in new[] { a, b, c }) { var tile = InLists(app, t.Id)!; tile.Board.Click(tile, Avalonia.Input.KeyModifiers.Control); }
        Assert.Equal("3 picked", board.PickedText);
        await app.Until(() => InLists(app, b.Id)?.MoveTargets.Any(m => m.Label == "⏵ Add to queue Q") == true, "the menu's add entry");
        InLists(app, b.Id)!.MoveTargets.First(m => m.Label == "⏵ Add to queue Q").Command.Execute(null);
        Assert.Equal([seed.Id, a.Id, b.Id, c.Id], Queue(app).Cards.OrderBy(x => x == seed.Id ? 0 : 1).ThenBy(x => x).ToList());
        Assert.DoesNotContain(d.Id, Queue(app).Cards);
        await app.Until(() => app.Project.Queues!.Queues[0].Doing.StartsWith("Added", StringComparison.Ordinal), "the lane saying what went in");

        // a drop on the lane (what DragLift calls) adds the dropped card
        app.Project.Queues!.DropOnKey("queue:Q", [d.Id]);
        Assert.Contains(d.Id, Queue(app).Cards);
    }

    /// <summary>WOR-0241: cards leave a queue the way they joined: Ctrl+picked lane tiles and a right-click take every picked one out,
    /// and a drop of them on a board's list (what DragLift calls: TakeOut) does too; a done card in a queue has the entry as well.</summary>
    [AvaloniaFact]
    public async Task Picked_cards_leave_a_queue_from_the_menu_or_a_drag_out_of_the_lane()
    {
        using var app = DargApp.Make();
        var a = app.Card("alpha", "Backlog");
        var b = app.Card("beta", "Backlog");
        var c = app.Card("gamma", "Backlog");
        var done = app.Card("finished", "Done");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Q", cards = $"{a.Id},{b.Id},{c.Id},{done.Id}" });
        var board = app.Board;
        await app.Until(() => new[] { a, b, c, done }.All(x => board.LaneTile(x.Id) is not null), "lane tiles");
        await DargApp.Settle(200);

        // a done card's menu still has Take out, and no Add
        Assert.Contains(board.LaneTile(done.Id)!.MoveTargets, m => m.Label == "⏵ Take out of queue Q");
        Assert.DoesNotContain(board.LaneTile(done.Id)!.MoveTargets, m => m.Label.StartsWith("⏵ Add to queue", StringComparison.Ordinal));

        // pick two lane tiles, right-click one: both go
        foreach (var t in new[] { a, b }) { var tile = board.LaneTile(t.Id)!; tile.Board.Click(tile, Avalonia.Input.KeyModifiers.Control); }
        await app.Until(() => board.LaneTile(b.Id)!.MoveTargets.Any(m => m.Label == "⏵ Take out of queue Q"), "the take-out entry");
        board.LaneTile(b.Id)!.MoveTargets.First(m => m.Label == "⏵ Take out of queue Q").Command.Execute(null);
        Assert.DoesNotContain(a.Id, Queue(app).Cards);
        Assert.DoesNotContain(b.Id, Queue(app).Cards);
        Assert.Contains(c.Id, Queue(app).Cards);

        // dragged out of the lane onto a list
        app.Project.Queues!.TakeOut([c.Id, done.Id]);
        Assert.Empty(Queue(app).Cards);
    }
}
