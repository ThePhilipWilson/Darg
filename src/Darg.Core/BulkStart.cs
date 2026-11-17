namespace Darg.Core;

/// <summary>The board's bulk buttons (WOR-0125): ▶ Work All at the top of the backlog and the working column starts an
/// agent on every card in either with none on it, and 🧪 QA Run All at the top of the QA column runs QA on every card there
/// with no QA agent on it. Which lists carry which button, when to ask first, and what the status line says after.</summary>
public static class BulkStart
{
    /// <summary>More than this many agents at once and the button asks first, in the column, with the count.</summary>
    public const int AskAbove = 3;

    public enum Kind { None, Work, Qa }

    /// <summary>The working column, where a card being worked on sits: In Progress, else the first column.</summary>
    public static Column? Working(IReadOnlyList<Column> columns) =>
        columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? columns.FirstOrDefault();

    /// <summary>The lists Work All starts cards from: the backlog, the first column when it isn't the working one (a To Do,
    /// which Work on moves cards out of, as it does the backlog's) and the working column. Never the done column.</summary>
    public static IReadOnlyList<string> WorkLists(IReadOnlyList<Column> columns)
    {
        var lists = new List<string> { Board.Backlog };
        if (columns.Count > 1) lists.Add(columns[0].Id);
        if (Working(columns) is { } w) lists.Add(w.Id);
        return lists.Distinct().Where(id => !IsDone(id, columns)).ToList();
    }

    /// <summary>The QA column's id, if the board has one.</summary>
    public static string? QaList(IReadOnlyList<Column> columns) =>
        BoardColumns.Qa(columns) is { } name ? BoardStore.PlaceOf(name, columns) : null;

    /// <summary>Which button a list's header carries.</summary>
    public static Kind For(string listId, IReadOnlyList<Column> columns) =>
        WorkLists(columns).Contains(listId) ? Kind.Work : QaList(columns) == listId ? Kind.Qa : Kind.None;

    static bool IsDone(string id, IReadOnlyList<Column> columns) => columns.Count > 0 && columns[^1].Id == id;

    /// <summary>The button's words: "▶ Work All (5)", "🧪 QA Run All (2)".</summary>
    public static string Label(Kind kind, int count) => kind == Kind.Qa ? $"🧪 QA Run All ({count})" : $"▶ Work All ({count})";

    public static bool NeedsAsking(int count) => count > AskAbove;

    /// <summary>The question asked in the column before starting more than <see cref="AskAbove"/>.</summary>
    public static string Question(Kind kind, int count) =>
        kind == Kind.Qa ? $"Start {count} QA agents, one per card?" : $"Start {count} agents, one per card?";

    /// <summary>What the status line says once they're started: who started, and who didn't and why.</summary>
    public static string Summary(Kind kind, IReadOnlyList<string> started, IReadOnlyList<(string Id, string Why)> failed)
    {
        var what = kind == Kind.Qa ? "QA" : "work";
        var said = started.Count switch
        {
            0 => $"Started no {what} agents",
            1 => $"Started {what} on {started[0]}",
            _ => $"Started {what} on {started.Count} cards: {string.Join(", ", started)}",
        };
        if (failed.Count == 0) return said;
        return $"{said}; couldn't start {string.Join("; ", failed.Select(f => $"{f.Id} ({f.Why})"))}";
    }
}
