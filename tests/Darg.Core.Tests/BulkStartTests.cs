using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

public sealed class BulkStartTests
{
    static readonly Column[] Philips = [new("c2", "In Progress"), new("c4", "QA Ready"), new("c5", "For Human Attention"), new("c6", "needs more work"), new("c3", "Done")];
    static readonly Column[] Default = [new("c1", "To Do"), new("c2", "In Progress"), new("c3", "For Human Attention"), new("c4", "Done")];

    [Fact]
    public void WorkAllSitsOnTheBacklogAndInProgress()
    {
        Assert.Equal([Board.Backlog, "c2"], BulkStart.WorkLists(Philips));
        Assert.Equal(BulkStart.Kind.Work, BulkStart.For(Board.Backlog, Philips));
        Assert.Equal(BulkStart.Kind.Work, BulkStart.For("c2", Philips));
        Assert.Equal(BulkStart.Kind.None, BulkStart.For("c5", Philips));
        Assert.Equal(BulkStart.Kind.None, BulkStart.For("c6", Philips));
        Assert.Equal(BulkStart.Kind.None, BulkStart.For("c3", Philips));
        Assert.Equal(BulkStart.Kind.None, BulkStart.For(Board.Completed, Philips));
    }

    [Fact]
    public void ToDoCountsAsWaitingToo()
    {
        // Work on moves a card out of the first column as it does out of the backlog
        Assert.Equal([Board.Backlog, "c1", "c2"], BulkStart.WorkLists(Default));
        Assert.Equal(BulkStart.Kind.Work, BulkStart.For("c1", Default));
    }

    [Fact]
    public void QaRunAllSitsOnTheQaColumnOnly()
    {
        Assert.Equal("c4", BulkStart.QaList(Philips));
        Assert.Equal(BulkStart.Kind.Qa, BulkStart.For("c4", Philips));
        Assert.Null(BulkStart.QaList(Default));
    }

    [Fact]
    public void NeverTheDoneColumn()
    {
        Assert.Equal([Board.Backlog], BulkStart.WorkLists([new("c1", "Done")]));
        Assert.Equal([Board.Backlog], BulkStart.WorkLists([]));
    }

    [Fact]
    public void AsksAboveThree()
    {
        Assert.False(BulkStart.NeedsAsking(3));
        Assert.True(BulkStart.NeedsAsking(4));
        Assert.Equal("▶ Work All (5)", BulkStart.Label(BulkStart.Kind.Work, 5));
        Assert.Equal("🧪 QA Run All (0)", BulkStart.Label(BulkStart.Kind.Qa, 0));
    }

    [Fact]
    public void SummarySaysWhoStartedAndWhoDidNot()
    {
        Assert.Equal("Started work on 2 cards: A-1, A-2", BulkStart.Summary(BulkStart.Kind.Work, ["A-1", "A-2"], []));
        Assert.Equal("Started QA on A-1; couldn't start A-2 (no claude)", BulkStart.Summary(BulkStart.Kind.Qa, ["A-1"], [("A-2", "no claude")]));
    }
}
