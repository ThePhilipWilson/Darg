using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A board per story (WOR-0087). A project's boards are its stories, with one inbox board where new cards
/// land: the default board set from a tab's menu (<see cref="DefaultBoard"/>, WOR-0219), else one named Inbox, Main or
/// Backlog, else the first. A card made from a note goes there, and so do Fettle's and + Card on it. Claude Haiku then
/// reads the card and the other boards (their names and some of their cards' titles) and moves the card to a story it
/// is plainly part of, in the background; anything less (a near fit, a shared word, unsure) stays on the inbox. The
/// Board section can show several boards at once, one row each (<see cref="ShownBoards"/>).</summary>
public static class StoryBoards
{
    /// <summary>The names an inbox board goes by, in the order they win, when no default is set.</summary>
    public static readonly IReadOnlyList<string> InboxNames = ["Inbox", "Main", "Backlog"];

    /// <summary>The board new cards land on: <paramref name="preferred"/> (the default set for the project) while it is
    /// one of the boards, else one named Inbox, Main or Backlog, else the first; null for none.</summary>
    public static string? Inbox(IReadOnlyList<string> boards, string? preferred = null) =>
        (preferred is { Length: > 0 } ? boards.FirstOrDefault(b => b.Equals(preferred.Trim(), StringComparison.OrdinalIgnoreCase)) : null)
        ?? InboxNames.Select(n => boards.FirstOrDefault(b => b.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(b => b is not null)
        ?? boards.FirstOrDefault();

    /// <summary>The most card titles a board is described by in the prompt (its newest).</summary>
    public const int TitlesPerBoard = 15;

    /// <summary>What Haiku is asked: which story board the card is plainly part of, or none. Strict (WOR-0219): a card
    /// stays on the default board unless it is one more piece of a single story's work, so a near fit, a shared word or
    /// a guess is none. Each board's name is on a line of its own after "Board:", apart from its cards' titles: with
    /// name and titles on one line Haiku sometimes answered with a card's title, or the new card's own. Haiku says how it
    /// decided before answering on the last line: asked for the name alone, it left a card plainly part of a story on the
    /// default board on up to 9 tries in 10, swayed by whatever else it had loaded (measured 2026-11-26: the live test's
    /// merge card reached its board on 1 of 10 from one folder and 10 of 10 from another). Reasoning first, story cards
    /// reached their board on 35 of 36 tries across both folders, and the cards that fit no story stayed on 54 of 54.</summary>
    public static string Prompt(Card card, IReadOnlyList<(string Name, IReadOnlyList<string> Titles)> stories)
    {
        var description = card.Description.Trim();
        if (description.Length > 1500) description = description[..1500];
        var lines = new List<string>
        {
            "A project keeps a board per story: one feature or piece of work, planned as a set of cards. New cards land on the " +
            "project's default board and stay there unless they are plainly part of one story.",
            "Move the new card to a story board only when it is clearly one more piece of that story's own work: the same " +
            "feature, something its cards build or need. Reply none when the card is about another part of the app, when it only " +
            "shares a word or a topic with a story (both mention boards, sync, agents or the phone), when it fits only partly, " +
            "when more than one story could take it, or when you aren't sure. Most new cards are none.",
            "First say in two or three short sentences how you decided. Then, on the last line by itself, the answer: one " +
            "board's name exactly as written after \"Board:\" below, or none. Never a card's title.",
            "",
            "Story boards:",
        };
        foreach (var (name, titles) in stories)
        {
            lines.Add($"Board: {name}");
            lines.Add("  Its cards: " + (titles.Count > 0 ? string.Join("; ", titles) : "none yet"));
        }
        lines.AddRange(["", "New card:", "Title: " + card.Title]);
        if (description.Length > 0) lines.Add("Description: " + description);
        return string.Join("\n", lines);
    }

    /// <summary>A model's reply as one of the boards, or null for none or for a reply that isn't one. The answer is the
    /// reply's last line; the lines above say how it decided and may name any board, so they are never read. What comes
    /// back wrong from a model asked to pick from a list, and is refused or unwrapped here: wrapping (quotes, backticks,
    /// bold, a list dash, "Answer:" or "Board:", a full stop: stripped); talk on the last line (a sentence around the
    /// name, or the reasoning run into it: refused, since a name inside a sentence could be a mention of the wrong
    /// board); a name not on the list or made up (refused); and "none", "no board", "N/A" (none).</summary>
    public static string? Pick(string reply, IReadOnlyList<string> boards)
    {
        var lines = reply.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("```", StringComparison.Ordinal)).ToList();
        if (lines.Count == 0) return null;
        var t = Regex.Replace(lines[^1], @"^(?:[-*]\s+)?(?:\*\*)?(?:(?:final\s+)?answer\s*:\s*)?(?:\*\*)?(?:board\s*:\s*)?", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t.Trim().Trim('"', '\'', '*', '`', '“', '”', '‘', '’', ' '), @"\s+", " ").TrimEnd('.', ' ');
        return boards.FirstOrDefault(b => b.Equals(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Haiku's board for a card, or null for none (or when Claude isn't there or replies with something
    /// <see cref="Pick"/> refuses). Never throws.</summary>
    public static async Task<string?> Suggest(Card card, IReadOnlyList<(string Name, IReadOnlyList<string> Titles)> stories, TimeSpan? timeout = null)
    {
        var reply = await Haiku.Ask(Prompt(card, stories), timeout ?? TimeSpan.FromSeconds(60));
        return reply is null ? null : Pick(reply, stories.Select(s => s.Name).ToList());
    }

    /// <summary>The story boards a card on <paramref name="inbox"/> could go to, each with its newest card titles.</summary>
    public static List<(string Name, IReadOnlyList<string> Titles)> Stories(string boardsFolder, string inbox) =>
        BoardStore.List(boardsFolder).Where(b => !b.Equals(inbox, StringComparison.OrdinalIgnoreCase))
            .Select(b => (b, (IReadOnlyList<string>)BoardStore.ReadCards(Path.Combine(boardsFolder, b))
                .Where(c => !c.IsStory).OrderByDescending(c => c.Number).Take(TitlesPerBoard).Select(c => c.Title).ToList()))
            .ToList();

    /// <summary>Move a card just made on the inbox board to the story board it fits, if any. Left alone when there is
    /// no other board, when the card has gone from the inbox or been moved past its first column meanwhile (someone
    /// is on it), or when <paramref name="busy"/> says an agent holds it. Returns the board it went to, or null.
    /// Never throws.</summary>
    public static async Task<string?> Place(string boardsFolder, string inbox, string cardId, Func<string, bool>? busy = null,
        Func<Card, IReadOnlyList<(string Name, IReadOnlyList<string> Titles)>, Task<string?>>? suggest = null)
    {
        try
        {
            var stories = await Task.Run(() => Stories(boardsFolder, inbox));
            if (stories.Count == 0) return null;
            var from = Path.Combine(boardsFolder, inbox);
            if (!Waiting(from, cardId, out var card)) return null;
            var to = await (suggest ?? ((c, s) => Suggest(c, s)))(card, stories);
            if (to is null) return null;
            // read again: it may have been picked up while Haiku thought
            if (!Waiting(from, cardId, out _) || busy?.Invoke(cardId) == true) return null;
            BoardStore.Transfer(from, Path.Combine(boardsFolder, to), cardId);
            return to;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The card is still on the board, in its backlog or first column: not yet picked up.</summary>
    static bool Waiting(string boardFolder, string cardId, out Card card)
    {
        card = null!;
        var board = BoardStore.Load(boardFolder);
        if (board.AllCards.FirstOrDefault(c => c.Id == cardId) is not { } found) return false;
        card = found;
        var place = BoardStore.PlaceOf(found.Status, board.Columns);
        return place == Board.Backlog || place == Board.Unsorted || (board.Columns.Count > 0 && place == board.Columns[0].Id);
    }
}

/// <summary>The project's default board (WOR-0219), set from a board tab's menu: where new cards land (a note's
/// ticket, Fettle's new-card, + Card on the All board) and where a card Haiku isn't sure of stays. A file per project
/// beside <see cref="ShownBoards"/> (default-board, the board's name), not in the boards folder. With none set, or
/// once the board it names is gone or renamed, <see cref="StoryBoards.Inbox"/> goes back to the name guess.</summary>
public static class DefaultBoard
{
    public static string FileFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(Path.GetDirectoryName(ShownBoards.FileFor(projectId, instanceFolder))!, "default-board");

    /// <summary>The board set as default, or null for none.</summary>
    public static string? Load(string projectId, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            return File.Exists(file) && File.ReadAllLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) is { } name ? name : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Set the default board; null or blank clears it. False when the file couldn't be written.</summary>
    public static bool Save(string projectId, string? board, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            if (string.IsNullOrWhiteSpace(board)) { File.Delete(file); return true; }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, board.Trim() + Environment.NewLine);
            return true;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Which boards the Board section shows at once (WOR-0087), beside the one picked: a file per project in
/// the instance (projects/&lt;project&gt;/shown-boards, a board name a line), not in the boards folder, which Tatty
/// and Obsidian read.</summary>
public static class ShownBoards
{
    public static string FileFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(Path.GetDirectoryName(ReportStore.FolderFor(projectId, instanceFolder))!, "shown-boards");

    public static List<string> Load(string projectId, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            return File.Exists(file)
                ? File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }
        catch (Exception) { return []; }
    }

    public static void Save(string projectId, IEnumerable<string> boards, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, boards);
        }
        catch (Exception) { }
    }
}

/// <summary>The order of a project's board tabs (WOR-0213): dragged into place, or sorted once by name or by age, and
/// kept in the instance beside <see cref="ShownBoards"/> (projects/&lt;project&gt;/board-order, a board name a line), not
/// in the boards folder. A board not in the file (new, or none set) goes after those that are, A to Z; a name in the
/// file with no board is left out. A sort writes the whole order, so dragging afterwards carries on from it.</summary>
public static class BoardOrder
{
    public static string FileFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(Path.GetDirectoryName(ShownBoards.FileFor(projectId, instanceFolder))!, "board-order");

    public static List<string> Load(string projectId, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            return File.Exists(file)
                ? File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }
        catch (Exception) { return []; }
    }

    public static void Save(string projectId, IEnumerable<string> boards, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, boards);
        }
        catch (Exception) { }
    }

    /// <summary>The boards in the kept order: those named in <paramref name="saved"/> first, as there, then the rest
    /// A to Z.</summary>
    public static List<string> Arrange(IEnumerable<string> boards, IReadOnlyList<string> saved)
    {
        var all = boards.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);
        var first = saved.Where(known.Contains).Select(s => all.First(b => b.Equals(s, StringComparison.OrdinalIgnoreCase))).ToList();
        var placed = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
        return [.. first, .. all.Where(b => !placed.Contains(b)).Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A to Z by name.</summary>
    public static List<string> ByName(IEnumerable<string> boards) => boards.Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Oldest first, by when each board's folder was made (A to Z between boards made at the same instant).
    /// A board whose folder can't be read goes last. A fresh clone or copy makes every folder at once, so there the
    /// order is the order they were copied in.</summary>
    public static List<string> ByAge(string boardsFolder, IEnumerable<string> boards) =>
        boards.Select(b => (Name: b, Made: Made(Path.Combine(boardsFolder, b))))
            .OrderBy(x => x.Made).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.Name).ToList();

    static DateTime Made(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.GetCreationTimeUtc(folder) : DateTime.MaxValue; }
        catch (Exception) { return DateTime.MaxValue; }
    }
}

