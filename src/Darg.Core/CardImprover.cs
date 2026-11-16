using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>Improve a card (WOR-0073): an agent rounds a thin card out (a note's few words, "improve ticket
/// button") into one someone could pick up cold: what's wanted, where it lives in the code, what done looks
/// like. It asks when the words could mean several things, and doesn't do the work itself.</summary>
public static partial class CardImprover
{
    [GeneratedRegex(@"From a note on the wall \(([^)\s]+)\)")]
    private static partial Regex FromNote();

    /// <summary>The note a card was made from ("From a note on the wall (id)" in its description), on the
    /// wall or in its archive; null when it names none or the note is gone.</summary>
    public static string? NoteFile(string? notesFolder, string description)
    {
        if (notesFolder is null || FromNote().Match(description) is not { Success: true } m) return null;
        var name = m.Groups[1].Value + ".md";
        return new[] { Path.Combine(notesFolder, name), Path.Combine(NoteStore.ArchiveFolder(notesFolder), name) }
            .FirstOrDefault(File.Exists);
    }

    /// <summary>What the agent is asked. <paramref name="commits"/> is the commit rule for the card's folder,
    /// already put as an instruction; with none it doesn't commit.</summary>
    public static string Prompt(string projectName, Card card, string file, string? noteFile = null, string? commits = null)
    {
        var lines = new List<string>
        {
            $"Improve this card on the {projectName} board so that an agent, or I, could pick it up cold. Don't do the work it asks for.",
            "",
            $"{card.Id}: {card.Title}",
            $"Card file: {file}",
        };
        if (noteFile is not null) lines.Add($"It was made from this note on the wall: {noteFile}");
        lines.AddRange([
            "",
            "1. Read the card" + (noteFile is not null ? ", the note" : "") + ", the project's CLAUDE.md and enough of the code to know " +
            "where this lives. Glance at the board's other cards (the items folder beside this one) for one that already covers it, and say so if one does.",
            "2. If what I meant is unclear (a few words can mean several things), ask me with AskUserQuestion before writing anything. " +
            "Put your readings in the question itself, since I may only see the question: your best reading first, marked (Recommended), " +
            "and one or two real alternatives. Don't pick one quietly and write it up.",
            "3. Rewrite the card's ## Description:",
            "   - My words first, as they were, quoted, so the original survives (keep any \"From a note on the wall\" line and any pictures).",
            "   - What's wanted, in a sentence or two of plain words.",
            "   - Where: the screen and the files or classes involved, with paths.",
            "   - Done when: a short list of checks someone could see or run.",
            "   - Questions left, if any.",
            "   Keep it short: a card, not a spec.",
            "4. If the title doesn't say what the card is, sharpen it on the # line (keep the id) and rename the file to match " +
            $"(\"{card.Id} - <title>.md\"). Set Type to Bug if it is one. Leave Status, Priority, board.json and the code alone, " +
            "and edit no file but the card's.",
            "5. " + (commits ?? "Don't commit or push."),
            "",
            "When you stop, say in a line or two what the card now asks for.",
        ]);
        return string.Join(Environment.NewLine, lines);
    }
}
