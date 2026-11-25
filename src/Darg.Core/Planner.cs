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
                "another card in this plan, or in another feature plan of the same project, by its heading copied exactly as written there (commas in it are fine), " +
                "or a card on a board by its id; none when it can start at once. Only if two plans share that heading, write it as Plan title: heading. " +
                "A wait on a card in another plan goes in this line, never in prose elsewhere: Darg makes the cards across plans in that order.",
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
            "say so and ask rather than doing it. " + KeepProperties,
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
        KeepProperties,
        "",
    ]);

    /// <summary>The Design page keeps its own state in a plan's properties (WOR-0199): a planner leaves them be.</summary>
    public const string KeepProperties = "Leave the planner, unticked and unticked-features properties as they are: Darg writes them (this chat, and the rows I unticked on the Design page).";

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

    /// <summary><paramref name="basis"/> with what a write from <paramref name="pre"/> to <paramref name="post"/>
    /// changed applied to it, and nothing else. Within a part the write changed, only the write's own lines are taken
    /// (<see cref="Merge3"/>: <paramref name="pre"/> already holds the planner's changes so far, which stay out); a part
    /// the write added goes in where post has it, one it dropped goes. Every other part stays as
    /// <paramref name="basis"/> has it. How a write made during a planner's one-part turn (an answer, Make cards' ids,
    /// an edit on the page) joins what the turn is compared with, so <see cref="KeepOnly"/> leaves it standing and still
    /// puts back what the planner changed beside it in the same part (WOR-0176, QA round 2).</summary>
    public static string Rebase(string basis, string pre, string post)
    {
        var changed = Differing(pre, post);
        if (changed.Count == 0) return basis;
        static bool Is(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
        var from = Blocks(post);
        var was = Blocks(pre);
        var result = new List<(string Heading, string Text)>();
        foreach (var block in Blocks(basis))
        {
            if (!changed.Any(c => Is(c, block.Heading))) { result.Add(block); continue; }
            if (result.Any(r => Is(r.Heading, block.Heading))) continue;   // a heading written twice: taken once, below
            var now = from.FirstOrDefault(p => Is(p.Heading, block.Heading));
            if (now.Text is null) continue;
            var then = was.FirstOrDefault(p => Is(p.Heading, block.Heading));
            result.Add(then.Text is null ? now : (block.Heading, Merge3(then.Text, block.Text, now.Text)));
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

    /// <summary>A three-way merge by lines: <paramref name="mine"/> with the change from <paramref name="pre"/> to
    /// <paramref name="post"/> applied, as diff3 does. Where both changed the same lines (overlapping), <paramref name="post"/>'s change
    /// wins: it's the person's write, so it must stand. A write that only touches a changed stretch (an answer line put
    /// where the planner's lines were, an id in the heading above a line the planner reworded) applies as well, so the
    /// planner's change beside it is still undone (WOR-0191).</summary>
    public static string Merge3(string pre, string mine, string post)
    {
        if (pre == post) return mine;
        if (pre == mine) return post;
        // lines compared without their \r, so a planner that rewrote the file with other line endings doesn't make
        // every line differ; the result takes mine's
        var crlf = mine.Contains("\r\n");
        var o = pre.Replace("\r\n", "\n").Split('\n');
        var a = mine.Replace("\r\n", "\n").Split('\n');
        var b = post.Replace("\r\n", "\n").Split('\n');
        var hunks = Hunks(o, a, Match(o, a)).Select(h => (H: h, Mine: true)).ToList();
        foreach (var h in Hunks(o, b, Match(o, b)))
            // the same line put in at the same spot by both is put in once
            if (!(h.Os == h.Oe && hunks.Any(x => x.H.Os == h.Os && x.H.Oe == h.Oe && x.H.New.SequenceEqual(h.New))))
                hunks.Add((h, false));
        hunks = hunks.OrderBy(x => x.H.Os).ThenBy(x => x.H.Oe).ThenBy(x => x.Mine ? 0 : 1).ToList();
        var result = new List<string>();
        var pos = 0;
        var cluster = new List<(Hunk H, bool Mine)>();
        void Flush()
        {
            if (cluster.Count == 0) return;
            var s = cluster.Min(x => x.H.Os);
            var e = cluster.Max(x => x.H.Oe);
            result.AddRange(o[pos..s]);
            if (cluster.All(x => x.Mine == cluster[0].Mine))
                foreach (var x in cluster) result.AddRange(x.H.New);
            else
            {
                // both changed the same lines (overlapping, not merely touching): the person's write stands
                var at = s;
                foreach (var x in cluster.Where(x => !x.Mine))
                {
                    result.AddRange(o[at..x.H.Os]);
                    result.AddRange(x.H.New);
                    at = x.H.Oe;
                }
                result.AddRange(o[at..e]);
            }
            pos = e;
            cluster.Clear();
        }
        foreach (var x in hunks)
        {
            if (cluster.Count > 0 && !cluster.Any(c => Overlap(c.H, x.H))) Flush();
            cluster.Add(x);
        }
        Flush();
        result.AddRange(o[pos..]);
        return string.Join(crlf ? "\r\n" : "\n", result);
    }

    /// <summary>A change from <c>o</c> to another version: lines Os..Oe of o became New (Os == Oe is an insertion).</summary>
    readonly record struct Hunk(int Os, int Oe, string[] New);

    static List<Hunk> Hunks(string[] o, string[] x, int[] m)
    {
        var list = new List<Hunk>();
        int p = 0, q = 0;
        for (var i = 0; i <= o.Length; i++)
        {
            if (i < o.Length && m[i] < 0) continue;
            var qi = i < o.Length ? m[i] : x.Length;
            if (i > p || qi > q) list.Add(new Hunk(p, i, x[q..qi]));
            p = i + 1; q = qi + 1;
        }
        return list;
    }

    /// <summary>Whether two changes collide: their ranges share a line, or one is an insertion strictly inside the
    /// other's range. Touching (one starting where the other ends, an insertion at the edge of a changed stretch, two
    /// insertions at one spot) is not a collision: both apply, mine's first (WOR-0191).</summary>
    static bool Overlap(Hunk h, Hunk g)
    {
        bool hi = h.Os == h.Oe, gi = g.Os == g.Oe;
        if (hi && gi) return false;
        if (hi) return g.Os < h.Os && h.Os < g.Oe;
        if (gi) return h.Os < g.Os && g.Os < h.Oe;
        return h.Os < g.Oe && g.Os < h.Oe;
    }

    /// <summary>For each line of <paramref name="x"/>, the index of the line of <paramref name="y"/> it's matched to in
    /// their longest common subsequence, or -1.</summary>
    static int[] Match(string[] x, string[] y)
    {
        var len = new int[x.Length + 1, y.Length + 1];
        for (var p = x.Length - 1; p >= 0; p--)
            for (var q = y.Length - 1; q >= 0; q--)
                len[p, q] = x[p] == y[q] ? len[p + 1, q + 1] + 1 : Math.Max(len[p + 1, q], len[p, q + 1]);
        var m = Enumerable.Repeat(-1, x.Length).ToArray();
        for (int p = 0, q = 0; p < x.Length && q < y.Length;)
            if (x[p] == y[q]) { m[p] = q; p++; q++; }
            else if (len[p + 1, q] >= len[p, q + 1]) p++;
            else q++;
        return m;
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
