using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A card: one markdown file in the board's items folder.</summary>
public sealed record Card(string Id, string Title, string Status, string Priority, string Type,
    IReadOnlyList<string> Labels, string File, string Description)
{
    public int Number => int.TryParse(Id[(Id.LastIndexOf('-') + 1)..], out var n) ? n : int.MaxValue;
    /// <summary>Its type, Task when the file leaves it blank.</summary>
    public string Kind => Type.Trim() is { Length: > 0 } t ? t : CardTypes.Task;
    public bool IsBug => Kind.Equals(CardTypes.Bug, StringComparison.OrdinalIgnoreCase);
    /// <summary>A story: done cards wrapped up into one (<see cref="Stories"/>).</summary>
    public bool IsStory => Kind.Equals(CardTypes.Story, StringComparison.OrdinalIgnoreCase);

    /// <summary>The same card as read before: every field alike, labels by their contents (a record's own == compares the lists by reference).</summary>
    public bool SameAs(Card other) => this with { Labels = other.Labels } == other && Labels.SequenceEqual(other.Labels);
}

/// <summary>Bring a shown list (an ObservableCollection a view is bound to) to a wanted one by removing, moving and
/// inserting only what differs, so items that stay keep their controls (WOR-0076: rebuilding the whole board on
/// every card change froze the window for half a second each time). Items are compared by reference.</summary>
public static class ListSync
{
    public static void Sync<T>(IList<T> shown, IReadOnlyList<T> want) where T : class
    {
        var keep = new HashSet<T>(want, ReferenceEqualityComparer.Instance);
        for (var i = shown.Count - 1; i >= 0; i--)
            if (!keep.Contains(shown[i])) shown.RemoveAt(i);
        for (var i = 0; i < want.Count; i++)
        {
            if (i < shown.Count && ReferenceEquals(shown[i], want[i])) continue;
            var at = -1;
            for (var j = i + 1; j < shown.Count; j++) if (ReferenceEquals(shown[j], want[i])) { at = j; break; }
            if (at < 0) shown.Insert(i, want[i]);
            else if (shown is System.Collections.ObjectModel.ObservableCollection<T> moving) moving.Move(at, i);
            else { var item = shown[at]; shown.RemoveAt(at); shown.Insert(i, item); }
        }
    }
}

/// <summary>The types a card can have: Task, Bug, and Story (done cards wrapped up into one, WOR-0074). A card
/// file may name another (Tatty's own types, or one typed by hand); it's kept as written.</summary>
public static class CardTypes
{
    public const string Task = "Task", Bug = "Bug", Story = "Story";
    public static readonly IReadOnlyList<string> All = [Task, Bug, Story];

    /// <summary>The type to give a card made from words someone wrote (a note, an issue): Bug when a label is
    /// bug, bugs or type: bug, or the words say something is wrong, written any of the ways people put it
    /// (<see cref="BugWords"/>); else Task. Philip's post-its said "got a bug with", "had a bug where", "fix
    /// ...", "is broken" as often as "Bug: ..." (WOR-0082). A wrong guess costs a click in the card panel.</summary>
    public static string Guess(string text, IEnumerable<string>? labels = null) =>
        BugWords.IsMatch(text)
        || (labels ?? []).Any(l => Regex.IsMatch(l.Trim(), @"^(type\s*[:/]\s*)?bugs?$", RegexOptions.IgnoreCase))
            ? Bug : Task;