/// <summary>How tall each board's row is when several show at once (WOR-0160). A row dragged to a height by its
/// divider keeps it (a file per project in the instance, projects/&lt;project&gt;/board-heights, a board and its
/// height a line, tab between); the rows not set share what is left, none smaller than <see cref="FitMin"/>, and
/// past that the rows scroll. One row on its own fills the space whatever it was set to.</summary>
public static class BoardRowHeights
{
    /// <summary>The smallest a divider makes a row: its name, its columns' heads and a card or so.</summary>
    public const double Min = 140;
    /// <summary>The smallest the rows not set are shared down to before the rows scroll instead.</summary>
    public const double FitMin = 180;

    /// <summary>Each row's height, in order: a set one as set (no less than <see cref="Min"/>), the rest sharing
    /// what is left of <paramref name="available"/>; with every row set and room to spare, the last takes it.</summary>
    public static double[] Fit(double available, IReadOnlyList<double?> set)
    {
        if (set.Count == 0) return [];
        if (set.Count == 1) return [Math.Max(available, Min)];
        var heights = set.Select(h => h is { } v ? Math.Max(Min, Math.Round(v)) : double.NaN).ToArray();
        var unset = heights.Count(double.IsNaN);
        var left = available - heights.Where(h => !double.IsNaN(h)).Sum();
        if (unset > 0)
        {
            var each = Math.Max(FitMin, Math.Floor(left / unset));
            for (var i = 0; i < heights.Length; i++) if (double.IsNaN(heights[i])) heights[i] = each;
        }
        else if (left > 0) heights[^1] += Math.Floor(left);
        return heights;
    }

    public static string FileFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(Path.GetDirectoryName(ShownBoards.FileFor(projectId, instanceFolder))!, "board-heights");

    public static Dictionary<string, double> Load(string projectId, string? instanceFolder = null)
    {
        var heights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var file = FileFor(projectId, instanceFolder);
            if (!File.Exists(file)) return heights;
            foreach (var line in File.ReadAllLines(file))
            {
                var tab = line.LastIndexOf('\t');
                if (tab > 0 && double.TryParse(line[(tab + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0)
                    heights[line[..tab].Trim()] = h;
            }
        }
        catch (Exception) { }
        return heights;
    }

    public static void Save(string projectId, IReadOnlyDictionary<string, double> heights, string? instanceFolder = null)
    {
        try
        {
            var file = FileFor(projectId, instanceFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, heights.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{p.Key}\t{Math.Round(p.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        }
        catch (Exception) { }
    }
}
