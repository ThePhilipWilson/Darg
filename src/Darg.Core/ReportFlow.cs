namespace Darg.Core;

/// <summary>Which cards have just entered the attention, QA or done column of one board folder, between one read of it
/// and the next (WOR-0092, WOR-0102), whoever moved them: a drag, an agent editing the card and board.json, a QA check.
/// The first read of a folder only learns who is where, so moves made while the app was closed make nothing.</summary>
public sealed class ColumnWatch
{
    HashSet<string>? _seen, _attention, _qa, _done;
    string? _folder;

    public sealed record Moves(IReadOnlyList<Card> Attention, IReadOnlyList<Card> Qa, IReadOnlyList<Card> Done);

    public Moves Read(string folder, Board board) => Read(folder, board.Columns, board.AllCards.ToList());

    public Moves Read(string folder, IReadOnlyList<Column> columns, IReadOnlyList<Card> cards)
    {
        HashSet<string> In(params string?[] places) =>
            cards.Where(c => places.Contains(BoardStore.PlaceOf(c.Status, columns))).Select(c => c.Id).ToHashSet();
        string? Place(string? column) => column is null ? null : BoardStore.PlaceOf(column, columns);
        var attention = In(Place(AttentionColumn.Find(columns)));
        var qa = In(Place(BoardColumns.Qa(columns)));
        // done: the last column, or Completed (a board with no done column, or a Status set to Completed by hand)
        var done = In(Place(columns.LastOrDefault()?.Name), Board.Completed);
        var known = _seen is not null && _folder == folder;
        var before = (_seen, _attention, _qa, _done);
        (_seen, _attention, _qa, _done, _folder) = (cards.Select(c => c.Id).ToHashSet(), attention, qa, done, folder);
        if (!known) return new([], [], []);
        // a card new to the board landing straight in attention counts (it was put there); in QA or done it doesn't
        var a = cards.Where(c => attention.Contains(c.Id) && !before._attention!.Contains(c.Id)).ToList();
        var moved = cards.Where(c => before._seen!.Contains(c.Id)).ToList();
        var q = moved.Where(c => qa.Contains(c.Id) && !before._qa!.Contains(c.Id)).ToList();
        var d = moved.Where(c => done.Contains(c.Id) && !before._done!.Contains(c.Id)).ToList();
        return new(a, q, d);
    }
}

/// <summary>What Darg does with a card's reports as it moves (WOR-0102). <paramref name="agentPurpose"/> in each is the
/// purpose (<see cref="AgentPurposes"/>) of an agent starting or working on the card right now, "" for one saved before
/// agents had one, or null when none is.</summary>
public static class ReportFlow
{
    static readonly TimeSpan QaWithin = TimeSpan.FromMinutes(2), DoneWithin = TimeSpan.FromMinutes(10);

    /// <summary>A card has entered the QA column: its QA report, unless an agent is at work on it (it writes its own as
    /// its last step) or one was written in the last two minutes. Returns the file written, or null.</summary>
    public static string? EnteredQa(string reports, Card card, string cardFile, string repo, DateTime now, string? agentPurpose) =>
        agentPurpose is not null ? null : DecisionReport.Write(reports, card, cardFile, repo, now, QaWithin);

    /// <summary>A card has landed in the attention column (WOR-0092): its decision report, whoever moved it, unless one
    /// was written in the last two minutes.</summary>
    public static string? EnteredAttention(string reports, Card card, string cardFile, string repo, DateTime now) =>
        DecisionReport.Write(reports, card, cardFile, repo, now, QaWithin);

    /// <summary>A card has reached done: its QA reports go to <c>reports/archive/</c>, and a done report is written
    /// unless a card agent still at work will write its own (a QA agent writes none, so Darg writes it then too).
    /// Returns the QA reports archived and the done report written (or null).</summary>
    public static (List<AgentReport> Archived, string? Done) EnteredDone(string reports, Card card, string cardFile, string repo, DateTime now, string? agentPurpose)
    {
        var archived = DecisionReport.Archive(reports, card.Id);
        if (agentPurpose is not null && agentPurpose != AgentPurposes.Qa) return (archived, null);
        return (archived, DecisionReport.WriteDone(reports, card, cardFile, repo, now, archived, DoneWithin));
    }
}
