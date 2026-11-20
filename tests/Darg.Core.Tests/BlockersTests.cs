using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0172: a card's Blocked by line, which blockers are still open across the project's boards, what a card
/// blocks, and cycles.</summary>
public sealed class BlockersTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "darg-blockers-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _boards;

    public BlockersTests() => _boards = Directory.CreateDirectory(Path.Combine(_root, "tasks")).FullName;
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Theory]
    [InlineData("AND-0003, WOR-0150", new[] { "AND-0003", "WOR-0150" })]
    [InlineData("wor-0150 AND-0003; WOR-0150", new[] { "WOR-0150", "AND-0003" })]
    [InlineData("[WOR-0158](card:WOR-0158)", new[] { "WOR-0158" })]
    [InlineData("none", new string[0])]
    [InlineData("", new string[0])]
    public void Ids_are_read_however_they_are_written(string value, string[] ids) => Assert.Equal(ids, Blockers.ParseIds(value));

    [Fact]
    public void The_line_among_the_properties_is_read_and_one_in_the_description_is_not()
    {
        var card = BoardStore.Parse("# T-0002: x\n\n- **Status:** To Do\n- **Labels:** a\n- **Blocked by:** T-0001, and-0003\n- **Created:** 2026-11-20\n\n" +
                                    "## Description\n\n- **Blocked by:** T-0009\n", "T-0002 - x.md")!;
        Assert.Equal(["T-0001", "AND-0003"], card.BlockedBy);
        Assert.Empty(BoardStore.Parse("# T-0003: y\n\n- **Status:** To Do\n\n## Description\n\n- **Blocked by:** T-0001\n", "y.md")!.BlockedBy);

        // read twice, it's the same card (the tile is kept), and a changed line is a change
        var again = BoardStore.Parse("# T-0002: x\n\n- **Status:** To Do\n- **Labels:** a\n- **Blocked by:** T-0001, AND-0003\n- **Created:** 2026-11-20\n\n" +
                                     "## Description\n\n- **Blocked by:** T-0009\n", "T-0002 - x.md")!;
        Assert.True(card.SameAs(again));
        Assert.False(card.SameAs(again with { BlockedBy = ["T-0001"] }));
    }

    [Fact]
    public void Setting_it_adds_the_line_after_Labels_replaces_it_and_takes_it_out()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", ["To Do", "Done"], "T");
        var a = BoardStore.Create(main, "first", "Words.", labels: ["x"]);
        var b = BoardStore.Create(main, "second");
        var file = Path.Combine(main, "items", b.File);

        Assert.Equal([a.Id], BoardStore.SetBlockedBy(main, b.Id, [a.Id.ToLowerInvariant()]).BlockedBy);
        var lines = File.ReadAllLines(file);
        Assert.Equal($"- **Blocked by:** {a.Id}", lines[Array.FindIndex(lines, l => l.StartsWith("- **Labels:**")) + 1]);

        BoardStore.SetBlockedBy(main, b.Id, [a.Id, "AND-0003"]);
        Assert.Single(File.ReadAllLines(file), l => l.StartsWith("- **Blocked by:**"));
        Assert.Equal([a.Id, "AND-0003"], BoardStore.ReadCards(main).Single(c => c.Id == b.Id).BlockedBy);

        BoardStore.SetBlockedBy(main, b.Id, []);
        Assert.DoesNotContain(File.ReadAllLines(file), l => l.StartsWith("- **Blocked by:**"));
        Assert.Contains("## Description", File.ReadAllText(file));
        Assert.Throws<ArgumentException>(() => BoardStore.SetBlockedBy(main, b.Id, [b.Id]));
    }

    [Fact]
    public void A_blocker_clears_in_QA_done_or_Completed_on_any_board_and_an_unknown_id_is_a_warning()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", ["To Do", "In Progress", "QA Ready", "Done"], "T");
        var phone = BoardStore.CreateBoard(_boards, "Android", ["To Do", "Doing", "Shipped"], "AND");
        var scaffold = BoardStore.Create(phone, "scaffold");
        var api = BoardStore.Create(main, "remote api");
        var waits = BoardStore.Create(main, "pairing");
        BoardStore.SetBlockedBy(main, waits.Id, [scaffold.Id, api.Id, "T-0999"]);

        IReadOnlyList<BlockerState> Now()
        {
            var index = Blockers.Index(CardIndex.Read(_boards));
            return Blockers.Of(index[waits.Id].Card, index);
        }
        var states = Now();
        Assert.Equal([scaffold.Id, api.Id, "T-0999"], states.Select(s => s.Id));
        Assert.Equal("Android", states[0].Card!.Board); // found on the other board
        Assert.All(states.Take(2), s => Assert.False(s.Cleared));
        Assert.False(states[2].Found);
        Assert.True(states[2].IsWarning);
        Assert.True(states[2].Shows);
        Assert.Equal("T-0999: no such card", states[2].Chip);

        BoardStore.Move(main, api.Id, BoardStore.Load(main).Columns.Single(c => c.Name == "In Progress").Id);
        Assert.False(Now()[1].Cleared);
        BoardStore.Move(main, api.Id, BoardStore.Load(main).Columns.Single(c => c.Name == "QA Ready").Id);
        Assert.True(Now()[1].Cleared);
        Assert.False(Now()[1].Shows);
        BoardStore.Move(phone, scaffold.Id, BoardStore.Load(phone).Columns.Single(c => c.Name == "Shipped").Id); // the last column is done
        Assert.True(Now()[0].Cleared);
        BoardStore.Move(phone, scaffold.Id, Board.Completed);
        Assert.True(Now()[0].Cleared);

        var index = Blockers.Index(CardIndex.Read(_boards));
        Assert.Empty(Blockers.Open(index[waits.Id].Card, index)); // the unknown id warns, it isn't waited on
        Assert.Equal([waits.Id], Blockers.Blocks(api.Id, CardIndex.Read(_boards)).Select(c => c.Card.Id));
        Assert.Equal([waits.Id], Blockers.Blocks(scaffold.Id.ToLowerInvariant(), CardIndex.Read(_boards)).Select(c => c.Card.Id));
        Assert.Empty(Blockers.Blocks(waits.Id, CardIndex.Read(_boards)));
    }

    [Fact]
    public void A_cycle_is_found_through_any_number_of_cards_and_shows_even_once_cleared()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", ["To Do", "QA Ready", "Done"], "T");
        var a = BoardStore.Create(main, "a");
        var b = BoardStore.Create(main, "b");
        var c = BoardStore.Create(main, "c");
        var d = BoardStore.Create(main, "d");
        BoardStore.SetBlockedBy(main, a.Id, [b.Id]);
        BoardStore.SetBlockedBy(main, b.Id, [c.Id]);
        BoardStore.SetBlockedBy(main, c.Id, [a.Id]);
        BoardStore.SetBlockedBy(main, d.Id, [a.Id]); // waits on the loop, but isn't in it

        var index = Blockers.Index(CardIndex.Read(_boards));
        Assert.True(Blockers.InCycle(index[a.Id].Card, index));
        Assert.True(Blockers.InCycle(index[c.Id].Card, index));
        Assert.False(Blockers.InCycle(index[d.Id].Card, index));
        var ofA = Blockers.Of(index[a.Id].Card, index).Single();
        Assert.True(ofA.Cycle);
        Assert.Equal($"{b.Id}: a cycle", ofA.Chip);
        Assert.False(Blockers.Of(index[d.Id].Card, index).Single().Cycle);

        BoardStore.Move(main, b.Id, BoardStore.Load(main).Columns.Single(c => c.Name == "QA Ready").Id);
        index = Blockers.Index(CardIndex.Read(_boards));
        ofA = Blockers.Of(index[a.Id].Card, index).Single();
        Assert.True(ofA.Cleared);
        Assert.True(ofA.Shows); // the cycle is still there to fix

        // a card written by hand to wait on itself is a cycle too
        var self = BoardStore.Parse($"# T-0050: me\n\n- **Status:** To Do\n- **Blocked by:** T-0050\n\n## Description\n", "me.md")!;
        Assert.True(Blockers.InCycle(self, new Dictionary<string, BoardCard> { ["T-0050"] = new(self, "Main", "To Do", "me.md", false, false, false) }));
    }
}
