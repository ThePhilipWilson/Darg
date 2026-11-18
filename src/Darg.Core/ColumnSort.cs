namespace Darg.Core;

/// <summary>How a column's cards are ordered on screen (WOR-0146). Manual is board.json's own order (the drag order);
/// the others are a view over it that leaves it untouched.</summary>
public enum SortBy { Manual, Title, Newest, Longest }

/// <summary>A column's sort: what by, and whether the natural order is reversed. Natural is A to Z, newest first, longest
/// in the column first.</summary>
public readonly record struct ColumnSort(SortBy By, bool Reversed = false)
{
    public static readonly ColumnSort Manual = new(SortBy.Manual);
    public bool IsManual => By == SortBy.Manual;

    public static string Label(SortBy by) => by switch
    {
        SortBy.Title => "A-Z", SortBy.Newest => "Newest", SortBy.Longest => "Longest in column", _ => "Manual",
    };

    /// <summary>The cards in this order. Manual keeps them as given; ties keep the given (manual) order.</summary>
    public List<Card> Apply(IEnumerable<Card> cards)
    {
        var list = cards.ToList();
        if (IsManual) return list;
        // Newest: when it was made, its number breaking ties (Created is a date only). Longest: how long since it entered
        // its column (Moved, else Created); a card with neither sorts as newest of all, so it never leads.
        var rev = Reversed;
        IOrderedEnumerable<Card> sorted = By switch
        {
            SortBy.Title => rev ? list.OrderByDescending(c => c.Title, StringComparer.OrdinalIgnoreCase) : list.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            SortBy.Newest => (rev ? list.OrderBy(c => c.Created ?? DateTime.MinValue) : list.OrderByDescending(c => c.Created ?? DateTime.MinValue))
                .ThenBy(c => c.Number * (rev ? 1 : -1)),
            _ => rev ? list.OrderByDescending(c => c.InColumnSince ?? DateTime.MaxValue) : list.OrderBy(c => c.InColumnSince ?? DateTime.MaxValue),
        };
        return sorted.ToList();
    }
}

/// <summary>The sort picked for each column, kept per project beside its shown-boards (not in board.json, which Tatty
/// reads): one line per column, <c>board/listId&lt;TAB&gt;by&lt;TAB&gt;reversed</c>.</summary>
public static class ColumnSorts
{
    public static string FileFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(Path.GetDirectoryName(ReportStore.FolderFor(projectId, instanceFolder))!, "column-sorts");

    public static Dictionary<string, ColumnSort> Load(string projectId, string? instanceFolder = null)
    {
        var all = new Dictionary<string, ColumnSort>();
        try
        {
            var file = FileFor(projectId, instanceFolder);
            if (!File.Exists(file)) return all;
            foreach (var line in File.ReadAllLines(file))
            {
                var p = line.Split('\t');
                if (p.Length >= 2 && Enum.TryParse<SortBy>(p[1], true, out var by) && by != SortBy.Manual)
                    all[p[0]] = new ColumnSort(by, p.Length > 2 && p[2] == "reversed");
            }
        }
        catch (Exception) { }
        return all;
    }

    public static void Save(string projectId, IReadOnlyDictionary<string, ColumnSort> sorts, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, sorts.Where(s => !s.Value.IsManual).OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Select(s => $"{s.Key}\t{s.Value.By}\t{(s.Value.Reversed ? "reversed" : "")}"));
        }
        catch (Exception) { }
    }
}

/// <summary>Stamps Moved on a card whose column changed outside <see cref="BoardStore.Move"/> (an agent editing the
/// card's Status line, a hand edit), noticed between one read of a board and the next. The first read of a folder only
/// learns where cards are, so a change made while the app was closed isn't stamped (the card falls back to its last
/// Moved or Created).</summary>
public sealed class MovedWatch
{
    Dictionary<string, string>? _places;
    string? _folder;

    /// <summary>Returns the ids stamped. A card whose Moved already changed with its column (Move did it) is left.</summary>
    public IReadOnlyList<string> Read(string folder, Board board)
    {
        var now = board.Lists.SelectMany(l => l.Value.Select(c => (Place: l.Key, Card: c))).ToList();
        var known = _places is not null && _folder == folder;
        var before = _places;
        _places = now.ToDictionary(x => x.Card.Id, x => x.Place);
        _folder = folder;
        if (!known) return [];
        var stamped = new List<string>();
        foreach (var (place, card) in now)
        {
            if (!before!.TryGetValue(card.Id, out var was) || was == place) continue;
            if (card.Moved is { } m && DateTime.Now - m < TimeSpan.FromSeconds(10)) continue; // already stamped by Move
            try { BoardStore.MarkMoved(folder, card); stamped.Add(card.Id); } catch (Exception) { }
        }
        return stamped;
    }
}
