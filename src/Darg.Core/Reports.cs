using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>What a card agent filed when it finished: the card, what was asked, what it did, and the commits.</summary>
/// <param name="File">The report's file (its name is its id: date, time and card).</param>
/// <param name="Agent">The agent that wrote it, when it was told its id; Darg forgets that agent once its turn ends.</param>
/// <param name="Body">The markdown below the properties.</param>
public sealed record AgentReport(string File, string Card, string Title, string Agent, IReadOnlyList<string> Commits,
    string Repo, string Date, bool Read, string Body)
{
    public string Id => Path.GetFileNameWithoutExtension(File);
    public string Heading => Card.Length > 0 && Title.Length > 0 ? $"{Card}: {Title}" : Title.Length > 0 ? Title : Card.Length > 0 ? Card : Id;
}

/// <summary>A project's finished-agent reports (WOR-0061): <c>&lt;instance&gt;/projects/&lt;project&gt;/reports/</c>, one markdown
/// file each, with Obsidian properties (card, title, agent, commits, repo, date, read). A card agent that is sure
/// its work is done moves the card to done and writes one (told how by <see cref="Rules"/>); work that needs the
/// person to look at it goes to the board's QA column instead, with no report, and the agent stays. Darg lists the
/// reports, unread first in weight, marks one read when it's opened, and forgets the agent that wrote it, so a
/// small one-shot task needs no tending.</summary>
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
            fields.GetValueOrDefault("repo", ""), date, IsTrue(fields.GetValueOrDefault("read", "")), body.TrimEnd());
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

    /// <summary>What a card agent is told about finishing, in place of "move the card to done": QA when the person
    /// should look, else done with a report. Free of double quotes, angle brackets, ampersands and pipes.</summary>
    /// <param name="qaColumn">The board's QA column, if it has one.</param>
    /// <param name="agentId">The agent's own id, written into the report so Darg forgets the right agent.</param>
    public static string Rules(string folder, Card card, string? qaColumn, string? agentId)
    {
        var name = $"{DateTime.Now:yyyyMMdd-HHmm}-{card.Id}.md";
        var qa = qaColumn is not null
            ? $"first add a section headed ## How to check to the card file, below the description, with what changed, the branch and commit ids to look at, " +
              "the exact command to run or file to open to try it (a screenshot path if you took one), and what to look for; " +
              $"then set its Status to {qaColumn} and move its id into that column in board.json's order"
            : "leave it where it is (this board has no QA column)";
        return
            "When the work is finished and pushed, decide whether I need to check it. " +
            "It needs checking when part of it is something you couldn't see or verify yourself (how it looks, behaviour you didn't exercise live, " +
            $"a choice you made on my behalf, tests you couldn't run): then {qa}, say in your last message exactly what to check, and write no report. " +
            "Otherwise, when you are confident it is done and verified, move the card to done yourself: " +
            "set its Status to the board's last column (Completed if the board has no done column), add a Done line with today's date, " +
            "and move its id into that column in board.json's order. Then, as the very last thing you do (after any merge, so the commit ids are final), " +
            $"write a short report to {Path.Combine(folder, name).Replace('\\', '/')} for me to read later; Darg lists it under Reports as unread " +
            "and forgets this agent once your turn ends, so the report is what I will see of this work. It starts with these properties between --- lines: " +
            $"card: {card.Id}, title: the card's title, " + (agentId is not null ? $"agent: {agentId}, " : "") +
            "commits: the short ids of your commits as they are on the branch they landed on, space-separated, repo: the repo's folder, " +
            $"date: {DateTime.Now:yyyy-MM-dd} and the time, read: false. Below them, three short sections: ## Asked (the problem, in a sentence or two), " +
            "## Done (what you changed and how you checked it), ## Left (anything not done, or None). " +
            "Leave the card where it is while work is left.";
    }

    /// <summary>The prompt for Work on a card already in the QA column: not more work, but getting the result
    /// in front of the person. Free of double quotes, angle brackets, ampersands and pipes.</summary>
    public static string CheckPrompt(string projectName, Card card, string file) =>
        $"Work on this card from the {projectName} board: {card.Id}: {card.Title}{Environment.NewLine}Card file: {file}{Environment.NewLine}{Environment.NewLine}" +
        "This card is in QA: the work is done and I want to try it, not have it changed. Read the card file, especially its How to check section, " +
        "and the project's CLAUDE.md. Then get it running for me: build and start the app (or open the page, or run the command) from the branch or commit " +
        "the card names, in a git worktree of its own if that is not what the main folder is on, and leave it running. Say in one message what is running, " +
        "from which commit, and what to look at. Change no code and don't move the card. If something won't build or the How to check section is missing, say so plainly.";

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

public static class BoardColumns
{
    /// <summary>The board's QA column: one named for QA, review or checking, not the last (that is the done column).</summary>
    public static string? Qa(IReadOnlyList<Column> columns) =>
        columns.Take(Math.Max(0, columns.Count - 1))
            .FirstOrDefault(c => Regex.IsMatch(c.Name, @"\bQA\b|review|to check|testing", RegexOptions.IgnoreCase))?.Name;
}
