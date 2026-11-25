namespace Darg.Core;

/// <summary>The board's bulk buttons (WOR-0125): ▶ Work All at the top of the working column (and a To Do) starts an agent
/// on every card in those with none on it, the working column's taking the backlog's cards too (WOR-0218); ✦ Improve All at
/// the top of the backlog starts an improve agent on every thin card there with none on it; and 🧪 QA Run All at the top of the QA column runs QA on every card there
/// with no QA agent on it. Which lists carry which button, when to ask first, and what the status line says after.</summary>
public static class BulkStart
{
    /// <summary>More than this many agents at once and the button asks first, in the column, with the count.</summary>
    public const int AskAbove = 3;

    public enum Kind { None, Work, Qa, Improve }

    /// <summary>The working column, where a card being worked on sits: In Progress, else the first column.</summary>
    public static Column? Working(IReadOnlyList<Column> columns) =>
        columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault();

    /// <summary>The lists Work All starts cards from: the first column when it isn't the working one (a To Do) and the
    /// working column. Never the backlog (WOR-0215: it has Improve All) or the done column.</summary>
    public static IReadOnlyList<string> WorkLists(IReadOnlyList<Column> columns)
    {
        var lists = new List<string>();
        if (columns.Count > 1) lists.Add(columns[0].Id);
        if (Working(columns) is { } w) lists.Add(w.Id);
        return lists.Distinct().Where(id => !IsDone(id, columns)).ToList();
    }

    /// <summary>The lists a list's button starts cards from (WOR-0218). The working column's ▶ Work All takes the backlog's
    /// cards too, so a board whose first column is In Progress (no To Do) still has something to count; a To Do's keeps to
    /// <see cref="WorkLists"/>, and the backlog's own button is ✦ Improve All.</summary>
    public static IReadOnlyList<string> Sources(string listId, IReadOnlyList<Column> columns) => For(listId, columns) switch
    {
        Kind.Work when Working(columns)?.Id == listId => [.. WorkLists(columns), Board.Backlog],
        Kind.Work => WorkLists(columns),
        Kind.Qa => [listId],
        Kind.Improve => [Board.Backlog],
        _ => [],
    };

    /// <summary>The list whose button the API's work-all / qa-all / improve presses: the working column, the QA column, the
    /// backlog. Null when the board has no such button.</summary>
    public static string? ButtonList(Kind kind, IReadOnlyList<Column> columns) => kind switch
    {
        Kind.Work => Working(columns) is { } w && For(w.Id, columns) == Kind.Work ? w.Id : null,
        Kind.Qa => QaList(columns),
        Kind.Improve => Board.Backlog,
        _ => null,
    };

    /// <summary>The QA column's id, if the board has one.</summary>
    public static string? QaList(IReadOnlyList<Column> columns) =>
        BoardColumns.Qa(columns) is { } name ? BoardStore.PlaceOf(name, columns) : null;

    /// <summary>Which button a list's header carries.</summary>
    public static Kind For(string listId, IReadOnlyList<Column> columns) =>
        listId == Board.Backlog ? Kind.Improve : WorkLists(columns).Contains(listId) ? Kind.Work : QaList(columns) == listId ? Kind.Qa : Kind.None;

    static bool IsDone(string id, IReadOnlyList<Column> columns) => columns.Count > 0 && columns[^1].Id == id;

    /// <summary>The button's words: "▶ Work All (5)", "🧪 QA Run All (2)"; with cards it leaves out for their open blockers
    /// (WOR-0173), "▶ Work All (4, 3 blocked)".</summary>
    public static string Label(Kind kind, int count, int blocked = 0) =>
        kind == Kind.Qa ? $"🧪 QA Run All ({count})" : kind == Kind.Improve ? $"✦ Improve All ({count})" : blocked > 0 ? $"▶ Work All ({count}, {blocked} blocked)" : $"▶ Work All ({count})";

    public static bool NeedsAsking(int count) => count > AskAbove;

    /// <summary>The question asked in the column before starting more than <see cref="AskAbove"/>.</summary>
    public static string Question(Kind kind, int count) =>
        kind == Kind.Qa ? $"Start {count} QA agents, one per card?" : kind == Kind.Improve ? $"Start {count} improve agents, one per card?" : $"Start {count} agents, one per card?";

    /// <summary>What the status line says once they're started: who started, and who didn't and why.</summary>
    public static string Summary(Kind kind, IReadOnlyList<string> started, IReadOnlyList<(string Id, string Why)> failed,
        IReadOnlyList<(string Id, IReadOnlyList<BlockerState> Open)>? blocked = null)
    {
        var what = kind == Kind.Qa ? "QA" : kind == Kind.Improve ? "improve" : "work";
        var said = started.Count switch
        {
            0 => $"Started no {what} agents",
            1 => $"Started {what} on {started[0]}",
            _ => $"Started {what} on {started.Count} cards: {string.Join(", ", started)}",
        };
        if (failed.Count > 0) said += $"; couldn't start {string.Join("; ", failed.Select(f => $"{f.Id} ({f.Why})"))}";
        if (blocked is { Count: > 0 }) said += "; " + BlockedStart.LeftOut(blocked);
        return said;
    }
}
