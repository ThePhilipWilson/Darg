using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A plan for one feature (its cards), or for a whole project (its features).</summary>
public enum PlanKind { Feature, Project }

/// <summary>The planner (WOR-0176, docs/FEATURE-PLANNING.md "The planner"): an agent that drafts a whole plan from a
/// line Philip types, every part filled, so he judges a draft rather than answering an interview (he judges in
/// seconds; writing from a blank page is the slow part). Darg names the file before it starts, so the Design page
/// knows which plan the agent is drafting and opens it when its turn ends. The agent stays beside the page: asked
/// to change one part, it changes that part only, and <see cref="KeepOnly"/> puts back anything else it touched.</summary>
public static partial class Planner
{
    public static PlanKind? ParseKind(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "feature" => PlanKind.Feature,
        "project" => PlanKind.Project,
        _ => null,
    };

    /// <summary>Where a new plan for <paramref name="idea"/> goes: its name (the words before a colon, else the first
    /// few) in lower-case words joined by hyphens, numbered past any plan already there or <paramref name="taken"/> by a
    /// planner still drafting one.</summary>
    public static string FileFor(string folder, string idea, IEnumerable<string>? taken = null)
    {
        var head = idea.Split('\n')[0];
        var colon = head.IndexOf(':');
        if (colon > 2) head = head[..colon];
        var words = Word().Matches(head.ToLowerInvariant()).Select(m => m.Value).Take(6).ToList();
        var slug = "";
        foreach (var w in words)
        {
            if (slug.Length > 0 && slug.Length + 1 + w.Length > 40) break;
            slug = slug.Length == 0 ? w : slug + "-" + w;
        }
        if (slug.Length == 0) slug = "plan";
        var file = Path.Combine(folder, slug + ".md");
        var held = new HashSet<string>((taken ?? []).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        for (var n = 2; File.Exists(file) || held.Contains(Path.GetFullPath(file)); n++) file = Path.Combine(folder, $"{slug}-{n}.md");
        return file;
    }

    /// <summary>What the planner is asked.</summary>
    /// <param name="idea">Philip's line: what the feature or project is.</param>
    /// <param name="file">The plan file to write (from <see cref="FileFor"/>); it doesn't exist yet.</param>
    /// <param name="docs">The project's docs folder, to read.</param>
    /// <param name="boards">The project's boards folder, to read (cards on it may be Blocked by entries).</param>
    /// <param name="plans">The plans already in the design folder, by file.</param>
    /// <param name="commits">The commit rule for where the plan file is (<see cref="Commits.Describe(CommitRule, string)"/>); null: don't commit.</param>
    /// <param name="parent">The project plan this feature plan sits under (its file name without .md), if any.</param>
    public static string Prompt(string projectName, string idea, PlanKind kind, string file, string? docs, string? boards,
        IReadOnlyList<string> plans, string? commits, string? parent = null)
    {
        var project = kind == PlanKind.Project;
        var lines = new List<string>
        {
            $"Draft a plan for {(project ? "a whole project" : "a feature")} of the {projectName} project, from my one line:",
            "> " + idea.Trim().Replace("\n", "\n> "),
            "",
            "Plan file: " + file,
            "",
            "I judge a draft in seconds but write from a blank page slowly, so draft every part yourself before anything comes back to me. " +
            "Don't interview me: don't ask me anything before the plan file is written. Every call you aren't sure of goes into the plan's Questions, " +
            "with options and one recommended, for me to tap an answer on the Design page. Don't settle such a call quietly in the Design or the cards.",
            "",
            "1. Read first: the project's CLAUDE.md" + (docs is null ? "" : $", its docs ({docs})") + (boards is null ? "" : $", its boards ({boards}: what's built, under way and planned)") +
            (plans.Count > 0 ? ", the plans already written (" + string.Join(", ", plans) + "): if one already covers this, say so and stop rather than writing a second" : "") +
            ", and the code the " + (project ? "project" : "feature") + " touches.",
        };
        // a feature of a project plan (WOR-0179): the project's plan sets the frame this one is drafted in
        if (!project && !string.IsNullOrWhiteSpace(parent))
            lines.Add($"   This feature is one of the Features of the project plan {Path.Combine(Path.GetDirectoryName(file) ?? "", parent + ".md")}: read it first. " +
                      "Its Goals, Not doing and answered Questions hold for this plan too; don't ask again what it has answered, and keep to this feature, not its siblings.");
        lines.AddRange(new[]
        {
            $"2. Write the plan to {file}, a new file, in this shape (the Design page reads these headings, so keep them):",
            "",
            "   ---",
            $"   kind: {(project ? "project" : "feature")}",
            "   status: draft",
        });
        if (!string.IsNullOrWhiteSpace(parent)) lines.Add($"   parent: {parent}");
        lines.AddRange([
            "   ---",
            "   # Name",
            "",
            "   One or two lines: what it is, in plain words.",
            "",
            "   ## Goals",
            "   - What is different for me when it's done, a few lines. Not features: \"answer what agents wait on from the sofa\", not \"an Android app\".",
            "",
            "   ## Not doing",
            "   - What is out, so a card agent doesn't drift into it.",
            "",
            "   ## Questions",
            "",
            "   ### A question only I can answer?",
            "   - **Option** (recommended): what picking it means.",
            "   - **Another option**: what that means.",
            "",
            "   **Context:** only what I need to answer, in a sentence or two.",
            "",
            "   ## Design",
            "   Free markdown: the approach, the parts, what it builds on, the risks.",
            "",
        ]);
        if (project)
        {
            lines.AddRange([
                "   ## Features",
                "   - **A feature**: a line on what it gives me. Each gets a feature plan of its own later, not cards here.",
                "",
            ]);
        }
        else
        {
            lines.AddRange([
                "   ## Cards",
                "",
                "   ### A card's title",
                "   What's wanted, in at least twenty words: the problem and the change, so an agent can pick it up cold.",
                "   **Where:** the files, classes or folders it touches, as `src/Some/File.cs`.",
                "   **Done when:** what can be seen or tested once it's done.",
                "   **Blocked by:** none",
                "",
            ]);
        }
        lines.AddRange([
            "   Questions: two to four options each, exactly one marked (recommended), and no **Answer:** line (that's mine). Leave the part with just its heading if nothing waits on me.",
        ]);
        if (project)
            lines.Add("   Features: a line each, as **Title**: what it gives me, small enough that one plan of a few cards builds it; the order they'd be built in. " +
                      "No links and no feature plans: I tick features on the Design page, and Darg starts a planner on each and links its plan here.");
        else
        {
            lines.AddRange([
                "   Cards: each one small enough that one agent finishes it in a session, and each scoring 3 of 3 on Darg's ticket score: twenty words or more on what's wanted, " +
                "a **Where:** line naming files or folders, and a **Done when:** line. **Blocked by:** names what has to land first, separated by commas: " +
                "another card in this plan by its title, or a card on a board by its id; none when it can start at once.",
                "3. Look for gaps before you stop: a part of the Design no card owns, or a card whose Done when needs something no earlier card makes. Add the card, or a Question if it's my call.",
            ]);
        }
        lines.AddRange([
            $"{(project ? 3 : 4)}. Read the file back: the properties, a # name line, then the ## parts in the order above. Write no other file and make no cards: " +
            "I tick the cards on the Design page and Darg makes them.",
            commits is null ? $"{(project ? 4 : 5)}. Don't commit the plan." : $"{(project ? 4 : 5)}. {commits} Commit only the plan file, nothing else that's changed there.",
            "",
            "When you stop, say in two lines what the plan proposes and how many questions wait on me. The Design page opens the plan when your turn ends.",
            "",
            "Afterwards I'll talk to you from beside the plan's page. When I ask you to change one part (Goals, Not doing, Questions, Design, " + (project ? "Features" : "Cards") +
            "), change only that part's lines in the file and leave every other line as it is, my answers to Questions included. If the change needs another part to change too, " +
            "say so and ask rather than doing it.",
        ]);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>What's sent to a plan's planner from the chat beside the page: Philip's words, the plan file, and,
    /// when a part is picked, that only that part changes (which <see cref="KeepOnly"/> then holds it to).</summary>
    public static string ChangeMessage(string file, string? part, string text)
    {
        var lines = new List<string> { text.Trim(), "", "Plan file: " + file };
        if (!string.IsNullOrWhiteSpace(part))
            lines.Add($"Change only its {part} part, and leave every other line of the file as it is. Darg puts back anything changed outside {part}.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>For a plan with no planner (one written by hand, or whose agent was forgotten): a new agent reads the
    /// plan and does what Philip asked of it.</summary>
    public static string ReviseFirst(string projectName, string file, string? commits) => string.Join(Environment.NewLine,
    [
        $"You're the planner for a plan of the {projectName} project, already written: {file}. Read it, the project's CLAUDE.md and what the plan points to, " +
        "then do what I ask below. Its parts are Goals, Not doing, Questions (each a ### question with two to four options, one (recommended), and my **Answer:** line once I've answered), " +
        "Design, and Cards (### title, a description, **Where:**, **Done when:**, **Blocked by:**) or Features. Calls only I can make go into Questions rather than being settled quietly. " +
        "Write only the plan file and make no cards. " + (commits is null ? "Don't commit it." : commits + " Commit only the plan file."),
        "When I ask you to change one part, change only that part's lines and leave every other line as it is.",
        "",
    ]);

    /// <summary>The body of a <c>##</c> part, as written: every line under its heading up to the next <c>##</c>; "" without one.</summary>
    public static string Part(string text, string part)
    {
        foreach (var (heading, body) in Parts(text))
            if (heading.Equals(part, StringComparison.OrdinalIgnoreCase)) return body.Trim('\n');
        return "";
    }

    /// <summary>The planner was asked to change <paramref name="part"/> only: whatever else in the file differs from
    /// <paramref name="before"/> (another part, the title, the properties) is put back, keeping its new
    /// <paramref name="part"/>. Returns what was put back ("Design", "the title and properties"); empty when it kept to
    /// the part. Asked by the planner's prompt and its message, held here: a model told "only that part" still tidies
    /// next door now and then.</summary>
    public static IReadOnlyList<string> KeepOnly(string file, string before, string part)
    {
        if (!File.Exists(file)) return [];
        var after = File.ReadAllText(file);
        var changed = Changed(before, after, part);
        if (changed.Count == 0) return [];
        var body = Part(after, part);
        var crlf = after.Contains("\r\n");
        File.WriteAllText(file, before, new UTF8Encoding(false));
        PlanStore.SetSection(file, part, body);
        // the file's line endings as the planner left them, for git's sake
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        File.WriteAllText(file, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
        return changed;
    }

    /// <summary>What differs between two versions of a plan outside <paramref name="part"/>, by part name.</summary>
    public static IReadOnlyList<string> Changed(string before, string after, string part) =>
        Differing(before, after, part).Select(n => n.Length == 0 ? "the title and properties" : n).ToList();

    /// <summary>The headings of the parts that differ between two versions ("" for what comes before any), leaving out
    /// <paramref name="except"/>.</summary>
    static List<string> Differing(string before, string after, string? except = null)
    {
        var a = Parts(before).Where(p => except is null || !p.Heading.Equals(except, StringComparison.OrdinalIgnoreCase)).ToList();
        var b = Parts(after).Where(p => except is null || !p.Heading.Equals(except, StringComparison.OrdinalIgnoreCase)).ToList();
        var names = a.Select(p => p.Heading).Concat(b.Select(p => p.Heading)).Distinct(StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        foreach (var n in names)
        {
            var x = a.FirstOrDefault(p => p.Heading.Equals(n, StringComparison.OrdinalIgnoreCase)).Body;
            var y = b.FirstOrDefault(p => p.Heading.Equals(n, StringComparison.OrdinalIgnoreCase)).Body;
            if (Norm(x ?? "\u0000") != Norm(y ?? "\u0000")) changed.Add(n);
        }
        return changed;
    }

    static string Norm(string s) => string.Join("\n", s.Split('\n').Select(l => l.TrimEnd())).Trim('\n');

    /// <summary><paramref name="basis"/> with every part that a write from <paramref name="pre"/> to
    /// <paramref name="post"/> changed taken as <paramref name="post"/> has it (added where post has it, dropped if post
    /// dropped it), and every other part left as <paramref name="basis"/> has it. How a write made during a planner's
    /// one-part turn (an answer, Make cards' ids, an edit on the page) joins what the turn is compared with, so
    /// <see cref="KeepOnly"/> leaves it standing.</summary>
    public static string Rebase(string basis, string pre, string post)
    {
        var changed = Differing(pre, post);
        if (changed.Count == 0) return basis;
        static bool Is(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
        var from = Blocks(post);
        var result = new List<(string Heading, string Text)>();
        foreach (var block in Blocks(basis))
        {
            if (!changed.Any(c => Is(c, block.Heading))) { result.Add(block); continue; }
            if (result.Any(r => Is(r.Heading, block.Heading))) continue;   // a heading written twice: taken once, below
            var now = from.FirstOrDefault(p => Is(p.Heading, block.Heading));
            if (now.Text is not null) result.Add(now);
        }
        // parts the write added: after the part post has before them, else at the end
        for (var i = 0; i < from.Count; i++)
        {
            if (result.Any(r => Is(r.Heading, from[i].Heading))) continue;
            if (!changed.Any(c => Is(c, from[i].Heading))) continue;
            var prev = i == 0 ? -1 : result.FindIndex(r => Is(r.Heading, from[i - 1].Heading));
            result.Insert(prev < 0 ? result.Count : prev + 1, from[i]);
        }
        return string.Join("\n", result.Select(r => r.Text));
    }

    /// <summary>A plan's text cut at its <c>##</c> headings: first what comes before any (properties, title, intro)
    /// under "", then each part.</summary>
    static List<(string Heading, string Body)> Parts(string text)
    {
        var parts = new List<(string Heading, List<string> Lines)> { ("", []) };
        foreach (var l in text.Replace("\r\n", "\n").Split('\n'))
            if (H2().Match(l) is { Success: true } m) parts.Add((m.Groups[1].Value.Trim(), []));
            else parts[^1].Lines.Add(l);
        return parts.Select(p => (p.Heading, string.Join("\n", p.Lines))).ToList();
    }

    /// <summary>As <see cref="Parts"/>, but each with its own lines as written, heading line included, so joined with
    /// "\n" they give the text back (line endings made "\n").</summary>
    static List<(string Heading, string Text)> Blocks(string text)
    {
        var blocks = new List<(string Heading, List<string> Lines)> { ("", []) };
        foreach (var l in text.Replace("\r\n", "\n").Split('\n'))
            if (H2().Match(l) is { Success: true } m) blocks.Add((m.Groups[1].Value.Trim(), [l]));
            else blocks[^1].Lines.Add(l);
        return blocks.Select(b => (b.Heading, string.Join("\n", b.Lines))).ToList();
    }

    [GeneratedRegex(@"[a-z0-9]+")] private static partial Regex Word();
    [GeneratedRegex(@"^##[ \t]+(.+?)\s*#*\s*$")] private static partial Regex H2();
}

/// <summary>A planner's turn held to one part of its plan: made as the message goes, finished when the turn ends.
/// What Darg writes to the plan in between (an answer tapped on the page, Keep, a part edited, Make cards' ids and
/// status, a feature's link: every <see cref="PlanStore.Edited"/>) is the person's, so it joins the text the turn is
/// compared with and stands; only what else differs at the end is the planner's, and that is put back (WOR-0176,
/// QA round 1: the page sits beside the chat, and a turn takes minutes).</summary>
public sealed class PlanHold : IDisposable
{
    readonly string _file;
    readonly object _gate = new();
    string _basis;
    bool _done;

    public string File { get; }
    public string Part { get; }

    PlanHold(string file, string part, string text)
    {
        File = file;
        Part = part;
        _file = Path.GetFullPath(file);
        _basis = text;
        PlanStore.Edited += Wrote;
    }

    /// <summary>Hold the planner's turn on <paramref name="file"/> to <paramref name="part"/>; null when there's no file yet.</summary>
    public static PlanHold? Start(string file, string part) =>
        System.IO.File.Exists(file) ? new PlanHold(file, part, System.IO.File.ReadAllText(file)) : null;

    void Wrote(string file, string pre, string post)
    {
        if (!string.Equals(Path.GetFullPath(file), _file, StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate) if (!_done) _basis = Planner.Rebase(_basis, pre, post);
    }

    /// <summary>The turn is over: put back whatever else the planner changed (<see cref="Planner.KeepOnly"/>), and say what.</summary>
    public IReadOnlyList<string> Finish()
    {
        string basis;
        lock (_gate) { _done = true; basis = _basis; }
        Dispose();
        return Planner.KeepOnly(File, basis, Part);
    }

    public void Dispose() => PlanStore.Edited -= Wrote;
}
