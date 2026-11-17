using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>What a card agent filed when it finished: the card, what was asked, what it did, and the commits.</summary>
/// <param name="File">The report's file (its name is its id: date, time and card).</param>
/// <param name="Agent">The agent that wrote it, when it was told its id; Darg forgets that agent once its turn ends.</param>
/// <param name="Body">The markdown below the properties.</param>
/// <param name="Qa">A QA handoff (<c>status: qa</c>, WOR-0088): the work went to the QA column for the person to check,
/// and the agent that wrote it stays.</param>
/// <param name="Decision">A decision report's state (WOR-0092): pending, approved or sent back; empty for any other report.</param>
/// <param name="Investigation">An investigation (<c>status: investigation</c>, WOR-0091): the agent looked into something
/// and changed nothing, so the report is the finding.</param>
public sealed record AgentReport(string File, string Card, string Title, string Agent, IReadOnlyList<string> Commits,
    string Repo, string Date, bool Read, string Body, bool Qa = false, bool Investigation = false, string Decision = "")
{
    /// <summary>Which of the three lists it belongs to (WOR-0091): QA wins, then investigation, else a finished task.</summary>
    public ReportKind Kind => Qa ? ReportKind.Qa : Investigation ? ReportKind.Investigation : ReportKind.Finished;
    public string Id => Path.GetFileNameWithoutExtension(File);
    public string Heading => Card.Length > 0 && Title.Length > 0 ? $"{Card}: {Title}" : Title.Length > 0 ? Title : Card.Length > 0 ? Card : Id;
}

/// <summary>The three kinds of report the Reports section divides into (WOR-0091).</summary>
public enum ReportKind { Finished, Investigation, Qa }

