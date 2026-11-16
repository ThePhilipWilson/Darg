using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workbench.Core;

/// <summary>A post-it: one markdown file, its fields as Obsidian-style properties at the top.</summary>
public sealed record Note(string Id, string Text, string Column, string Colour, IReadOnlyList<string> Tags,
    string Created, string Ticket)
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

    public static IReadOnlyList<string> Columns(string folder)
    {
        var path = Path.Combine(folder, "wall.json");
        if (!File.Exists(path)) return DefaultColumns;
        var cols = (JsonNode.Parse(File.ReadAllText(path))?["columns"] as JsonArray)?.Select(c => (string?)c).OfType<string>().ToList();
        return cols is { Count: > 0 } ? cols : DefaultColumns;
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
            fields.GetValueOrDefault("colour", "yellow"), tags, fields.GetValueOrDefault("created", ""), fields.GetValueOrDefault("ticket", ""));
    }

    public static string Render(Note n)
    {
        var sb = new StringBuilder("---\n");
        sb.Append($"column: {n.Column}\ncolour: {n.Colour}\n");
        if (n.Tags.Count > 0) sb.Append($"tags: [{string.Join(", ", n.Tags)}]\n");
        sb.Append($"created: {n.Created}\n");
        if (n.Ticket.Length > 0) sb.Append($"ticket: {n.Ticket}\n");
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

    /// <summary>Turn a note into a card on a board's backlog (first line as the title, the rest as the
    /// description, tags as labels) and mark the note with the card's ID.</summary>
    public static (Note Note, Card Card) MakeTicket(string folder, Note note, string boardFolder)
    {
        var rest = note.Text.Contains('\n') ? note.Text[(note.Text.IndexOf('\n') + 1)..].Trim() : "";
        var title = note.FirstLine.Length > 70 ? note.FirstLine[..70].TrimEnd() : note.FirstLine;
        var desc = (rest.Length > 0 ? rest + "\n\n" : "") + $"From a note on the wall ({note.Id}).";
        var card = BoardStore.Create(boardFolder, title, desc, note.Tags);
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
    /// Done column, as Workbench's does, never use Completed).</summary>
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