    /// <summary>Words that say a bug: opening with "bug" ("Bug: ...", "[bug] ...", "Bugs in ...") or "fix"; a bug
    /// had or found ("got a bug with", "there's a bug", "the bug where"); something broken, crashing, not
    /// working, wrong, hanging or glitching, or drawn cut off, clipped or misaligned. Mentioning bugs in passing ("ticket types, task and bug") isn't one.</summary>
    static readonly Regex BugWords = new(
        @"^\W*bugs?\b|^\W*fix\s" +
        @"|\b(?:got|had|have|has|found|there'?s|there\s+is|spotted)\s+(?:a|an|another|some)?\s*bugs?\b" +
        @"|\b(?:a|the|this|that)\s+bugs?\s+(?:where|with|in|when|on|that|which)\b" +
        @"|\b(?:crash(?:es|ed|ing)?|broken|broke|wrong|regress(?:ion|ed)|glitch(?:es|ing|y)?|freez(?:es|ing)|hangs|hanging)\b" +
        @"|\b(?:cut\s+off|clipped|misaligned)\b" +
        @"|\b(?:doesn'?t|does\s+not|don'?t|do\s+not|isn'?t|is\s+not|aren'?t|not|stopped|no\s+longer)\s+work(?:s|ing)?\b",
        RegexOptions.IgnoreCase);
}

public sealed record Column(string Id, string Name);

/// <summary>A board as read from disk: its columns and the cards in each list (backlog, each column,
/// completed, and unsorted for a Status that names no list), in board.json's order.</summary>
public sealed class Board
{
    public const string Backlog = "backlog", Completed = "completed", Unsorted = "unsorted";

    public required string Name { get; init; }
    public required string Folder { get; init; }
    public string Prefix { get; init; } = "";
    public IReadOnlyList<Column> Columns { get; init; } = [];
    public IReadOnlyDictionary<string, List<Card>> Lists { get; init; } = new Dictionary<string, List<Card>>();
    /// <summary>Lists shown folded to a narrow strip (board.json's "minimized"; Tatty keeps keys it doesn't know).</summary>
    public IReadOnlySet<string> Minimized { get; init; } = new HashSet<string>();

    /// <summary>Lists in display order: backlog, the columns, completed.</summary>
    public IEnumerable<(string Id, string Name)> ListOrder()
    {
        yield return (Backlog, "Backlog");
        foreach (var c in Columns) yield return (c.Id, c.Name);
        yield return (Completed, "Completed");
        if (Lists.TryGetValue(Unsorted, out var u) && u.Count > 0) yield return (Unsorted, "Unsorted");
    }

    public IEnumerable<Card> AllCards => Lists.Values.SelectMany(l => l);
}

/// <summary>Reads and writes boards in Tatty's format, so Tatty and Obsidian see the same files:
/// &lt;folder&gt;/board.json ({columns:[{id,name}], prefix, nextId, order:{list:[ids]}}) and
/// &lt;folder&gt;/items/&lt;ID&gt; - &lt;title&gt;.md with "- **Field:** value" lines.</summary>
public static class BoardStore
{
    static readonly Regex Head = new(@"^# ([A-Z][A-Z0-9]*-\d+): (.*)$", RegexOptions.Multiline);
    static readonly Regex Field = new(@"^- \*\*(\w+):\*\*[ \t]*(.*)$", RegexOptions.Multiline);

    /// <summary>The boards in a project's boards folder: every subfolder with a board.json.</summary>
    public static IReadOnlyList<string> List(string? boardsFolder) =>
        boardsFolder is null || !Directory.Exists(boardsFolder) ? []
            : Directory.GetDirectories(boardsFolder).Where(d => File.Exists(Path.Combine(d, "board.json")))
                .Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();

