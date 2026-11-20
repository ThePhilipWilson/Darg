using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>One of a card's blockers as things stand: the id it names, the card that id is (null when no board of the
/// project has it), whether that card has cleared (in QA, done or Completed), and whether waiting on it closes a loop
/// back to this card (a cycle).</summary>
public sealed record BlockerState(string Id, BoardCard? Card, bool Cleared, bool Cycle)
{
    public bool Found => Card is not null;
    /// <summary>Shown on the tile: still waited on, or something wrong with it (no such card, a cycle). A cleared blocker
    /// in a cycle still shows: the cycle is the thing to fix.</summary>
    public bool Shows => !Cleared || Cycle || !Found;
    /// <summary>Not a plain wait: an id no board has, or a cycle.</summary>
    public bool IsWarning => !Found || Cycle;
    /// <summary>The chip's words.</summary>
    public string Chip => !Found ? $"{Id}: no such card" : Cycle ? $"{Id}: a cycle" : $"waits on {Id}";
    /// <summary>A line saying what it is and where, for a tooltip or the panel.</summary>
    public string Text => Card is not { } c ? $"{Id}: no board of this project has it"
        : $"{Id}: {c.Card.Title} ({c.Column}{(c.Board.Length > 0 ? " on " + c.Board : "")})"
          + (Cycle ? ", and it waits on this card too: a cycle" : Cleared ? ", cleared" : "");
}

/// <summary>Card dependencies (WOR-0172): a card's <c>- **Blocked by:** AND-0003, WOR-0150</c> line names the cards it
/// waits on, on any of the project's boards. A blocker clears once it is in the QA column, done or Completed (a card
/// agent merges its branch before QA, so the work is there to build on). An id no board has, or a loop of cards each
/// waiting on the next, is shown as a warning rather than dropped.</summary>
public static class Blockers
{
    /// <summary>The field's name, as written in the card file.</summary>
    public const string Field = "Blocked by";

    static readonly Regex IdPattern = new(@"\b([A-Za-z][A-Za-z0-9]*-\d+)\b");

    /// <summary>The ids in a Blocked by value, upper-cased, each once, in the order written. Takes commas, spaces or
    /// semicolons between them, card links (<c>[WOR-0150](card:WOR-0150)</c>) and "none".</summary>
    public static IReadOnlyList<string> ParseIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var ids = new List<string>();
        foreach (Match m in IdPattern.Matches(value))
        {
            var id = m.Groups[1].Value.ToUpperInvariant();
            if (!ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>The value to write for a list of ids: comma separated, or "" for none.</summary>
    public static string Format(IEnumerable<string> ids) => string.Join(", ", ParseIds(string.Join(",", ids)));

    /// <summary>A blocker has cleared: it sits in its board's QA column, its done (last) column, or Completed.</summary>
    public static bool IsCleared(BoardCard c) => c.InQa || c.Done;

    /// <summary>The project's cards by id, for the lookups below (the first when two boards hold the same id).</summary>
    public static Dictionary<string, BoardCard> Index(IEnumerable<BoardCard> cards)
    {
        var index = new Dictionary<string, BoardCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cards) index.TryAdd(c.Card.Id, c);
        return index;
    }

    /// <summary>Each of the card's blockers as things stand, in the order written.</summary>
    public static IReadOnlyList<BlockerState> Of(Card card, IReadOnlyDictionary<string, BoardCard> index)
    {
        if (card.BlockedBy.Count == 0) return [];
        return card.BlockedBy.Select(id =>
        {
            var found = index.GetValueOrDefault(id);
            return new BlockerState(id, found, found is not null && IsCleared(found), Reaches(id, card.Id, index));
        }).ToList();
    }

    /// <summary>The blockers still holding the card up: found and not cleared. Unknown ids and cycles aren't counted
    /// here (they are warnings to fix, not work to wait for).</summary>
    public static IReadOnlyList<BlockerState> Open(Card card, IReadOnlyDictionary<string, BoardCard> index) =>
        Of(card, index).Where(b => b.Found && !b.Cleared).ToList();

    /// <summary>The cards that name this one in their Blocked by line: what it blocks.</summary>
    public static IReadOnlyList<BoardCard> Blocks(string id, IEnumerable<BoardCard> cards) =>
        cards.Where(c => !c.Card.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                         && c.Card.BlockedBy.Contains(id, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(c => c.Card.Id, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Whether following Blocked by lines from <paramref name="from"/> comes to <paramref name="target"/>
    /// (from itself counts: a card blocked by itself is a cycle).</summary>
    public static bool Reaches(string from, string target, IReadOnlyDictionary<string, BoardCard> index)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var todo = new Stack<string>();
        todo.Push(from);
        while (todo.Count > 0)
        {
            var id = todo.Pop();
            if (id.Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
            if (!seen.Add(id) || index.GetValueOrDefault(id) is not { } c) continue;
            foreach (var next in c.Card.BlockedBy) todo.Push(next);
        }
        return false;
    }

    /// <summary>Whether the card is in a cycle: one of its blockers waits, by any path, on it.</summary>
    public static bool InCycle(Card card, IReadOnlyDictionary<string, BoardCard> index) =>
        card.BlockedBy.Any(id => Reaches(id, card.Id, index));
}
