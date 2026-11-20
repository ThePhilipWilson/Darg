namespace Darg.Core;

/// <summary>One list of a board as the phone shows it (AND-0004): its id in board.json (backlog, a column id, completed),
/// its name, what kind of list it is (<see cref="BoardLayouts.Kinds"/>, or "" for a plain column) and its cards in order.</summary>
public sealed record ListLayout(string Id, string Name, string Kind, IReadOnlyList<Card> Cards);

/// <summary>A board with every list in display order, empty ones too, so a client can draw its columns without
/// reading board.json itself. <see cref="Inbox"/> marks the board a new card lands on by default.</summary>
public sealed record BoardLayout(string Name, bool Inbox, IReadOnlyList<ListLayout> Lists);

/// <summary>A project's boards for a remote client (the Android app, AND-0004): read them column by column, move a card to
/// another list of its board, and add a card straight into a list. Everything goes through <see cref="BoardStore"/>, the
/// code the desktop's board uses, so the files stay as Tatty and Obsidian read them.</summary>
public static class BoardLayouts
{
    /// <summary>The kinds of list a client can name instead of a column's own name, whatever each board calls it:
    /// the backlog, the QA, attention and needs-more-work columns, done (the last column) and completed.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["backlog", "qa", "attention", "rework", "done", "completed"];

    /// <summary>Every board in the boards folder, in name order, each with all its lists. Unsorted shows only when it has cards.</summary>
    public static List<BoardLayout> Read(string? boardsFolder)
    {
        var all = new List<BoardLayout>();
        if (boardsFolder is null) return all;
        var names = BoardStore.List(boardsFolder);
        var inbox = StoryBoards.Inbox(names);
        foreach (var name in names)
        {
            Board board;
            try { board = BoardStore.Load(Path.Combine(boardsFolder, name)); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { continue; }
            var lists = board.ListOrder()
                .Select(l => new ListLayout(l.Id, l.Name, KindOf(board, l.Id), board.Lists.GetValueOrDefault(l.Id) ?? []))
                .ToList();
            all.Add(new BoardLayout(name, name == inbox, lists));
        }
        return all;
    }

    /// <summary>What kind of list this is on its board ("" for a plain column).</summary>
    public static string KindOf(Board board, string listId)
    {
        if (listId == Board.Backlog) return "backlog";
        if (listId == Board.Completed) return "completed";
        if (listId == Board.Unsorted) return "";
        var c = board.Columns;
        string? Id(string? name) => name is null ? null : BoardStore.PlaceOf(name, c);
        if (listId == Id(BoardColumns.Qa(c))) return "qa";
        if (listId == Id(AttentionColumn.Find(c))) return "attention";
        if (listId == Id(BoardColumns.Rework(c))) return "rework";
        if (listId == c.LastOrDefault()?.Id) return "done";
        return "";
    }

    /// <summary>The list of the board that <paramref name="column"/> names: a list's id, its name (case aside), or one of
    /// <see cref="Kinds"/>; null when it names none. "done" is the last column, or completed on a board with no columns.</summary>
    public static string? ListFor(Board board, string column)
    {
        column = column.Trim();
        if (column.Length == 0) return null;
        foreach (var (id, name) in board.ListOrder())
            if (id.Equals(column, StringComparison.OrdinalIgnoreCase) || name.Equals(column, StringComparison.OrdinalIgnoreCase)) return id;
        var kind = column.ToLowerInvariant();
        if (!Kinds.Contains(kind)) return null;
        if (kind == "done" && board.Columns.Count == 0) return Board.Completed;
        return board.ListOrder().Select(l => l.Id).FirstOrDefault(id => KindOf(board, id) == kind);
    }

    /// <summary>Move a card to another list of its own board, before another card or at the end. Returns the board's name
    /// and the list's name. Throws <see cref="ArgumentException"/> for a card no board has or a list its board hasn't.</summary>
    public static (string Board, string List) Move(string boardsFolder, string cardId, string column, string? beforeId = null)
    {
        var found = CardIndex.Find(boardsFolder, cardId) ?? throw new ArgumentException($"No card {cardId} on these boards.");
        var folder = Path.Combine(boardsFolder, found.Board);
        var board = BoardStore.Load(folder);
        var list = ListFor(board, column) ?? throw new ArgumentException(
            $"No column \"{column}\" on {found.Board}. Columns: {string.Join(", ", board.ListOrder().Select(l => l.Name).Concat(Kinds))}.");
        // a card that isn't in that list is no place to land: the end is
        var before = beforeId is null ? null
            : (board.Lists.GetValueOrDefault(list) ?? []).FirstOrDefault(c => c.Id.Equals(beforeId.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
        BoardStore.Move(folder, found.Card.Id, list, before);
        return (found.Board, board.ListOrder().First(l => l.Id == list).Name);
    }

    /// <summary>Make a card on a named board and put it in a list there (the backlog when <paramref name="column"/> is empty):
    /// the phone's quick-add, which knows where it is, so no story-board placing follows. Returns the card as it is after the move.</summary>
    public static Card Add(string boardsFolder, string boardName, string? column, string title, string description = "",
        IEnumerable<string>? labels = null, string priority = "", string? type = null)
    {
        var name = BoardStore.List(boardsFolder).FirstOrDefault(b => b.Equals(boardName.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException($"No board \"{boardName}\". Boards: {string.Join(", ", BoardStore.List(boardsFolder))}.");
        var folder = Path.Combine(boardsFolder, name);
        var board = BoardStore.Load(folder);
        string? list = null;
        if (!string.IsNullOrWhiteSpace(column))
            list = ListFor(board, column) ?? throw new ArgumentException(
                $"No column \"{column}\" on {name}. Columns: {string.Join(", ", board.ListOrder().Select(l => l.Name).Concat(Kinds))}.");
        var labelList = labels?.ToList() ?? [];
        var card = BoardStore.Create(folder, title, description, labelList, priority, type ?? CardTypes.Guess(title + "\n" + description, labelList));
        if (list is not null && list != Board.Backlog) BoardStore.Move(folder, card.Id, list);
        return BoardStore.ReadCards(folder).First(c => c.Id == card.Id);
    }
}
