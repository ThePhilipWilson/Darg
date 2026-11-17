using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A post-it: one markdown file, its fields as Obsidian-style properties at the top.</summary>
public sealed record Note(string Id, string Text, string Column, string Colour, IReadOnlyList<string> Tags,
    string Created, string Ticket, string Image = "")
{
    /// <summary>The first line, for a ticket's title.</summary>
    public string FirstLine => Text.Split('\n', 2)[0].Trim();
}

/// <summary>A project's notes wall: &lt;folder&gt;/&lt;id&gt;.md per note, &lt;folder&gt;/wall.json for the
/// column names. Deleting moves a note to &lt;folder&gt;/.deleted (a dot folder Obsidian ignores).</summary>
public static class NoteStore
{
    public static readonly string[] DefaultColumns = ["Loose", "Thinking about", "Parked"];
    public static readonly string[] Colours = ["yellow", "pink", "blue", "green", "orange"];

    public const double DefaultWidth = 250, MinWidth = 160, MaxWidth = 560;

    /// <summary>How wide a wall column (and so its post-its) is, from wall.json's "width"; the default when absent or silly.</summary>
    public static double ColumnWidth(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "wall.json");
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path))?["width"] is JsonValue v && v.TryGetValue<double>(out var w))
                return Math.Clamp(w, MinWidth, MaxWidth);
        }
        catch (Exception e) when (e is JsonException or IOException or FormatException or InvalidOperationException) { }
        return DefaultWidth;
    }

    /// <summary>Write the column width to wall.json, keeping everything else in it.</summary>
    public static void SetColumnWidth(string folder, double width)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "wall.json");
        JsonObject json;
        try { json = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new(); }
        catch (JsonException) { json = new(); }
        json["width"] = Math.Round(Math.Clamp(width, MinWidth, MaxWidth));
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    public static IReadOnlyList<string> Columns(string folder)
    {
        var path = Path.Combine(folder, "wall.json");
        if (!File.Exists(path)) return DefaultColumns;
        var cols = (JsonNode.Parse(File.ReadAllText(path))?["columns"] as JsonArray)?.Select(c => (string?)c).OfType<string>().ToList();
        return cols is { Count: > 0 } ? cols : DefaultColumns;
    }

    /// <summary>Write the wall's columns to wall.json, keeping anything else in it. Names are checked:
    /// not empty, no repeats, never Ticketed or Archive (those follow the tickets), at least one.</summary>
    public static void SetColumns(string folder, IReadOnlyList<string> columns)
    {
        var names = columns.Select(CleanName).ToList();
        if (names.Count == 0) throw new InvalidOperationException("The wall needs at least one column.");
        for (var i = 0; i < names.Count; i++)
        {
            CheckName(names[i]);
            if (names.Take(i).Contains(names[i], StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"There's already a column called {names[i]}.");
        }
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "wall.json");
        JsonObject json;
        try { json = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new(); }
        catch (JsonException) { json = new(); }
        json["columns"] = new JsonArray(names.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    static string CleanName(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\s+", " ").Trim();

    static void CheckName(string name)
    {
        if (name.Length == 0) throw new InvalidOperationException("A column needs a name.");
        if (name.Equals(Ticketed, StringComparison.OrdinalIgnoreCase) || name.Equals("Archive", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{name} is kept for notes with tickets; pick another name.");
    }

    /// <summary>Add a column (at the end unless <paramref name="at"/> says where), named "New column" or the
    /// next free "New column n" when no name is given. Returns its name.</summary>
    public static string AddColumn(string folder, string? name = null, int? at = null)
    {
        var cols = Columns(folder).ToList();
        if (name is null)
        {
            name = "New column";
            for (var n = 2; cols.Contains(name, StringComparer.OrdinalIgnoreCase); n++) name = $"New column {n}";
        }
        name = CleanName(name);
        cols.Insert(Math.Clamp(at ?? cols.Count, 0, cols.Count), name);
        SetColumns(folder, cols);
        return name;
    }

    /// <summary>Rename a column, and every note in it (archived ones too, so they come back to the right
    /// place). A column only notes name, not on the wall yet, joins the wall under its new name.</summary>
    public static void RenameColumn(string folder, string from, string to)
    {
        to = CleanName(to);
        CheckName(to);
        var cols = Columns(folder).ToList();
        var i = cols.FindIndex(c => c.Equals(from, StringComparison.OrdinalIgnoreCase));
        if (cols.Where((c, j) => j != i).Contains(to, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"There's already a column called {to}.");
        if (i >= 0) cols[i] = to; else cols.Add(to);
        SetColumns(folder, cols);
        MoveNotes(folder, from, to);
    }

    /// <summary>Remove a column; its notes go to the first column left. Returns how many moved.</summary>
    public static int RemoveColumn(string folder, string name)
    {
        var cols = Columns(folder).Where(c => !c.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (cols.Count == 0) throw new InvalidOperationException("The wall needs at least one column.");
        SetColumns(folder, cols);
        return MoveNotes(folder, name, cols[0]);
    }

    /// <summary>Move a column one place left (-1) or right (+1).</summary>
    public static void ShiftColumn(string folder, string name, int by)
    {
        var cols = Columns(folder).ToList();
        var i = cols.FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        var j = i + by;
        if (i < 0 || j < 0 || j >= cols.Count) return;
        (cols[i], cols[j]) = (cols[j], cols[i]);
        SetColumns(folder, cols);
    }

    static int MoveNotes(string folder, string from, string to)
    {
        var moved = 0;
        foreach (var (dir, notes) in new[] { (folder, List(folder)), (ArchiveFolder(folder), Archived(folder)) })
            foreach (var n in notes.Where(n => n.Column.Equals(from, StringComparison.OrdinalIgnoreCase)))
            {
                Save(dir, n with { Column = to });
                if (dir == folder && n.Ticket.Length == 0) moved++;
            }
        return moved;
    }

    public static List<Note> List(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var notes = new List<Note>();
        foreach (var f in Directory.GetFiles(folder, "*.md"))
            if (Parse(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f)) is { } n) notes.Add(n);
        return notes.OrderByDescending(n => n.Created, StringComparer.Ordinal).ThenByDescending(n => n.Id, StringComparer.Ordinal).ToList();
    }

    public static Note? Parse(string text, string id)
    {
        text = text.Replace("\r\n", "\n");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = text;
        if (text.StartsWith("---\n"))
        {
            var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end > 0)
            {
                foreach (var line in text[4..end].Split('\n'))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0) fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }
                body = text[(end + 4)..].TrimStart('\n');
            }
        }
        var tags = fields.GetValueOrDefault("tags", "").Trim('[', ']')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).ToList();
        return new Note(id, body.TrimEnd(), fields.GetValueOrDefault("column", DefaultColumns[0]),
            fields.GetValueOrDefault("colour", "yellow"), tags, fields.GetValueOrDefault("created", ""), fields.GetValueOrDefault("ticket", ""), fields.GetValueOrDefault("image", ""));
    }

    public static string Render(Note n)
    {
        var sb = new StringBuilder("---\n");
        sb.Append($"column: {n.Column}\ncolour: {n.Colour}\n");
        if (n.Tags.Count > 0) sb.Append($"tags: [{string.Join(", ", n.Tags)}]\n");
        sb.Append($"created: {n.Created}\n");
        if (n.Ticket.Length > 0) sb.Append($"ticket: {n.Ticket}\n");
        if (n.Image.Length > 0) sb.Append($"image: {n.Image}\n");
        sb.Append("---\n").Append(n.Text.Trim()).Append('\n');
        return sb.ToString();
    }

    /// <summary>A new note's id: when it was made, plus a few random letters so two at once don't clash.</summary>
    public static string NewId() => $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";

    public static Note Save(string folder, Note note)
    {
        Directory.CreateDirectory(folder);
        if (note.Created.Length == 0) note = note with { Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        File.WriteAllText(Path.Combine(folder, note.Id + ".md"), Render(note), new UTF8Encoding(false));
        return note;
    }

    public static void Delete(string folder, string id)
    {
        var from = Path.Combine(folder, id + ".md");
        if (!File.Exists(from)) return;
        var bin = Path.Combine(folder, ".deleted");
        Directory.CreateDirectory(bin);
        File.Move(from, Path.Combine(bin, id + ".md"), overwrite: true);
    }

    /// <summary>The longest title a card made from a note gets; past it the title is the note's first sentence,
    /// or its first words and an ellipsis.</summary>
    public const int TitleMax = 70;
    /// <summary>A first sentence this long still makes a whole title, rather than being cut.</summary>
    const int SentenceMax = 90;
    static readonly Regex SentenceEnd = new(@"(?<!\b(?:e\.g|i\.e|etc|vs|eg|ie))([.?!])\s+(?=\S)", RegexOptions.IgnoreCase);

    /// <summary>A note's words split into a card's title and the start of its description, losing none of them
    /// (WOR-0082: a long first line used to be cut at 70 characters and the rest thrown away). A short first
    /// line is the title and the other lines the body. A long one gives its first sentence as the title when
    /// that is short enough (of two words or more, not ending in an abbreviation) and the rest of the line
    /// leads the body; with no such sentence the title is its first words and "…", and the body has the
    /// whole line.</summary>
    public static (string Title, string Body) TicketText(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        var cut = text.IndexOf('\n');
        var first = (cut < 0 ? text : text[..cut]).Trim();
        var rest = cut < 0 ? "" : text[(cut + 1)..].Trim();
        if (first.Length <= TitleMax) return (first, rest);
        static string Join(string a, string b) => b.Length == 0 ? a : a + "\n\n" + b;
        foreach (Match m in SentenceEnd.Matches(first))
        {
            var sentence = first[..m.Index].Trim();
            if (sentence.Length > SentenceMax) break;
            if (sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2) continue;
            var title = m.Groups[1].Value == "." ? sentence : sentence + m.Groups[1].Value;
            return (title, Join(first[(m.Index + m.Length)..].Trim(), rest));
        }
        var space = first.LastIndexOf(' ', TitleMax);
        var words = (space > TitleMax / 2 ? first[..space] : first[..TitleMax]).TrimEnd(' ', ',', ';', ':', '-');
        return (words + "…", Join(first, rest));
    }

    /// <summary>Turn a note into a card on a board's backlog (title and description from
    /// <see cref="TicketText"/>, tags as labels, type from <see cref="CardTypes.Guess"/>) and mark the note
    /// with the card's ID.</summary>
    public static (Note Note, Card Card) MakeTicket(string folder, Note note, string boardFolder)
    {
        var (title, rest) = TicketText(note.Text);
        var desc = (rest.Length > 0 ? rest + "\n\n" : "") + $"From a note on the wall ({note.Id}).";
        if (note.Image.Length > 0 && Pictures.Resolve(folder, note.Image) is { } pic && File.Exists(pic))
            desc += $"\n\n![]({Pictures.Add(boardFolder, pic)})";
        var card = BoardStore.Create(boardFolder, title, desc, note.Tags, type: CardTypes.Guess(note.Text, note.Tags));
        return (Save(folder, note with { Ticket = card.Id }), card);
    }

    /// <summary>The column a note with a ticket shows in, whatever column its file names.</summary>
    public const string Ticketed = "Ticketed";

    /// <summary>Where notes go once their ticket is completed: &lt;folder&gt;/archive.</summary>
    public static string ArchiveFolder(string folder) => Path.Combine(folder, "archive");

    public static List<Note> Archived(string folder) => List(ArchiveFolder(folder));

    /// <summary>Every card's Status across a project's boards (each subfolder with a board.json), by card ID.</summary>
    public static Dictionary<string, string> TicketStatuses(string? boardsFolder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in BoardStore.List(boardsFolder))
            foreach (var card in BoardStore.ReadCards(Path.Combine(boardsFolder!, board)))
                map.TryAdd(card.Id, card.Status);
        return map;
    }

    /// <summary>Finished: the board's Completed list, or a column called Done (boards that end in their own
    /// Done column, as Darg's does, never use Completed).</summary>
    public static bool IsCompleted(string status) =>
        BoardStore.PlaceOf(status, []) == Board.Completed || status.Trim().Equals("Done", StringComparison.OrdinalIgnoreCase);

    /// <summary>Keep the archive in step with the tickets: a note whose ticket is completed moves to the
    /// archive, an archived note whose ticket is open again comes back. A ticket that can't be found leaves
    /// its note where it is. Returns how many went each way.</summary>
    public static (int Archived, int Restored) SyncArchive(string folder, IReadOnlyDictionary<string, string> statuses)
    {
        int archived = 0, restored = 0;
        var archive = ArchiveFolder(folder);
        foreach (var n in List(folder))
            if (n.Ticket.Length > 0 && statuses.TryGetValue(n.Ticket, out var s) && IsCompleted(s))
            {
                Directory.CreateDirectory(archive);
                File.Move(Path.Combine(folder, n.Id + ".md"), Path.Combine(archive, n.Id + ".md"), overwrite: true);
                archived++;
            }
        foreach (var n in List(archive))
            if (n.Ticket.Length > 0 && statuses.TryGetValue(n.Ticket, out var s) && !IsCompleted(s))
            {
                File.Move(Path.Combine(archive, n.Id + ".md"), Path.Combine(folder, n.Id + ".md"), overwrite: true);
                restored++;
            }
        return (archived, restored);
    }

    /// <summary>Turn several notes into cards at once, oldest note first so the card IDs follow the order
    /// the notes were written. Notes that already have a ticket, or have no text, are left alone.</summary>
    public static List<Card> MakeTickets(string folder, IEnumerable<Note> notes, string boardFolder) =>
        notes.Where(n => n.Ticket.Length == 0 && n.Text.Trim().Length > 0)
            .OrderBy(n => n.Created, StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => MakeTicket(folder, n, boardFolder).Card).ToList();

    /// <summary>What Fettle is told when Organise is pressed: sort these notes (the loose ones showing, no
    /// tickets) into the wall's columns, give them tags and colours, and say which look like duplicates.
    /// Null when there's nothing to organise.</summary>
    public static string? OrganisePrompt(string folder, IReadOnlyList<Note> notes, IReadOnlyList<string> columns)
    {
        var mine = notes.Where(n => n.Ticket.Length == 0).ToList();
        if (mine.Count == 0) return null;
        var tags = List(folder).SelectMany(n => n.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Organise the notes wall: {mine.Count} post-it{(mine.Count == 1 ? "" : "s")} in {folder}. Philip pressed Organise, so you may change all of these files without asking; touch no other note.");
        sb.AppendLine();
        sb.AppendLine($"Columns on the wall, in order: {string.Join(", ", columns.Select(c => $"\"{c}\""))}. The first is where new notes land.");
        sb.AppendLine(tags.Count > 0 ? $"Tags already in use: {string.Join(", ", tags)}." : "No tags are in use yet.");
        sb.AppendLine($"Colours: {string.Join(", ", Colours)}.");
        sb.AppendLine();
        sb.AppendLine("For each note, edit only its properties (column, colour, tags), never its text:");
        sb.AppendLine("- column: the one that fits what the note is. Leave it where it is if nothing fits better. If several notes clearly form a group no column fits, you may add up to two columns to wall.json ({\"columns\": [every column, in order]}); keep the existing ones and their order, and never name one Ticketed or Archive.");
        sb.AppendLine("- tags: one to three short lowercase tags, reusing the ones in use where they fit. Keep tags a note already has unless they're plainly wrong.");
        sb.AppendLine("- colour: one colour per theme, the same theme the same colour across the wall. A note that isn't yellow was coloured by Philip: keep it, and let it hint at what that colour means.");
        sb.AppendLine("Don't delete, merge or make tickets. If two notes look like the same thought, say which in your reply.");
        sb.AppendLine();
        sb.AppendLine("The notes:");
        foreach (var n in mine)
        {
            var text = n.Text.Replace("\r", "").Replace("\n", " / ");
            if (text.Length > 300) text = text[..300] + "…";
            sb.AppendLine($"- {n.Id}.md [{n.Column}; {n.Colour}{(n.Tags.Count > 0 ? "; " + string.Join(", ", n.Tags) : "")}]: {text}");
        }
        sb.AppendLine();
        sb.Append("When done, reply in a few lines: what went where (by theme, not note by note), any new columns or tags, and any duplicates.");
        return sb.ToString();
    }
}
