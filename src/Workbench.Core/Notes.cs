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

    /// <summary>Turn several notes into cards at once, oldest note first so the card IDs follow the order
    /// the notes were written. Notes that already have a ticket, or have no text, are left alone.</summary>
    public static List<Card> MakeTickets(string folder, IEnumerable<Note> notes, string boardFolder) =>
        notes.Where(n => n.Ticket.Length == 0 && n.Text.Trim().Length > 0)
            .OrderBy(n => n.Created, StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => MakeTicket(folder, n, boardFolder).Card).ToList();
}
