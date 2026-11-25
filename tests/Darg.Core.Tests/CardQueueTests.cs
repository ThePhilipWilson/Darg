using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0212: a named queue of cards, worked one at a time: its order from Blocked by lines (no model), what it
/// does next as cards move, the QA check's reply, and the file it's kept in.</summary>
public sealed class CardQueueTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "darg-queue-" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// <summary>A card where the queue sees it: board, column (Backlog, In Progress, QA Ready, For Human Attention, Done).</summary>
    static BoardCard C(string id, string column = "Backlog", string board = "Story", params string[] blockedBy) =>
        new(new Card(id, id + " title", column, "", "Task", [], id + ".md", "") { BlockedBy = blockedBy },
            board, column, id + ".md", column == "QA Ready", column == "For Human Attention", column == "Done");

    sealed class World
    {
        public readonly Dictionary<string, BoardCard> Cards = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, QueueAgent> Agents = new(StringComparer.OrdinalIgnoreCase);
        public World(params BoardCard[] cards) { foreach (var c in cards) Cards[c.Card.Id] = c; }
        public void Move(string id, string column) { var c = Cards[id]; Cards[id] = C(id, column, c.Board, [.. c.Card.BlockedBy]); }
        public QueueStep Next(CardQueue q) => QueueRun.Next(q, id => Cards.GetValueOrDefault(id), id => Agents.GetValueOrDefault(id), Cards);
    }

    [Fact]
    public void A_boards_queue_puts_each_card_after_what_it_waits_on_and_pulls_in_blockers_from_other_boards()
    {
        var cards = new List<BoardCard>
        {
            C("S-3", blockedBy: ["S-2"]),
            C("S-1"),
            C("S-2", blockedBy: ["M-9"]),
            C("S-4", "Done"),           // done: not queued
            C("S-5", "QA Ready"),       // already worked: not queued
            C("M-9", board: "Main"),    // another board's card S-2 waits on: queued first
            C("M-8", board: "Main"),    // another board's card nothing here waits on: left out
            C("S-6", blockedBy: ["S-5"]), // its blocker is in QA, so cleared: not pulled in
        };
        Assert.Equal(["S-1", "M-9", "S-2", "S-3", "S-6"], QueueOrder.ForBoard("Story", cards));
    }

    [Fact]
    public void A_cycle_keeps_its_given_order_rather_than_losing_cards()
    {
        var index = Blockers.Index([C("A-1", blockedBy: ["A-2"]), C("A-2", blockedBy: ["A-1"]), C("A-3")]);
        Assert.Equal(["A-3", "A-1", "A-2"], QueueOrder.Sort(["A-1", "A-2", "A-3"], index));
    }

    [Fact]
    public void Adding_a_card_brings_its_open_blockers_ahead_of_it_and_nothing_the_queue_has()
    {
        var index = Blockers.Index([C("A-1"), C("A-2", blockedBy: ["A-1"]), C("A-3", blockedBy: ["A-2", "A-0"]), C("A-0", "Done")]);
        var q = new CardQueue { Name = "q", Cards = ["A-1"] };
        Assert.Equal(["A-2", "A-3"], QueueOrder.Add(q, ["A-3"], index));
        Assert.Equal(["A-1", "A-2", "A-3"], q.Cards);
        Assert.Empty(QueueOrder.Add(q, ["A-2", "NOPE-1"], index));
    }

    [Fact]
    public void A_new_queue_waits_for_Run_then_starts_the_first_card_and_holds_while_it_is_worked()
    {
        var w = new World(C("A-1"), C("A-2", blockedBy: ["A-1"]));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2"] };
        Assert.IsType<QueueStep.Held>(w.Next(q));

        QueueRun.Resume(q);
        Assert.Equal(new QueueStep.Start("A-1"), w.Next(q));
        QueueRun.Started(q, "A-1");
        w.Move("A-1", "In Progress"); w.Agents["A-1"] = QueueAgent.Working;
        Assert.IsType<QueueStep.Wait>(w.Next(q));

        // done: the next starts
        w.Move("A-1", "Done"); w.Agents.Remove("A-1");
        Assert.Equal(new QueueStep.Start("A-2"), w.Next(q));
        QueueRun.Started(q, "A-2");
        w.Move("A-2", "Done");
        Assert.IsType<QueueStep.Finished>(w.Next(q));
    }

    [Fact]
    public void In_QA_it_asks_once_whether_that_blocks_the_next_and_does_as_told()
    {
        var w = new World(C("A-1", "In Progress"), C("A-2"), C("A-3"));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2", "A-3"] };
        QueueRun.Resume(q);
        QueueRun.Started(q, "A-1");

        // still at work in QA (merging, writing its report): wait for the turn to end
        w.Move("A-1", "QA Ready"); w.Agents["A-1"] = QueueAgent.Working;
        Assert.IsType<QueueStep.Wait>(w.Next(q));
        w.Agents.Remove("A-1");
        Assert.Equal(new QueueStep.CheckQa("A-1", "A-2"), w.Next(q));

        // blocking: wait for done
        q.Gates["A-1"] = new QaGate(true, "A-2", "same screen");
        Assert.Contains("same screen", Assert.IsType<QueueStep.Wait>(w.Next(q)).Why);
        // reordered: the answer was about another card, so it asks again
        QueueOrder.Shift(q, "A-3", -1);
        Assert.Equal(new QueueStep.CheckQa("A-1", "A-3"), w.Next(q));
        // not blocking: the next starts with A-1 still in QA
        q.Gates["A-1"] = new QaGate(false, "A-3", "separate");
        Assert.Equal(new QueueStep.Start("A-3"), w.Next(q));
    }

    [Fact]
    public void Waiting_on_the_person_holds_its_place_and_the_queue_goes_on()
    {
        var w = new World(C("A-1", "In Progress"), C("A-2"), C("A-3"));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2", "A-3"] };
        QueueRun.Resume(q);
        QueueRun.Started(q, "A-1");
        w.Agents["A-1"] = QueueAgent.Asking;
        Assert.Equal(new QueueStep.Start("A-2"), w.Next(q));
        QueueRun.Started(q, "A-2");
        w.Move("A-2", "For Human Attention");
        Assert.Equal(new QueueStep.Start("A-3"), w.Next(q));
    }

    [Theory]
    [InlineData(QueueAgent.Failed, "failed")]
    [InlineData(QueueAgent.Stopped, "stopped")]
    [InlineData(QueueAgent.None, "stopped")]
    public void A_failure_pauses_it_and_Resume_moves_on_past_that_card(QueueAgent agent, string said)
    {
        var w = new World(C("A-1", "In Progress"), C("A-2"));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2"] };
        QueueRun.Resume(q);
        QueueRun.Started(q, "A-1");
        w.Agents["A-1"] = agent;
        var pause = Assert.IsType<QueueStep.Pause>(w.Next(q));
        Assert.Equal("A-1", pause.Id);
        Assert.Contains(said, pause.Why);
        QueueRun.Pause(q, pause.Why, pause.Id);
        Assert.IsType<QueueStep.Held>(w.Next(q));
        Assert.Equal("failed", QueueRun.StateOf(q, "A-1", w.Cards["A-1"], agent));

        QueueRun.Resume(q);
        Assert.Equal(new QueueStep.Start("A-2"), w.Next(q));
    }

    /// <summary>QA, 2026-11-25: a card that couldn't be started (the app pauses on it with "Couldn't start") was picked
    /// again on every Resume, since only started cards were passed over. Resume moves on past it as it does a failed one.</summary>
    [Fact]
    public void A_card_that_failed_to_start_is_moved_on_from_by_Resume()
    {
        var w = new World(C("A-1"), C("A-2"));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2"] };
        QueueRun.Resume(q);
        Assert.Equal(new QueueStep.Start("A-1"), w.Next(q));
        QueueRun.Pause(q, "Couldn't start A-1: no agent started.", "A-1"); // as QueuesViewModel.Step does
        Assert.Equal("failed", QueueRun.StateOf(q, "A-1", w.Cards["A-1"], QueueAgent.None));

        QueueRun.Resume(q);
        Assert.Equal(new QueueStep.Start("A-2"), w.Next(q));
        Assert.Equal("moved on", QueueRun.StateOf(q, "A-1", w.Cards["A-1"], QueueAgent.None));
        QueueRun.Started(q, "A-2");
        w.Move("A-2", "Done");
        Assert.IsType<QueueStep.Finished>(w.Next(q));
    }

    /// <summary>QA round 2, 2026-11-25: a card the queue had moved on from stayed "moved on" for good, even taken out (✕)
    /// and added back, since Remove left it in Started and Released. Taking a card out forgets it; added back, it runs.</summary>
    [Theory]
    [InlineData(true)]  // its start failed
    [InlineData(false)] // its agent failed
    public void A_card_moved_on_from_runs_again_once_taken_out_and_added_back(bool startFailed)
    {
        var w = new World(C("A-1"), C("A-2"));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2"] };
        QueueRun.Resume(q);
        Assert.Equal(new QueueStep.Start("A-1"), w.Next(q));
        if (!startFailed) { QueueRun.Started(q, "A-1"); w.Move("A-1", "In Progress"); w.Agents["A-1"] = QueueAgent.Failed; }
        q.Gates["A-1"] = new QaGate(true, "A-2", "stale");
        QueueRun.Pause(q, "A-1 failed.", "A-1");
        QueueRun.Resume(q);
        Assert.Equal(new QueueStep.Start("A-2"), w.Next(q));
        QueueRun.Started(q, "A-2");
        w.Move("A-2", "Done");
        Assert.IsType<QueueStep.Finished>(w.Next(q));
        Assert.Equal("moved on", QueueRun.StateOf(q, "A-1", w.Cards["A-1"], QueueAgent.None));

        // seen to by hand, then taken out and added back
        w.Agents.Remove("A-1"); w.Move("A-1", "Backlog");
        Assert.True(QueueOrder.Remove(q, "A-1"));
        Assert.DoesNotContain("A-1", q.Started);
        Assert.DoesNotContain("A-1", q.Released);
        Assert.False(q.Gates.ContainsKey("A-1"));
        Assert.Equal(["A-1"], QueueOrder.Add(q, ["A-1"], w.Cards));
        Assert.Equal("queued", QueueRun.StateOf(q, "A-1", w.Cards["A-1"], QueueAgent.None));
        Assert.Equal(new QueueStep.Start("A-1"), w.Next(q));
    }

    [Fact]
    public void Taking_out_the_card_a_queue_is_paused_on_leaves_it_paused_on_nothing()
    {
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2"] };
        QueueRun.Resume(q);
        QueueRun.Pause(q, "Couldn't start A-1.", "A-1");
        Assert.True(QueueOrder.Remove(q, "A-1"));
        Assert.Null(q.PausedOn);
        Assert.True(q.Paused);
        Assert.False(QueueOrder.Remove(q, "A-1"));
    }

    [Fact]
    public void A_blocked_card_is_passed_over_and_the_queue_says_what_the_rest_wait_on()
    {
        var w = new World(C("A-1", blockedBy: ["X-1"]), C("A-2"), C("X-1", "In Progress", "Other"));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2"] };
        QueueRun.Resume(q);
        Assert.Equal(new QueueStep.Start("A-2"), w.Next(q));
        QueueRun.Started(q, "A-2");
        w.Move("A-2", "Done");
        Assert.Contains("A-1 on X-1", Assert.IsType<QueueStep.Wait>(w.Next(q)).Why);
        w.Move("X-1", "Done");
        Assert.Equal(new QueueStep.Start("A-1"), w.Next(q));
    }

    [Fact]
    public void A_card_started_by_hand_is_taken_on_and_waited_for()
    {
        var w = new World(C("A-1"), C("A-2", "In Progress"));
        var q = new CardQueue { Name = "q", Cards = ["A-1", "A-2"] };
        QueueRun.Resume(q);
        w.Agents["A-1"] = QueueAgent.Working;
        Assert.IsType<QueueStep.Wait>(w.Next(q));
        Assert.Contains("A-1", q.Started);
    }

    [Theory]
    [InlineData("BLOCKING: it edits the same view", true, "it edits the same view")]
    [InlineData("NOT BLOCKING: separate API work.", false, "separate API work")]
    [InlineData("**Not blocking** - different files", false, "different files")]
    [InlineData("`non-blocking`", false, "no reason given")]
    [InlineData("Answer: blocking — same test", true, "same test")]
    [InlineData("\n\n  \"Not Blocking: unrelated\"\nmore words", false, "unrelated")]
    // QA round 2: apostrophes inside the reason stay (real Haiku replies lost them)
    [InlineData("BLOCKING: AGE-0003's saved-agent folder move", true, "AGE-0003's saved-agent folder move")]
    [InlineData("**NOT BLOCKING**: WOR-0215’s Backlog Work All button.", false, "WOR-0215’s Backlog Work All button")]
    [InlineData("'Blocking': it's the same view'", true, "it's the same view")]
    [InlineData("BLOCKING: edits `QueueRun.Next` and **the strip**", true, "edits QueueRun.Next and the strip")]
    public void The_QA_check_takes_the_shapes_a_model_drifts_into(string reply, bool blocking, string why) =>
        Assert.Equal((blocking, why), QueueCheck.Parse(reply));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I think it is probably not blocking")]
    [InlineData("Yes")]
    [InlineData("blockingly so")]
    public void Anything_else_is_no_answer(string? reply) => Assert.Null(QueueCheck.Parse(reply));

    [Fact]
    public void The_queues_survive_a_save_and_load_and_names_stay_unique()
    {
        var file = Path.Combine(_root, "queues.json");
        var q = new CardQueue { Name = "Travel", Cards = ["A-1", "A-2"], Started = ["A-1"] };
        q.Gates["a-1"] = new QaGate(false, "A-2", "separate");
        QueueRun.Resume(q);
        QueueStore.Save(file, [q]);
        var back = Assert.Single(QueueStore.Load(file));
        Assert.Equal(["A-1", "A-2"], back.Cards);
        Assert.False(back.Paused);
        Assert.False(back.Gates["A-1"].Blocking); // case aside, after a load too
        Assert.Equal("travel 2", QueueStore.NameFor(" travel ", [back]));
        Assert.Equal("Queue", QueueStore.NameFor("", []));
        Assert.Empty(QueueStore.Load(Path.Combine(_root, "none.json")));
    }

    /// <summary>WOR-0220: a queue renamed in its lane keeps names unique; dragged, it moves among the queues.</summary>
    [Fact]
    public void A_queue_renames_to_a_free_name_and_moves_among_the_queues()
    {
        var a = new CardQueue { Name = "Travel" };
        var b = new CardQueue { Name = "Fixes" };
        var c = new CardQueue { Name = "Later" };
        var all = new List<CardQueue> { a, b, c };
        Assert.Equal("There's a queue called fixes already.", QueueStore.Rename(a, " fixes ", all));
        Assert.Equal("A queue needs a name.", QueueStore.Rename(a, "  ", all));
        Assert.Equal("Travel", a.Name);
        Assert.Null(QueueStore.Rename(a, "travel", all)); // its own name in another case
        Assert.Null(QueueStore.Rename(a, " Trips ", all));
        Assert.Equal("Trips", a.Name);

        Assert.True(QueueStore.Move(all, 0, 2));
        Assert.Equal([b, c, a], all);
        Assert.True(QueueStore.Move(all, 2, 0));
        Assert.Equal([a, b, c], all);
        Assert.True(QueueStore.Move(all, 1, 99)); // past the end: last
        Assert.Equal([a, c, b], all);
        Assert.False(QueueStore.Move(all, 1, 1));
        Assert.False(QueueStore.Move(all, 5, 0));
    }
}