/// <summary>A project's finished-agent reports (WOR-0061): <c>&lt;instance&gt;/projects/&lt;project&gt;/reports/</c>, one markdown
/// file each, with Obsidian properties (card, title, agent, commits, repo, date, read). A card agent that is sure
/// its work is done moves the card to done and writes one (told how by <see cref="Rules"/>), and Darg forgets the agent
/// that wrote it, so a small one-shot task needs no tending; work that needs the person to look at it goes to the
/// board's QA column with a short QA report (<c>status: qa</c>, WOR-0088), and that agent stays. Darg lists the
/// reports, unread first in weight, and marks one read when it's opened, so Reports is the one place to triage from.</summary>
public static partial class ReportStore
{
    /// <summary>In a folder of its own, so a card agent can be given that folder (--add-dir) and nothing beside it.</summary>
    public static string FolderFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "projects", Safe(projectId.Length > 0 ? projectId : "default"), "reports");

    /// <summary>Every report in the folder, newest first (file names start with the date and time).</summary>
    public static List<AgentReport> List(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var all = new List<AgentReport>();
        foreach (var f in Directory.GetFiles(folder, "*.md"))
        {
            try { all.Add(Parse(System.IO.File.ReadAllText(f), f)); }
            catch (IOException) { } // being written: the watcher brings it in when it lands
        }
        return all.OrderByDescending(r => r.Date, StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal).ToList();
    }

    public static AgentReport Parse(string text, string file)
    {
        var (fields, body) = Split(text);
        var commits = CommitRx().Matches(fields.GetValueOrDefault("commits", "")).Select(m => m.Value).Distinct().ToList();
        var date = fields.GetValueOrDefault("date", "");
        if (date.Length == 0)
        {
            try { date = System.IO.File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm"); } catch (IOException) { }
        }
        // a report without a title line: its first heading, else the card id
        var title = fields.GetValueOrDefault("title", "");
        if (title.Length == 0 && Regex.Match(body, @"^#\s+(.+)$", RegexOptions.Multiline) is { Success: true } h) title = h.Groups[1].Value.Trim();
        return new AgentReport(file, fields.GetValueOrDefault("card", ""), title, fields.GetValueOrDefault("agent", ""), commits,
            fields.GetValueOrDefault("repo", ""), date, IsTrue(fields.GetValueOrDefault("read", "")), body.TrimEnd(),
            Status(fields) == "qa", Status(fields) == "investigation",
            fields.GetValueOrDefault("decision", "").Trim().Trim('"', '\'').ToLowerInvariant());
    }

    /// <summary>Mark a report read or unread by rewriting its read property (added if missing); everything else stays as written.</summary>
    public static void SetRead(string file, bool read)
    {
        var text = System.IO.File.ReadAllText(file).Replace("\r\n", "\n");
        var value = read ? "true" : "false";
        string updated;
        if (text.StartsWith("---\n") && text.IndexOf("\n---", 3, StringComparison.Ordinal) is var end and > 0)
        {
            var props = end > 3 ? text[4..end] : "";
            var rx = ReadLineRx();
            props = rx.IsMatch(props) ? rx.Replace(props, $"read: {value}", 1) : props + $"\nread: {value}";
            updated = "---\n" + props + text[end..];
        }
        else updated = $"---\nread: {value}\n---\n\n" + text;
        System.IO.File.WriteAllText(file, updated, new UTF8Encoding(false));
    }

    /// <summary>Told to agents writing a report: Reports draws pictures and makes links clickable. Free of double quotes, angle brackets, ampersands and pipes.</summary>
    public const string ReportMedia =
        "Reports shows markdown pictures and links, so where it helps put them in: a screenshot you took as a markdown image with its full path (an image on this machine, for example ![the board](C:/path/shot.png)), " +
        "and a link for a commit, branch, pull request, issue or page the reader may want to open (a full https address). Leave them out when there is nothing to show.";

    /// <summary>What a card agent is told about finishing, in place of "move the card to done": QA with a short QA
    /// report (<c>status: qa</c>) when the person should look, and the agent stays; else done with a report, and the
    /// agent is forgotten. Free of double quotes, angle brackets, ampersands and pipes.</summary>
    /// <param name="qaColumn">The board's QA column, if it has one.</param>
    /// <param name="agentId">The agent's own id, written into the report so Darg forgets (or keeps) the right agent.</param>
    public static string Rules(string folder, Card card, string? qaColumn, string? agentId, string? attentionColumn = null)
    {
        var stamp = $"{DateTime.Now:yyyyMMdd-HHmm}-{card.Id}";
        string FileFor(string suffix) => Path.Combine(folder, stamp + suffix + ".md").Replace('\\', '/');
        var props =
            $"card: {card.Id}, title: the card's title, " + (agentId is not null ? $"agent: {agentId}, " : "") +
            "commits: the short ids of your commits as they are on the branch they landed on, space-separated, repo: the repo's folder, " +
            $"date: {DateTime.Now:yyyy-MM-dd} and the time, read: false";
        var howToCheck = "a section headed ## How to check to the card file, below the description, with what changed, the branch and commit ids to look at, " +
            "the exact command to run or file to open to try it (a screenshot path if you took one), and what to look for";
        var qa = qaColumn is not null
            ? $"first add {howToCheck}; then set its Status to {qaColumn} and move its id into that column in board.json's order"
            : $"first add {howToCheck}, and leave the card where it is (this board has no QA column)";
        return
            "When the work is finished and pushed, decide whether I need to check it. " +
            "It needs checking when part of it is something you couldn't see or verify yourself (how it looks, behaviour you didn't exercise live, " +
            $"a choice you made on my behalf, tests you couldn't run): then {qa}, and say in your last message exactly what to check. " +
            "Then, as the very last thing you do (after any merge, so the commit ids are final), write a short QA report to " +
            $"{FileFor("-qa")} so I can triage this from Reports; Darg lists it as unread and keeps this agent, for me to talk to about what I find. " +
            $"It starts with these properties between --- lines: status: qa, {props}. Below them, three short sections: " +
            "## Changed (what you changed, in a few lines), ## Look at (what to try and what to look for; the How to check section has the detail), " +
            "## Checked (what you verified yourself and how, and what you could not check). " + ReportMedia + " " +
            "Otherwise, when you are confident it is done and verified, move the card to done yourself: " +
            "set its Status to the board's last column (Completed if the board has no done column), add a Done line with today's date, " +
            "and move its id into that column in board.json's order. Then, as the very last thing you do (after any merge, so the commit ids are final), " +
            $"write a short report to {FileFor("")} for me to read later; Darg lists it under Reports as unread " +
            "and forgets this agent once your turn ends, so the report is what I will see of this work. " +
            $"It starts with these properties between --- lines: {props}. Below them, three short sections: ## Asked (the problem, in a sentence or two), " +
            "## Done (what you changed and how you checked it), ## Left (anything not done, or None). " + ReportMedia + " " +
            "If the card asked you to look into something, find something out or answer a question, and you changed no code, the report is the finding: " +
            $"write it to {FileFor("")} as above but with status: investigation as its first property (Asked is the question, Done is what you found and how, Left is what is still unknown, or None), then move the card to done. " +
            "Leave the card where it is, with no report, while work is left." + (attentionColumn is not null
                ? $" If you move the card to {attentionColumn}, don't write its decision report: Darg writes one when a card lands there, from the card file, so keep How to check in the card up to date."
                : "");
    }

    /// <summary>The prompt for Work on a card already in the QA column: not more work, but getting the result
    /// in front of the person. Free of double quotes, angle brackets, ampersands and pipes.</summary>
    public static string CheckPrompt(string projectName, Card card, string file) =>
        $"Work on this card from the {projectName} board: {card.Id}: {card.Title}{Environment.NewLine}Card file: {file}{Environment.NewLine}{Environment.NewLine}" +
        "This card is in QA: the work is done and I want to try it, not have it changed. Read the card file, especially its How to check section, " +
        "and the project's CLAUDE.md. Then get it running for me: build and start the app (or open the page, or run the command) from the branch or commit " +
        "the card names, in a git worktree of its own if that is not what the main folder is on, and leave it running. Say in one message what is running, " +
        "from which commit, and what to look at. Change no code and don't move the card. If something won't build or the How to check section is missing, say so plainly.";

    /// <summary>The prompt for Run QA on a card in QA (WOR-0108): the agent does the checking itself, from the How to check
    /// section, and reports what passed and what didn't. Free of double quotes, angle brackets, ampersands and pipes.</summary>
    public static string QaRunPrompt(string projectName, Card card, string file) =>
        $"Work on this card from the {projectName} board: {card.Id}: {card.Title}{Environment.NewLine}Card file: {file}{Environment.NewLine}{Environment.NewLine}" +
        "This card is in QA: the work is done and I want you to QA it for me, not change it. Read the card file, especially its How to check section, " +
        "and the project's CLAUDE.md. Then do the checking yourself as far as you can: build it from the branch or commit the card names " +
        "(in a git worktree of its own if that is not what the main folder is on), run the tests and the commands the section gives, and exercise what it says to look for. " +
        "Say in one message what you checked and how, what passed, what failed or looks wrong, and what you could not check and why, so I only have to look at that. " +
        "Change no code and don't move the card or write a report: the verdict is mine. If something won't build or the How to check section is missing, say so plainly.";

    /// <summary>What goes before a card agent's usual prompt when the person sends a card back from QA with
    /// notes (WOR-0079): build on the work the How to check section names, fix what the notes say, and give the
    /// next round its own How to check. Free of double quotes, angle brackets, ampersands and pipes in its own words.</summary>
    public static string ReworkPreface(string notes) =>
        "This card came back from QA. I tried the work its How to check section describes and it isn't right yet. " +
        $"What I found (also kept in the card file under {QaNotes.Heading}, newest last):{Environment.NewLine}{Environment.NewLine}" +
        notes.Trim() + Environment.NewLine + Environment.NewLine +
        "Read the card's How to check section before anything else: it says what was done and in which commits. " +
        "Build on that work rather than starting again, fix what my notes say, and leave what already works alone. " +
        "If a note could mean several things, ask me with AskUserQuestion before changing anything. " +
        "Going back to QA or to the attention column (Darg writes that report itself), replace the How to check section with one for this round (what changed since, the new commits, what to look for) " +
        $"rather than adding a second one, leave the {QaNotes.Heading} section as it is, and write a fresh QA report for this round " +
        "(a new file, as the rules below say) rather than editing the last one.";

    static string Status(Dictionary<string, string> f) => f.GetValueOrDefault("status", "").Trim().Trim('"', '\'').ToLowerInvariant();

    static bool IsTrue(string v) => v.Trim().Trim('"', '\'').ToLowerInvariant() is "true" or "yes" or "1";

    static (Dictionary<string, string> Fields, string Body) Split(string text)
    {
        text = text.Replace("\r\n", "\n");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!text.StartsWith("---\n")) return (fields, text);
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return (fields, text);
        string? listKey = null;
        foreach (var line in (end > 3 ? text[4..end] : "").Split('\n'))
        {
            // a YAML list under a key ("commits:" then "  - abc1234") reads as the items joined
            if (listKey is not null && Regex.Match(line, @"^\s+-\s*(.*)$") is { Success: true } item)
            {
                fields[listKey] = (fields[listKey] + " " + item.Groups[1].Value.Trim()).Trim();
                continue;
            }
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            fields[key] = line[(colon + 1)..].Trim().Trim('[', ']').Trim();
            listKey = fields[key].Length == 0 ? key : null;
        }
        var body = text[(end + 4)..];
        var nl = body.IndexOf('\n');
        body = nl < 0 ? "" : body[(nl + 1)..]; // the rest of the closing --- line
        return (fields, body.TrimStart('\n'));
    }

    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [GeneratedRegex(@"\b[0-9a-f]{7,40}\b")]
    private static partial Regex CommitRx();

    [GeneratedRegex(@"^read\s*:.*$", RegexOptions.Multiline)]
    private static partial Regex ReadLineRx();
}