    public static Board Load(string folder)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "board.json")))!.AsObject();
        var columns = (json["columns"] as JsonArray ?? [])
            .Select(c => new Column((string?)c?["id"] ?? "", (string?)c?["name"] ?? "")).Where(c => c.Id != "").ToList();
        var cards = ReadCards(folder);
        var lists = new Dictionary<string, List<Card>> { [Board.Backlog] = [], [Board.Completed] = [], [Board.Unsorted] = [] };
        foreach (var c in columns) lists[c.Id] = [];
        foreach (var card in cards) lists[PlaceOf(card.Status, columns)].Add(card);

        var order = json["order"] as JsonObject;
        foreach (var (id, list) in lists)
        {
            var wanted = (order?[id] as JsonArray)?.Select(x => (string?)x).OfType<string>().ToList() ?? [];
            int Rank(Card c) { var i = wanted.IndexOf(c.Id); return i < 0 ? int.MaxValue : i; }
            list.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : a.Number.CompareTo(b.Number));
        }
        return new Board
        {
            Name = Path.GetFileName(folder), Folder = folder, Prefix = (string?)json["prefix"] ?? "",
            Columns = columns, Lists = lists,
            Minimized = (json["minimized"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>().ToHashSet(),
        };
    }

    static JsonObject ReadJson(string folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "board.json")))!.AsObject();
    static void WriteJson(string folder, JsonObject json) =>
        File.WriteAllText(Path.Combine(folder, "board.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    static JsonArray ColumnsOf(JsonObject json) => json["columns"] as JsonArray ?? (JsonArray)(json["columns"] = new JsonArray());

    /// <summary>A usable column name, as Tatty checks it: one line, at most 40 characters, not a list of
    /// its own (Backlog, Completed) and not another column's name. Throws with why not.</summary>
    static string ColumnName(JsonObject json, string name, string keepId = "")
    {
        name = Regex.Replace(name, @"\s+", " ").Trim();
        if (name.Length > 40) name = name[..40].TrimEnd();
        if (name.Length == 0) throw new ArgumentException("A column needs a name.");
        if (name.ToLowerInvariant() is "backlog" or "completed" or "not on a column")
            throw new ArgumentException($"{name} can't be a column's name: it's a list of its own.");
        if (ColumnsOf(json).Any(c => ((string?)c?["name"] ?? "").Equals(name, StringComparison.OrdinalIgnoreCase) && (string?)c?["id"] != keepId))
            throw new ArgumentException($"There's a {name} column already.");
        return name;
    }

    /// <summary>Add a column (at the end, or at index among the columns), with the next free cN id as Tatty
    /// names them. Returns its id.</summary>
    public static string AddColumn(string folder, string name, int? index = null)
    {
        var json = ReadJson(folder);
        name = ColumnName(json, name);
        var cols = ColumnsOf(json);
        var n = 1 + cols.Select(c => (string?)c?["id"] ?? "").Select(id => Regex.Match(id, @"^c(\d+)$"))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
        var id = $"c{n}";
        cols.Insert(Math.Clamp(index ?? cols.Count, 0, cols.Count), new JsonObject { ["id"] = id, ["name"] = name });
        WriteJson(folder, json);
        return id;
    }

    /// <summary>Rename a column: board.json, and the Status line of every card in it (a card's Status is
    /// its column's name, so they would fall out of it otherwise).</summary>
    public static void RenameColumn(string folder, string columnId, string name)
    {
        var json = ReadJson(folder);
        var col = ColumnsOf(json).OfType<JsonObject>().FirstOrDefault(c => (string?)c["id"] == columnId)
                  ?? throw new ArgumentException("There's no such column.", nameof(columnId));
        name = ColumnName(json, name, columnId);
        var cards = Load(folder).Lists.GetValueOrDefault(columnId) ?? [];
        col["name"] = name;
        WriteJson(folder, json);
        foreach (var card in cards) RewriteFields(folder, card, t => SetField(t, "Status", name));
    }

    /// <summary>Remove a column. Its cards go to the end of the backlog, as Tatty does; a board keeps at
    /// least one column. Returns how many cards moved.</summary>
    public static int RemoveColumn(string folder, string columnId)
    {
        var json = ReadJson(folder);
        var cols = ColumnsOf(json);
        var col = cols.OfType<JsonObject>().FirstOrDefault(c => (string?)c["id"] == columnId)
                  ?? throw new ArgumentException("There's no such column.", nameof(columnId));
        if (cols.Count == 1) throw new ArgumentException("A board needs at least one column.");
        var cards = Load(folder).Lists.GetValueOrDefault(columnId) ?? [];
        cols.Remove(col);
        var order = json["order"] as JsonObject ?? new JsonObject();
        json["order"] = order;
        if (order[Board.Backlog] is not JsonArray backlog) order[Board.Backlog] = backlog = new JsonArray();
        foreach (var card in cards)
            if (!backlog.Any(x => (string?)x == card.Id)) backlog.Add(card.Id);
        order.Remove(columnId);
        if (json["minimized"] is JsonArray min) foreach (var x in min.Where(x => (string?)x == columnId).ToList()) min.Remove(x);
        WriteJson(folder, json);
        foreach (var card in cards) RewriteFields(folder, card, t => SetField(t, "Status", "Backlog"));
        return cards.Count;
    }

    /// <summary>Fold a list (backlog, a column, completed) to a narrow strip, or open it again. Kept in
    /// board.json, so it stays that way next time and on every machine the board syncs to.</summary>
    public static void SetMinimized(string folder, string listId, bool minimized)
    {
        var json = ReadJson(folder);
        var ids = (json["minimized"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>().Where(x => x != listId).ToList();
        if (minimized) ids.Add(listId);
        if (ids.Count == 0) json.Remove("minimized");
        else json["minimized"] = new JsonArray(ids.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        WriteJson(folder, json);
    }

    static void RewriteFields(string folder, Card card, Func<string, string> change)
    {
        var file = Path.Combine(folder, "items", card.File);
        var raw = File.ReadAllText(file);
        var crlf = raw.Contains("\r\n");
        var text = change(raw.Replace("\r\n", "\n"));
        File.WriteAllText(file, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
    }

    /// <summary>Which list a Status puts a card in, as Tatty decides it.</summary>
    public static string PlaceOf(string status, IReadOnlyList<Column> columns)
    {
        var s = Regex.Replace(status.Trim(), @"\s+", " ");
        if (s.Length == 0 || s.Equals("backlog", StringComparison.OrdinalIgnoreCase)) return Board.Backlog;
        if (s.Equals("completed", StringComparison.OrdinalIgnoreCase)) return Board.Completed;
        return columns.FirstOrDefault(c => c.Name.Equals(s, StringComparison.OrdinalIgnoreCase))?.Id ?? Board.Unsorted;
    }

    public static List<Card> ReadCards(string folder)
    {
        var items = Path.Combine(folder, "items");
        if (!Directory.Exists(items)) return [];
        var cards = new List<Card>();
        foreach (var file in Directory.GetFiles(items, "*.md"))
        {
            var card = Parse(File.ReadAllText(file), Path.GetFileName(file));
            if (card is not null) cards.Add(card);
        }
        return cards;
    }

    public static Card? Parse(string text, string file)
    {
        text = text.Replace("\r\n", "\n");
        var head = Head.Match(text);
        if (!head.Success) return null;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Field.Matches(text)) fields.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Trim());
        var labels = fields.GetValueOrDefault("Labels", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var desc = "";
        var d = text.IndexOf("\n## Description\n", StringComparison.Ordinal);
        if (d >= 0)
        {
            var rest = text[(d + "\n## Description\n".Length)..];
            var next = rest.IndexOf("\n## ", StringComparison.Ordinal);
            desc = (next >= 0 ? rest[..next] : rest).Trim();
        }
        return new Card(head.Groups[1].Value, head.Groups[2].Value.Trim(), fields.GetValueOrDefault("Status", ""),
            fields.GetValueOrDefault("Priority", ""), fields.GetValueOrDefault("Type", ""), labels, file, desc);
    }

    /// <summary>Move a card to a list (backlog, a column id or completed), before another card or at the
    /// end: sets its Status line (and Done date, going to or from completed) and its place in board.json.</summary>
    public static void Move(string folder, string cardId, string toList, string? beforeId = null)
    {
        var boardPath = Path.Combine(folder, "board.json");
        var json = JsonNode.Parse(File.ReadAllText(boardPath))!.AsObject();
        var columns = (json["columns"] as JsonArray ?? [])
            .Select(c => new Column((string?)c?["id"] ?? "", (string?)c?["name"] ?? "")).ToList();
        string status = toList switch
        {
            Board.Backlog => "Backlog",
            Board.Completed => "Completed",
            _ => columns.FirstOrDefault(c => c.Id == toList)?.Name
                 ?? throw new ArgumentException($"No list {toList} on this board.", nameof(toList)),
        };
        var card = ReadCards(folder).FirstOrDefault(c => c.Id == cardId)
                   ?? throw new ArgumentException($"No card {cardId} on this board.", nameof(cardId));

        var file = Path.Combine(folder, "items", card.File);
        var raw = File.ReadAllText(file);
        var crlf = raw.Contains("\r\n");
        var text = raw.Replace("\r\n", "\n");
        text = SetField(text, "Status", status);
        if (toList == Board.Completed) text = SetField(text, "Done", DateTime.Now.ToString("yyyy-MM-dd"));
        else if (card.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)) text = SetField(text, "Done", "");
        File.WriteAllText(file, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));

        var order = json["order"] as JsonObject ?? new JsonObject();
        json["order"] = order;
        foreach (var (_, ids) in order)
            if (ids is JsonArray a)
                foreach (var n in a.Where(x => (string?)x == cardId).ToList()) a.Remove(n);
        if (order[toList] is not JsonArray target) order[toList] = target = new JsonArray();
        var at = beforeId is null ? -1 : target.Select(x => (string?)x).ToList().IndexOf(beforeId);
        if (at < 0) target.Add(cardId); else target.Insert(at, cardId);
        File.WriteAllText(boardPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    /// <summary>Add a card to the backlog: the next ID (past board.json's count and every ID in use), a
    /// file named as Tatty names them, and its place at the end of the backlog. Returns the card.</summary>
    public static Card Create(string folder, string title, string description = "", IEnumerable<string>? labels = null,
        string priority = "", string type = "Task")
    {
        title = Regex.Replace(title.Replace('\n', ' ').Replace('\r', ' '), @"\s+", " ").Trim();
        if (title.Length == 0) throw new ArgumentException("A card needs a title.", nameof(title));
        var boardPath = Path.Combine(folder, "board.json");
        var json = JsonNode.Parse(File.ReadAllText(boardPath))!.AsObject();
        var prefix = (string?)json["prefix"] is { Length: > 0 } p ? p : "T";
        var next = Math.Max((int?)json["nextId"] ?? 1, HighestUsed(folder, prefix) + 1);
        var id = $"{prefix}-{next:0000}";
        var labelText = string.Join(", ", (labels ?? []).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        var text = $"# {id}: {title}\n\n- **Status:** Backlog\n- **Priority:**{(priority.Length > 0 ? " " + priority : "")}\n" +
                   $"- **Type:** {type}\n- **Labels:**{(labelText.Length > 0 ? " " + labelText : "")}\n" +
                   $"- **Created:** {DateTime.Now:yyyy-MM-dd}\n- **Done:**\n\n## Description\n\n{description.Trim()}\n";
        var items = Path.Combine(folder, "items");
        Directory.CreateDirectory(items);
        var file = $"{id} - {FileTitle(title)}.md";
        File.WriteAllText(Path.Combine(items, file), text, new UTF8Encoding(false));

        json["nextId"] = next + 1;
        var order = json["order"] as JsonObject ?? new JsonObject();
        json["order"] = order;
        if (order[Board.Backlog] is not JsonArray backlog) order[Board.Backlog] = backlog = new JsonArray();
        backlog.Add(id);
        File.WriteAllText(boardPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        return Parse(text, file)!;
    }

    /// <summary>The highest number in use for a prefix, on this board and on every board beside it with the
    /// same prefix (their cards and their nextId). Boards in a project share a prefix, so an ID stays unique
    /// across them and a card keeps its ID when it moves to another board.</summary>
    static int HighestUsed(string folder, string prefix)
    {
        var high = 0;
        var parent = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('\\', '/'));
        var boards = parent is null ? [folder] : Directory.GetDirectories(parent).Where(d => File.Exists(Path.Combine(d, "board.json")))
            .Append(folder).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var b in boards)
        {
            JsonObject? json;
            try { json = JsonNode.Parse(File.ReadAllText(Path.Combine(b, "board.json")))?.AsObject(); }
            catch (Exception) { continue; }
            if (json is null || !string.Equals((string?)json["prefix"] is { Length: > 0 } p ? p : "T", prefix, StringComparison.Ordinal)) continue;
            high = Math.Max(high, ((int?)json["nextId"] ?? 1) - 1);
            foreach (var c in ReadCards(b))
                if (c.Id.StartsWith(prefix + "-", StringComparison.Ordinal) && c.Number != int.MaxValue) high = Math.Max(high, c.Number);
        }
        return high;
    }

    public static readonly IReadOnlyList<string> DefaultColumns = ["To Do", "In Progress", "Done"];

    /// <summary>Make a new, empty board in a project's boards folder, in Tatty's format. It takes the prefix
    /// of the boards already there (so IDs stay unique across them; "T" if there are none) unless given one,
    /// and the columns given (To Do, In Progress, Done by default). Returns its folder.</summary>
    public static string CreateBoard(string boardsFolder, string name, IEnumerable<string>? columns = null, string? prefix = null)
    {
        name = Regex.Replace(name.Replace('\n', ' ').Replace('\r', ' '), @"\s+", " ").Trim();
        if (name.Length == 0) throw new ArgumentException("A board needs a name.", nameof(name));
        if (FileTitle(name) != name || name.StartsWith('.'))
            throw new ArgumentException("A board's name is its folder's name: no / \\ : * ? \" < > |, and not starting with a dot.", nameof(name));
        Directory.CreateDirectory(boardsFolder);
        var folder = Path.Combine(boardsFolder, name);
        if (Directory.Exists(folder)) throw new ArgumentException($"There is already a board or folder called {name}.", nameof(name));
        var cols = (columns ?? DefaultColumns).Select(c => Regex.Replace(c, @"\s+", " ").Trim()).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cols.Any(c => c.Equals("Backlog", StringComparison.OrdinalIgnoreCase) || c.Equals("Completed", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Backlog and Completed are always there; they can't be columns too.", nameof(columns));
        prefix = prefix?.Trim().ToUpperInvariant() is { Length: > 0 } given ? given
            : List(boardsFolder).Select(b => { try { return (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(boardsFolder, b, "board.json")))?["prefix"]; } catch (Exception) { return null; } })
                .FirstOrDefault(p => p is { Length: > 0 }) ?? "T";
        if (!Regex.IsMatch(prefix, "^[A-Z][A-Z0-9]*$")) throw new ArgumentException("A prefix is letters and digits, starting with a letter.", nameof(prefix));
        Directory.CreateDirectory(Path.Combine(folder, "items"));
        var json = new JsonObject
        {
            ["columns"] = new JsonArray(cols.Select((c, i) => (JsonNode)new JsonObject { ["id"] = $"c{i + 1}", ["name"] = c }).ToArray()),
            ["prefix"] = prefix,
            ["version"] = 1,
            ["nextId"] = HighestUsed(folder, prefix) + 1,
            ["order"] = new JsonObject { [Board.Backlog] = new JsonArray() },
        };
        File.WriteAllText(Path.Combine(folder, "board.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        return folder;
    }

    /// <summary>Move a card to another board, keeping its ID and file: it leaves this board's order and joins
    /// the other's, in the column of the same name if there is one, else the backlog (Completed stays
    /// Completed). The other board's nextId moves past it if they share a prefix. Returns the card as moved.</summary>
    public static Card Transfer(string fromFolder, string toFolder, string cardId)
    {
        if (string.Equals(Path.GetFullPath(fromFolder).TrimEnd('\\', '/'), Path.GetFullPath(toFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("That is the board it's on.", nameof(toFolder));
        var card = ReadCards(fromFolder).FirstOrDefault(c => c.Id == cardId)
                   ?? throw new ArgumentException($"No card {cardId} on this board.", nameof(cardId));
        if (ReadCards(toFolder).Any(c => c.Id == cardId))
            throw new ArgumentException($"The other board already has a card {cardId}.", nameof(cardId));
        var toPath = Path.Combine(toFolder, "board.json");
        var to = JsonNode.Parse(File.ReadAllText(toPath))!.AsObject();
        var columns = (to["columns"] as JsonArray ?? [])
            .Select(c => new Column((string?)c?["id"] ?? "", (string?)c?["name"] ?? "")).Where(c => c.Id != "").ToList();
        var place = PlaceOf(card.Status, columns);
        if (place == Board.Unsorted) place = Board.Backlog;

        var items = Path.Combine(toFolder, "items");
        Directory.CreateDirectory(items);
        var target = Path.Combine(items, card.File);
        if (File.Exists(target)) throw new ArgumentException($"The other board already has a file {card.File}.", nameof(cardId));
        File.Move(Path.Combine(fromFolder, "items", card.File), target);
        if (place == Board.Backlog && !card.Status.Equals("backlog", StringComparison.OrdinalIgnoreCase))
        {
            var raw = File.ReadAllText(target);
            var crlf = raw.Contains("\r\n");
            var text = SetField(raw.Replace("\r\n", "\n"), "Status", "Backlog");
            File.WriteAllText(target, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
        }

        var fromPath = Path.Combine(fromFolder, "board.json");
        var from = JsonNode.Parse(File.ReadAllText(fromPath))!.AsObject();
        if (from["order"] is JsonObject fromOrder)
            foreach (var (_, ids) in fromOrder)
                if (ids is JsonArray a)
                    foreach (var n in a.Where(x => (string?)x == cardId).ToList()) a.Remove(n);
        File.WriteAllText(fromPath, from.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));

        var order = to["order"] as JsonObject ?? new JsonObject();
        to["order"] = order;
        if (order[place] is not JsonArray list) order[place] = list = new JsonArray();
        list.Add(cardId);
        var prefix = (string?)to["prefix"] is { Length: > 0 } p ? p : "T";
        if (cardId.StartsWith(prefix + "-", StringComparison.Ordinal) && card.Number != int.MaxValue && ((int?)to["nextId"] ?? 1) <= card.Number)
            to["nextId"] = card.Number + 1;
        File.WriteAllText(toPath, to.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        return ReadCards(toFolder).First(c => c.Id == cardId);
    }

    /// <summary>Delete a card the way Tatty does: its file moves into the board's .deleted folder (out of
    /// the way, never gone; Obsidian ignores dot folders) and it leaves board.json's order.</summary>
    public static void Delete(string folder, string cardId)
    {
        var card = ReadCards(folder).FirstOrDefault(c => c.Id == cardId)
                   ?? throw new ArgumentException($"No card {cardId} on this board.", nameof(cardId));
        var bin = Path.Combine(folder, ".deleted");
        Directory.CreateDirectory(bin);
        File.Move(Path.Combine(folder, "items", card.File), Path.Combine(bin, card.File), overwrite: true);
        var boardPath = Path.Combine(folder, "board.json");
        var json = JsonNode.Parse(File.ReadAllText(boardPath))!.AsObject();
        if (json["order"] is JsonObject order)
            foreach (var (_, ids) in order)
                if (ids is JsonArray a)
                    foreach (var n in a.Where(x => (string?)x == cardId).ToList()) a.Remove(n);
        File.WriteAllText(boardPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    /// <summary>Change a card's title, description, labels, priority or type (null leaves one as it is). The
    /// rest of the file, sections Darg doesn't edit included, stays as written. A new title renames
    /// the file to match, as Tatty does. Returns the card as saved.</summary>
    public static Card Update(string folder, string cardId, string? title = null, string? description = null,
        IEnumerable<string>? labels = null, string? priority = null, string? type = null)
    {
        var card = ReadCards(folder).FirstOrDefault(c => c.Id == cardId)
                   ?? throw new ArgumentException($"No card {cardId} on this board.", nameof(cardId));
        var path = Path.Combine(folder, "items", card.File);
        var raw = File.ReadAllText(path);
        var crlf = raw.Contains("\r\n");
        var text = raw.Replace("\r\n", "\n");
        if (title is not null)
        {
            title = Regex.Replace(title.Replace('\n', ' '), @"\s+", " ").Trim();
            if (title.Length == 0) throw new ArgumentException("A card needs a title.", nameof(title));
            text = new Regex($@"^# {Regex.Escape(cardId)}: .*$", RegexOptions.Multiline).Replace(text, $"# {cardId}: {title}", 1);
        }
        if (labels is not null)
            text = SetField(text, "Labels", string.Join(", ", labels.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)));
        if (priority is not null) text = SetField(text, "Priority", priority.Trim());
        if (type is not null)
        {
            var t = type.Trim() is { Length: > 0 } given ? given : CardTypes.Task;
            if (Regex.IsMatch(text, @"^- \*\*Type:\*\*", RegexOptions.Multiline)) text = SetField(text, "Type", t);
            else // a card written without a Type line gets one after Priority (or Status)
            {
                var after = new Regex(@"^- \*\*(Priority|Status):\*\*[^\n]*$", RegexOptions.Multiline).Matches(text).LastOrDefault();
                if (after is not null) text = text.Insert(after.Index + after.Length, $"\n- **Type:** {t}");
            }
        }
        if (description is not null)
        {
            const string head = "\n## Description\n";
            var d = text.IndexOf(head, StringComparison.Ordinal);
            if (d < 0) text = text.TrimEnd('\n') + "\n" + head + "\n" + description.Trim() + "\n";
            else
            {
                var start = d + head.Length;
                var next = text.IndexOf("\n## ", start, StringComparison.Ordinal);
                var tail = next >= 0 ? text[next..] : "\n";
                text = text[..start] + "\n" + description.Trim() + "\n" + tail;
            }
        }
        File.WriteAllText(path, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
        var file = card.File;
        if (title is not null && title != card.Title)
        {
            var wanted = $"{cardId} - {FileTitle(title)}.md";
            var to = Path.Combine(folder, "items", wanted);
            if (wanted != card.File && !File.Exists(to)) { File.Move(path, to); file = wanted; }
        }
        return Parse(text, file)!;
    }

    /// <summary>A title as it goes in a file name, as Tatty does it: slashes become spaces, characters a
    /// file name can't hold go, and it's cut to 60 characters.</summary>
    public static string FileTitle(string title)
    {
        var s = title.Replace('/', ' ').Replace('\\', ' ');
        s = Regex.Replace(s, @"[<>:""|?*\x00-\x1f]", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length > 60) s = s[..60];
        return s.TrimEnd(' ', '.');
    }

    static string SetField(string text, string name, string value)
    {
        var line = new Regex($@"^- \*\*{Regex.Escape(name)}:\*\*[^\n]*$", RegexOptions.Multiline);
        var repl = value.Length > 0 ? $"- **{name}:** {value}" : $"- **{name}:**";
        return line.IsMatch(text) ? line.Replace(text, repl, 1) : text;
    }
}
