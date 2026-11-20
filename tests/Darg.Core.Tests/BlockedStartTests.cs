using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0173: the words Darg uses when it holds a blocked card back, and what a card agent is told of its blockers.</summary>
public sealed class BlockedStartTests
{
    static Card C(string id, string title = "a card", params string[] blockedBy) =>
        new(id, title, "", "", "", [], $"{id}.md", "") { BlockedBy = blockedBy };

    static BoardCard On(Card card, string column, bool qa = false, bool done = false) =>
        new(card, "Main", column, $"D:/b/items/{card.File}", qa, false, done);

    static readonly Card Scaffold = C("AND-0003", "the scaffold");
    static readonly Card Api = C("AND-0002", "remote access");

    static IReadOnlyList<BlockerState> OpenOf(Card card, params BoardCard[] cards) => Blockers.Open(card, Blockers.Index(cards));

    [Fact]
    public void TheQuestionNamesEachOpenBlockerAndWhereItIs()
    {
        var card = C("AND-0004", "pairing", "AND-0003");
        var open = OpenOf(card, On(Scaffold, "In Progress"));
        Assert.Equal("Blocked by AND-0003, In Progress. Start anyway?", BlockedStart.Question(open));

        var two = OpenOf(C("AND-0005", "needs both", "AND-0003", "AND-0002"), On(Scaffold, "In Progress"), On(Api, "Backlog"));
        Assert.Equal("Blocked by AND-0003, In Progress; AND-0002, Backlog. Start anyway?", BlockedStart.Question(two));
    }

    [Fact]
    public void ABlockerInQaOrDoneOrNowhereDoesntHoldTheCard()
    {
        var card = C("AND-0004", "pairing", "AND-0003", "AND-0002", "AND-0099");
        Assert.Empty(OpenOf(card, On(Scaffold, "QA Ready", qa: true), On(Api, "Done", done: true)));
        Assert.Single(OpenOf(card, On(Scaffold, "QA Ready", qa: true), On(Api, "In Progress")));
    }

    [Fact]
    public void TheRefusalSaysHowToStartItAnyway()
    {
        var open = OpenOf(C("AND-0004", "pairing", "AND-0003"), On(Scaffold, "In Progress"));
        Assert.Equal("AND-0004 is blocked by AND-0003, In Progress: it waits until that is in QA or done. Send force: true to start it anyway.",
            BlockedStart.Refusal("AND-0004", open));
    }

    [Fact]
    public void WorkAllsLabelAndSummarySayWhatWasLeftOut()
    {
        Assert.Equal("▶ Work All (4, 3 blocked)", BulkStart.Label(BulkStart.Kind.Work, 4, 3));
        Assert.Equal("▶ Work All (4)", BulkStart.Label(BulkStart.Kind.Work, 4, 0));
        Assert.Equal("🧪 QA Run All (2)", BulkStart.Label(BulkStart.Kind.Qa, 2, 5));

        var open = OpenOf(C("AND-0004", "pairing", "AND-0003"), On(Scaffold, "In Progress"));
        Assert.Equal("Started work on AND-0001; left out 1 blocked: AND-0004 (waits on AND-0003)",
            BulkStart.Summary(BulkStart.Kind.Work, ["AND-0001"], [], [("AND-0004", open)]));
        Assert.Equal("Started work on AND-0001", BulkStart.Summary(BulkStart.Kind.Work, ["AND-0001"], [], []));
    }

    [Fact]
    public void TheAgentIsToldEachBlockersColumnAndNewestReport()
    {
        var card = C("AND-0004", "pairing", "AND-0003", "AND-0002", "AND-0099");
        var all = Blockers.Of(card, Blockers.Index([On(Scaffold, "QA Ready", qa: true), On(Api, "In Progress")]));
        var lines = BlockedStart.PromptLines(all, id => id == "AND-0003" ? "D:/r/20261120-1200-AND-0003-qa.md" : null);
        Assert.Equal("This card is blocked by (its Blocked by line):", lines[0]);
        Assert.Equal("- AND-0003: the scaffold (QA Ready, cleared). Its newest report: D:/r/20261120-1200-AND-0003-qa.md", lines[1]);
        Assert.Equal("- AND-0002: remote access (In Progress, not finished yet). It has no report yet.", lines[2]);
        Assert.Equal("- AND-0099: no board of this project has it.", lines[3]);
        Assert.Contains("started anyway", lines[^1]);

        Assert.Empty(BlockedStart.PromptLines([], _ => null));
        var cleared = Blockers.Of(C("AND-0004", "pairing", "AND-0003"), Blockers.Index([On(Scaffold, "Done", done: true)]));
        Assert.DoesNotContain("started anyway", BlockedStart.PromptLines(cleared, _ => null)[^1]);
    }
}