/// <summary>What the person found trying a card in QA (WOR-0079): a <c>## QA notes</c> section in the card file,
/// below the rest, a dated <c>###</c> entry per send-back, so every round's notes stay with the card. The board
/// reads only the Description section, so editing the card in Darg leaves these alone.</summary>
public static class QaNotes
{
    public const string Heading = "## QA notes";

    /// <summary>Add a dated entry to the card's QA notes, making the section at the end if it has none.</summary>
    public static void Add(string file, string notes, DateTime when)
    {
        var raw = System.IO.File.ReadAllText(file);
        var crlf = raw.Contains("\r\n");
        var text = raw.Replace("\r\n", "\n").TrimEnd('\n');
        var entry = $"### {when:yyyy-MM-dd HH:mm}\n\n{notes.Replace("\r\n", "\n").Trim()}";
        var at = Section(text);
        if (at < 0) text += $"\n\n{Heading}\n\n{entry}\n";
        else
        {
            // the end of the QA notes section: the next ## heading, or the end of the file
            var next = text.IndexOf("\n## ", at + Heading.Length, StringComparison.Ordinal);
            var end = next >= 0 ? next : text.Length;
            text = text[..end].TrimEnd('\n') + $"\n\n{entry}\n" + (next >= 0 ? "\n" + text[(next + 1)..] + "\n" : "");
        }
        System.IO.File.WriteAllText(file, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
    }

    static int Section(string text) =>
        text.StartsWith(Heading + "\n", StringComparison.Ordinal) ? 0 : text.IndexOf("\n" + Heading + "\n", StringComparison.Ordinal) is var i and >= 0 ? i + 1 :
        text.EndsWith("\n" + Heading, StringComparison.Ordinal) ? text.Length - Heading.Length : -1;
}

public static class BoardColumns
{
    /// <summary>The board's QA column: one named for QA, review or checking, not the last (that is the done column).</summary>
    public static string? Qa(IReadOnlyList<Column> columns) =>
        columns.Take(Math.Max(0, columns.Count - 1))
            .FirstOrDefault(c => Regex.IsMatch(c.Name, @"\bQA\b|review|to check|testing", RegexOptions.IgnoreCase))?.Name;
}
