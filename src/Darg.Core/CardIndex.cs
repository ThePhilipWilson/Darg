namespace Darg.Core;

/// <summary>A card as found on one of a project's boards: which board, which list (its column's name, or Backlog,
/// Completed, Unsorted), the card file's full path, and whether that list is the board's QA, attention or done one.</summary>
public sealed record BoardCard(Card Card, string Board, string Column, string File, bool InQa, bool InAttention, bool Done);

/// <summary>Every card on a project's boards, for the API's <c>cards</c> and <c>card</c> commands (WOR-0140), so a
/// question about the board ("which QA cards have no agent?") is one read instead of grepping card files.</summary>
public static partial class CardIndex
{
    /// <summary>The words <c>column</c> takes besides a column's name: the board's QA, attention and done lists,
    /// whatever each board calls them (done is the last column and Completed).</summary>
    public static readonly IReadOnlyList<string> Kinds = ["qa", "attention", "done"];

    /// <summary>Every card on every board in the boards folder, board by board in board.json's order.</summary>
    public static List<BoardCard> Read(string? boardsFolder)
    {
        var all = new List<BoardCard>();
        if (boardsFolder is null) return all;
        foreach (var name in BoardStore.List(boardsFolder))
        {
            Board board;
            try { board = BoardStore.Load(Path.Combine(boardsFolder, name)); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { continue; }
            var qa = BoardColumns.Qa(board.Columns) is { } q ? BoardStore.PlaceOf(q, board.Columns) : null;
            var attention = AttentionColumn.Find(board.Columns) is { } a ? BoardStore.PlaceOf(a, board.Columns) : null;
            var last = board.Columns.LastOrDefault()?.Id;
            foreach (var (listId, listName) in board.ListOrder())
                foreach (var card in board.Lists.GetValueOrDefault(listId) ?? [])
                    all.Add(new BoardCard(card, name, listName, Path.GetFullPath(Path.Combine(board.Folder, "items", card.File)),
                        listId == qa, listId == attention, listId == Board.Completed || listId == last));
        }
        return all;
    }

    /// <summary>The card with this id on any of the boards (case aside), or null.</summary>
    public static BoardCard? Find(string? boardsFolder, string id) =>
        Read(boardsFolder).FirstOrDefault(c => c.Card.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the card sits in <paramref name="column"/>: a list's name, or one of <see cref="Kinds"/>.</summary>
    public static bool InColumn(BoardCard c, string column)
    {
        column = column.Trim();
        if (c.Column.Equals(column, StringComparison.OrdinalIgnoreCase)) return true;
        return column.ToLowerInvariant() switch
        {
            "qa" => c.InQa,
            "attention" => c.InAttention,
            "done" => c.Done,
            _ => false,
        };
    }

    /// <summary>The cards that pass every filter given: a column (<see cref="InColumn"/>), a label (case aside), and text
    /// found in the id, title or description. A null or empty filter passes everything.</summary>
    public static IEnumerable<BoardCard> Filter(IEnumerable<BoardCard> cards, string? column, string? label, string? text) =>
        cards.Where(c =>
            (string.IsNullOrWhiteSpace(column) || InColumn(c, column))
            && (string.IsNullOrWhiteSpace(label) || c.Card.Labels.Any(l => l.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (string.IsNullOrWhiteSpace(text) || new[] { c.Card.Id, c.Card.Title, c.Card.Description }.Any(s => s.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))));

    /// <summary>The card's <c>## QA notes</c> section (<see cref="QaNotes"/>), without its heading; "" when it has none. The heading is
    /// read as agents write it: any case, a colon or dated tail (<c>## QA notes (2026-11-18)</c>), trailing spaces. A card holding
    /// more than one such section (an agent's dated one, then Send back's) gives them all, in file order.</summary>
    public static string QaNotesOf(string cardText)
    {
        var text = cardText.Replace("\r\n", "\n");
        return string.Join("\n\n", QaNotesSections(text).Select(s => text[s.Start..s.End].Trim()).Where(s => s.Length > 0));
    }

    /// <summary>Each QA notes section of <paramref name="text"/> (\n line ends): where its heading line starts, where what is under
    /// it starts, and where it ends (the next <c>## </c> heading, or the end).</summary>
    public static List<(int Heading, int Start, int End)> QaNotesSections(string text)
    {
        var padded = "\n" + text;
        var found = new List<(int, int, int)>();
        foreach (System.Text.RegularExpressions.Match m in QaHeadingRx().Matches(padded))
        {
            var start = m.Index + m.Length;
            var next = padded.IndexOf("\n## ", start, StringComparison.Ordinal);
            found.Add((m.Index, start - 1, (next >= 0 ? next : padded.Length) - 1));
        }
        return found;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\n##[ \t]+QA[ \t]+notes(?:[ \t]*[:(\-–—][^\n]*|[ \t]*)(?=\n|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex QaHeadingRx();

    /// <summary>The reports filed for the card, newest first: the reports folder's, then its archive's (top level and each story's).</summary>
    public static List<AgentReport> ReportsFor(string reportsFolder, string cardId)
    {
        var all = ReportStore.List(reportsFolder)
            .Concat(ReportStore.List(ReportArchive.Folder(reportsFolder)))
            .Concat(ReportArchive.List(reportsFolder));
        return all.Where(r => r.Card.Equals(cardId, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(r => Path.GetFullPath(r.File), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(r => r.Date, StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal).ToList();
    }
}
