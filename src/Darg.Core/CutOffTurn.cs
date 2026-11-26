namespace Darg.Core;

/// <summary>A work or rework agent cut off mid-turn by Darg closing, crashing or updating (WOR-0221) carries on by itself
/// when Darg opens again, as long as its card is still being worked: in its board's working column (In Progress, else the
/// first) or its needs-more-work column. A card moved on meanwhile (QA, attention, done) or back to To Do isn't.</summary>
public static class CutOffTurn
{
    /// <summary>Whether the card with this id sits in its board's working or needs-more-work column, on any of the boards
    /// in the folder. False when no board has it.</summary>
    public static bool StillWorked(string? boardsFolder, string cardId)
    {
        if (boardsFolder is null || !Directory.Exists(boardsFolder)) return false;
        return StillWorkedOn(ProjectBoards.Read(boardsFolder).Boards, cardId);
    }

    /// <summary>The same, on boards already read (WOR-0227: the board section's last read, so the UI thread reads nothing).</summary>
    public static bool StillWorkedOn(IEnumerable<Board> boards, string cardId)
    {
        foreach (var board in boards)
            foreach (var (listId, cards) in board.Lists)
                if (cards.Any(c => c.Id.Equals(cardId, StringComparison.OrdinalIgnoreCase)))
                    return listId == BulkStart.Working(board.Columns)?.Id
                        || BoardColumns.Rework(board.Columns) is { } rework && listId == BoardStore.PlaceOf(rework, board.Columns);
        return false;
    }
}
