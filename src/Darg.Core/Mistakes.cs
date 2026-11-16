using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>One entry in a project's mistake log: a level-3 heading (date and title) and its bullet fields.</summary>
/// <param name="Body">The entry's markdown below its heading, as written.</param>
public sealed record Mistake(DateOnly? Date, string Title, string CaughtBy, IReadOnlyList<string> Categories, string Body)
{
    public string Heading => Date is { } d ? $"{d:yyyy-MM-dd}: {Title}" : Title;
}

/// <summary>A project's mistake log: <c>&lt;instance&gt;/projects/&lt;project&gt;/mistakes/mistake_log.md</c>, append-only, in the
/// schema of the mistake_log.md files kept before Darg (Beta's), so the same reading works on both. Agents
/// write it (told how by <see cref="Briefing"/>, or asked by the Mistake button through <see cref="AskToLog"/>);
/// Darg only reads it, and counts the categories, since three of one kind is the point to build a gate.</summary>
public static partial class MistakeLog
{
    /// <summary>The name the "unlogged mistake" Stop hook already counts as a durable write.</summary>
    public const string FileName = "mistake_log.md";
    /// <summary>How many of one category make it time for a hook or gate rather than another memory.</summary>
    public const int GateAt = 3;

    /// <summary>In a folder of its own, so an agent can be given that folder (--add-dir) and not the board and notes beside it.</summary>
    public static string PathFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "projects", Safe(projectId.Length > 0 ? projectId : "default"), "mistakes", FileName);

    /// <summary>The categories the log starts with; an agent may add one when none fits.</summary>
    public static readonly string[] Categories =
    [
        "workflow-skip", "default-trust", "premature-claim", "plan-drift", "convention-miss",
        "tooling-error", "scope-creep", "measurement-gap", "instruction-mishandled", "unauthorised-action",
    ];

    /// <summary>Make the log with its header (when to log, the schema, the categories) if it isn't there yet.</summary>
    public static string Ensure(string path, string projectName)
    {
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Header(projectName), new UTF8Encoding(false));
        return path;
    }

    public static string Header(string projectName) =>
        $"""
        # {projectName} mistake log

        Append-only record of mistakes agents made (or were about to make) on {projectName}. Darg lists it under Mistakes and counts each category; {GateAt} of one category means a hook or gate is due, not another memory.

        **Never edit or delete past entries.** A correction to an entry goes in a new entry that names the old one.

        ## When to add an entry

        - The person corrects or redirects what the agent did, or repeats an instruction that didn't land
        - The person points out a CLAUDE.md, memory or workflow rule the agent didn't apply
        - The agent catches its own mistake mid-action (self-catches count)

        Skip routine clarifying questions.

        ## Schema

        ```
        ### YYYY-MM-DD: short descriptive title

        - **Caught by:** user | self | hook
        - **Category:** `one-of-the-categories`
        - **Trigger:** quote or paraphrase of what surfaced it
        - **What I did (or was about to do):** concrete, with file names, commands, phrasings
        - **What was wrong:** the failure, named as a failure mode
        - **Right behaviour:** what should have happened
        - **Rule violated:** the CLAUDE.md section, memory or plan; "None, new rule needed" is valid
        - **Notes:** suspected root cause, related entries, a gate to build if it recurs
        ```

        ## Categories

        {string.Join(Environment.NewLine, Categories.Select(c => $"- `{c}`"))}

        Add a new one when none fits.

        ## Entries

        """;

    /// <summary>The entries in a log file, as written (oldest first); none when there is no file.</summary>
    public static List<Mistake> Read(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : []; }
        catch (IOException) { return []; }
    }

    /// <summary>Every level-3 heading outside a code fence starts an entry; a level-1 or level-2 heading ends one.</summary>
    public static List<Mistake> Parse(string text)
    {
        var entries = new List<Mistake>();
        string? heading = null;
        var body = new List<string>();
        var fence = false;
        void Flush()
        {
            if (heading is not null) entries.Add(Make(heading, body));
            heading = null;
            body.Clear();
        }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~")) fence = !fence;
            if (!fence && line.StartsWith("### ")) { Flush(); heading = line[4..].Trim(); continue; }
            if (!fence && (line.StartsWith("# ") || line.StartsWith("## "))) { Flush(); continue; }
            if (heading is not null) body.Add(line);
        }
        Flush();
        return entries;
    }

    static Mistake Make(string heading, List<string> body)
    {
        DateOnly? date = null;
        var title = heading;
        var m = HeadingRx().Match(heading);
        if (m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", out var d))
        {
            date = d;
            title = m.Groups[2].Value.Trim();
            if (title.Length == 0) title = heading;
        }
        var text = string.Join("\n", body).Trim('\n');
        return new Mistake(date, title, Field(body, "Caught by") ?? "", SplitCategories(Field(body, "Category")), text);
    }

    /// <summary>A bullet field's value: "- **Name:** value" (the colon inside or outside the bold).</summary>
    static string? Field(List<string> body, string name)
    {
        var rx = new Regex(@"^\s*[-*]\s*\*\*" + Regex.Escape(name) + @"\s*:?\*\*\s*:?\s*(.*)$", RegexOptions.IgnoreCase);
        foreach (var line in body)
            if (rx.Match(line) is { Success: true } m) return m.Groups[1].Value.Trim();
        return null;
    }

    /// <summary>"`a` + `b`", "a, b (why)", "`c` (in the sense of x)": the slugs, lower case, once each.</summary>
    public static IReadOnlyList<string> SplitCategories(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var ticks = Regex.Matches(value, "`([^`]+)`").Select(x => x.Groups[1].Value).ToList();
        IEnumerable<string> parts = ticks.Count > 0 ? ticks
            : Regex.Replace(value, @"\([^)]*\)?", " ").Split([',', '+', ';', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => p.Trim().Trim('`', '.', '*').Trim().ToLowerInvariant())
            .Where(p => p.Length > 0).Distinct().ToList();
    }

    /// <summary>How often each category comes up, most first (then by name).</summary>
    public static List<(string Category, int Count)> Tally(IEnumerable<Mistake> entries) =>
        entries.SelectMany(e => e.Categories).GroupBy(c => c)
            .Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();

    /// <summary>What every agent is told about the log (appended to its system prompt). Free of the characters a
    /// shell might take as syntax (angle brackets, double quotes, ampersands, pipes): it goes on a command line.</summary>
    public static string Briefing(string path) =>
        $"Mistake log: this project keeps one at {path}. When the person corrects or redirects you, repeats an instruction, " +
        "or points out a rule you didn't apply, or you catch your own mistake, append an entry to it in that turn, following the schema at its top " +
        "(a level-3 heading with the date and a short title, then Caught by, Category, Trigger, What I did, What was wrong, Right behaviour, Rule violated, Notes). " +
        "Never edit past entries. Create the file with a heading and an Entries section if it is missing. Darg lists the log under Mistakes and counts the categories.";

    /// <summary>The message the Mistake button sends: log the reply before it, with what the person typed as what was wrong.</summary>
    public static string AskToLog(string path, string what) =>
        (what.Trim().Length == 0
            ? "Your last reply was a mistake. Work out what was wrong with it; if you can't tell, ask me with AskUserQuestion rather than guess. "
            : $"Your last reply was a mistake: {what.Trim()}\n\n") +
        $"Log it in the project's mistake log, {path}: append one entry following the schema at the top of that file " +
        $"(Caught by: user; pick the closest category, or add one if none fits). Then say in a line what you logged, and carry on from the correction.";

    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})\s*(?:[:\-–—]\s*)?(.*)$")]
    private static partial Regex HeadingRx();
}
