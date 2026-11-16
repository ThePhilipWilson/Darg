using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Workbench.Core;

/// <summary>A card: one markdown file in the board's items folder.</summary>
public sealed record Card(string Id, string Title, string Status, string Priority, string Type,
    IReadOnlyList<string> Labels, string File, string Description)
{
    public int Number => int.TryParse(Id[(Id.LastIndexOf('-') + 1)..], out var n) ? n : int.MaxValue;
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
        };
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
        var used = ReadCards(folder).Select(c => c.Number).Where(n => n != int.MaxValue).DefaultIfEmpty(0).Max();
        var next = Math.Max((int?)json["nextId"] ?? 1, used + 1);
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
