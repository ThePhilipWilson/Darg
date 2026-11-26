namespace Darg.Core;

/// <summary>Every board of a project, read once (WOR-0227). The board section reads them all off the UI thread on each
/// reload and keeps the result, so what the agents list asks after a reload (each card's board, which cards are done,
/// whether a cut-off agent's card is still being worked) is answered from memory: it used to read every card file of every
/// board twice, on the UI thread, once per row reloaded and again for each agent it forgot.</summary>
public sealed class ProjectBoards
{
    /// <summary>The boards read, in <see cref="BoardStore.List"/>'s order; one that couldn't be read is left out.</summary>
    public IReadOnlyList<Board> Boards { get; }

    ProjectBoards(IReadOnlyList<Board> boards)
    {
        Boards = boards;
        _cardBoards = new Lazy<Dictionary<string, string>>(() =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in Boards) foreach (var c in b.AllCards) map.TryAdd(c.Id, b.Name);
            return map;
        });
        _done = new Lazy<IReadOnlyList<(Card, string, bool)>>(() =>
        {
            var found = new List<(Card, string, bool)>();
            foreach (var b in Boards)
            {
                if (b.Columns.LastOrDefault()?.Name is not { } last) continue;
                var place = BoardStore.PlaceOf(last, b.Columns);
                foreach (var c in b.AllCards)
                {
                    var p = BoardStore.PlaceOf(c.Status, b.Columns);
                    if (p == place || p == Board.Completed) found.Add((c, Path.GetFullPath(Path.Combine(b.Folder, "items", c.File)), p == place));
                }
            }
            return found;
        });
        _doneIds = new Lazy<HashSet<string>>(() => _done.Value.Select(d => d.Item1.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public static ProjectBoards Empty { get; } = new([]);

    /// <summary>Read every board in <paramref name="boardsFolder"/>. A board already loaded (by its folder, as the rows
    /// read theirs in the same pass) is taken as it is rather than read twice.</summary>
    public static ProjectBoards Read(string? boardsFolder, IReadOnlyDictionary<string, Board>? already = null)
    {
        var boards = new List<Board>();
        foreach (var name in BoardStore.List(boardsFolder))
        {
            var folder = Path.Combine(boardsFolder!, name);
            if (already is not null && already.TryGetValue(Key(folder), out var loaded)) { boards.Add(loaded); continue; }
            try { boards.Add(BoardStore.Load(folder)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        return new ProjectBoards(boards);
    }

    /// <summary>The key a board's folder goes by in <see cref="Read"/>'s <c>already</c>: full, no trailing slash, upper case.</summary>
    public static string Key(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/').ToUpperInvariant();

    readonly Lazy<Dictionary<string, string>> _cardBoards;
    readonly Lazy<IReadOnlyList<(Card Card, string File, bool InLast)>> _done;
    readonly Lazy<HashSet<string>> _doneIds;

    /// <summary>Each card's board name by card id (the first board holding it wins). A copy: the caller may keep or change it.</summary>
    public Dictionary<string, string> CardBoards() => new(_cardBoards.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every card in the done (last) column of each board, with its card file; with <paramref name="completedToo"/>,
    /// cards whose Status is Completed as well.</summary>
    public List<(Card Card, string File)> DoneCards(bool completedToo = false) =>
        _done.Value.Where(d => d.InLast || completedToo).Select(d => (d.Card, d.File)).ToList();

    /// <summary>The id of every done card on any board (last column or Completed).</summary>
    public IReadOnlySet<string> DoneCardIds() => _doneIds.Value;

    /// <summary>Whether the card sits in its board's working or needs-more-work column (<see cref="CutOffTurn.StillWorkedOn"/>).</summary>
    public bool StillWorked(string cardId) => CutOffTurn.StillWorkedOn(Boards, cardId);
}
