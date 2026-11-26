using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0221: a work or rework agent cut off mid-turn carries on only while its card is still being worked: in its
/// board's working column or its needs-more-work column, on whichever of the project's boards holds it.</summary>
public sealed class CutOffTurnTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "darg-cutoff-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _boards;

    public CutOffTurnTests() => _boards = Directory.CreateDirectory(Path.Combine(_root, "tasks")).FullName;
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Theory]
    [InlineData("In Progress", true)]
    [InlineData("needs more work", true)]
    [InlineData("To Do", false)]
    [InlineData("QA Ready", false)]
    [InlineData(BoardStore.AttentionName, false)]
    [InlineData("Done", false)]
    [InlineData("Backlog", false)]
    public void Only_the_working_and_needs_more_work_columns_count(string column, bool worked)
    {
        BoardStore.CreateBoard(_boards, "Other", ["To Do", "In Progress", "Done"], "O");
        var main = BoardStore.CreateBoard(_boards, "Main", ["To Do", "In Progress", "QA Ready", BoardStore.AttentionName, "needs more work", "Done"], "T");
        var card = BoardStore.Create(main, "fix the logo", "");
        if (column != "Backlog")
            BoardStore.Move(main, card.Id, BoardStore.Load(main).Columns.First(c => c.Name == column).Id);

        Assert.Equal(worked, CutOffTurn.StillWorked(_boards, card.Id));
        Assert.Equal(worked, CutOffTurn.StillWorked(_boards, card.Id.ToLowerInvariant()));
    }

    [Fact]
    public void A_card_no_board_has_is_not_being_worked()
    {
        BoardStore.CreateBoard(_boards, "Main", ["To Do", "In Progress", "Done"], "T");
        Assert.False(CutOffTurn.StillWorked(_boards, "T-0042"));
        Assert.False(CutOffTurn.StillWorked(null, "T-0042"));
        Assert.False(CutOffTurn.StillWorked(Path.Combine(_root, "missing"), "T-0042"));
    }
}
