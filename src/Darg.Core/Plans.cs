using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A choice in a plan only the person can make (WOR-0174): shaped like a card's decision, so it reads with
/// <see cref="CardDecision.Parse"/>. The heading is the question; <see cref="Answer"/> is the text of its
/// <c>**Answer:**</c> line, empty while open.</summary>
public sealed record PlanQuestion(string Question, IReadOnlyList<DecisionOption> Options, string Context, string Answer)
{
    public bool Open => Answer.Length == 0;
    /// <summary>The answer was a card agent's choice, not the person's (WOR-0178): <c>**Answer:** X (decided by agent,
    /// AND-0002, 2026-11-20)</c>, which <see cref="PlanRules.Prompt"/> tells the agent to write.</summary>
    public bool ByAgent => PlanRules.DecidedBy(Answer) is not null;
    /// <summary>The card whose agent chose, when the answer names it.</summary>
    public string? DecidedCard => PlanRules.DecidedBy(Answer)?.Card;
    /// <summary>The answer without its trailing <c>(by, date)</c> note.</summary>
    public string Pick => PlanRules.Pick(Answer);
}

/// <summary>What a card agent is told about the plan its card came from, and how a choice it made is read back
/// (WOR-0178, docs/FEATURE-PLANNING.md "Keeping it current"): the plan holds intent, the board holds state, and only a
/// change of intent (a choice the plan didn't settle) is written to the plan.</summary>
public static partial class PlanRules
{
    public sealed record Decided(string? Card);

    /// <summary>The plan holding a made card, if any plan has it.</summary>
    public static PlanDoc? PlanOf(IEnumerable<PlanDoc> plans, string cardId) =>
        plans.FirstOrDefault(p => p.Cards.Any(c => c.Made && c.Id.Equals(cardId, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Lines for a card agent's prompt: where the plan is and the rule for a choice it settled.</summary>
    public static IReadOnlyList<string> Prompt(string planFile, string cardId)
    {
        // named relative to its repo: a card agent works in a worktree of its own, so the absolute path is the main
        // checkout's copy, and an edit there is missing from the agent's commit
        var rel = RepoRelative(planFile);
        var where = rel is null
            ? planFile.Replace(Path.DirectorySeparatorChar, '/')
            : rel + " (in your own checkout of the repo, your worktree if you work in one; not the copy in another folder)";
        return
        [
            $"This card was made from the plan {where}, intent only: the board holds progress, so never write how far along the work is into it.",
            "If you make a choice the plan did not settle (a tool, a name, an id, an approach that had other options), add it to the plan's ## Questions as a ### heading " +
            "with the options you weighed and the line `**Answer:** <your pick> (decided by agent, " + cardId + ", yyyy-MM-dd)`, in the same commit as the work. " +
            "A question the plan already answers, or a detail nobody would weigh in on, doesn't go there. If the choice is mine to make rather than yours, ask with AskUserQuestion instead.",
        ];
    }

    /// <summary>The path from the repo holding the file (nearest folder above with a .git), with / as separator.</summary>
    internal static string? RepoRelative(string file)
    {
        if (string.IsNullOrEmpty(file) || !Path.IsPathRooted(file)) return null;
        for (var d = Path.GetDirectoryName(Path.GetFullPath(file)); d is not null; d = Path.GetDirectoryName(d))
            if (Directory.Exists(Path.Combine(d, ".git")) || System.IO.File.Exists(Path.Combine(d, ".git")))
                return Path.GetRelativePath(d, file).Replace(Path.DirectorySeparatorChar, '/');
        return null;
    }

    /// <summary>Set when the text records an agent's choice. Agents drift from the shape they're told (the real plan's
    /// lines read "agent for AND-0002 ... on Philip's behalf"), so any of these in the trailing (note) counts, or the
    /// text itself when it has no note: "decided by agent", "decided by an agent", "decided by AND-0002's agent",
    /// "agent for / by AND-0002", "chosen by an agent", "AND-0002 agent", "on Philip's behalf". The card is the first id in it.</summary>
    public static Decided? DecidedBy(string answer)
    {
        var note = NoteRx().Match(answer) is { Success: true } n ? n.Value : answer;
        if (!AgentRx().IsMatch(note)) return null;
        return new Decided(IdRx().Match(note) is { Success: true } m ? m.Value.ToUpperInvariant() : null);
    }

    public static string Pick(string answer) => NoteRx().Replace(answer, "").Trim();

    [GeneratedRegex(@"\bagents?\b|\bon\s+(?:philip'?s|his|my|your)\s+behalf\b", RegexOptions.IgnoreCase)] private static partial Regex AgentRx();
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9]*-\d+\b")] private static partial Regex IdRx();
    [GeneratedRegex(@"\s*\([^()]*\)\s*$")] private static partial Regex NoteRx();
}

/// <summary>A card the plan proposes: a <c>###</c> heading (with its id once made), a description, and the Where,
/// Done when and Blocked by lines. <see cref="Score"/> is the ticket's quality as it would be made, so the page and
/// Make cards can refuse one under <see cref="TicketScore.Max"/>.</summary>
public sealed record PlanCard(string Id, string Title, string Description, string Where, string Done, IReadOnlyList<string> BlockedBy)
{
    public bool Made => Id.Length > 0;
    /// <summary>What the card's text would score, worked out as the board does (description, Where and Done when).</summary>
    public TicketScore Score => TicketQuality.Score(Ticket);
    public bool Ready => Score.Green;
    /// <summary>The text a made card's description would hold.</summary>
    public string Ticket =>
        Description + (Where.Length > 0 ? "\n\n**Where:** " + Where : "") + (Done.Length > 0 ? "\n\n**Done when:** " + Done : "");
    /// <summary>How the card is named in another card's Blocked by: its id once made, else its heading.</summary>
    public string Ref => Made ? Id : Title;
}

/// <summary>A feature a project plan lists: a line (or heading) with its feature plan linked once there is one.</summary>
public sealed record PlanFeature(string Title, string Description, string Link)
{
    /// <summary>What the planner is given to draft the feature's plan from (WOR-0179): its title, then its line.</summary>
    public string Idea => Description.Length > 0 ? $"{Title}: {Description}" : Title;
    /// <summary>The plan file the link names (its file name without .md), or "" with no link to a markdown file here.</summary>
    public string LinkSlug
    {
        get
        {
            var target = Link.Split('#')[0].Trim();
            if (target.Length == 0 || target.Contains("://")) return "";
            return target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(target.Replace('\\', '/').Split('/')[^1]) : "";
        }
    }
}

/// <summary>A plan file (WOR-0174, docs/FEATURE-PLANNING.md "The plan file"): properties, a title and intro, then Goals,
/// Not doing, Questions, Design and Cards (or Features, for a project). Written by planner agents as plain
/// markdown, so the parser is forgiving as <see cref="CardDecision"/> is: any heading case, bullets or plain lines for
/// the lists, bold or plain labels, a colon or a dash after a card's id.</summary>
public sealed partial record PlanDoc(
    string File, string Kind, string Status, string Board, string Parent, string Title, string Intro,
    IReadOnlyList<string> Goals, IReadOnlyList<string> NotDoing, IReadOnlyList<PlanQuestion> Questions,
    string Design, IReadOnlyList<PlanCard> Cards, IReadOnlyList<PlanFeature> Features)
{
    public bool IsProject => Kind.Equals("project", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<PlanQuestion> OpenQuestions => Questions.Where(q => q.Open).ToList();
    /// <summary>The cards not yet made that score under 3, so can't be made yet.</summary>
    public IReadOnlyList<PlanCard> NotReady => Cards.Where(c => !c.Made && !c.Ready).ToList();
    /// <summary>What names this plan in another's <c>parent</c>: its file name without .md.</summary>
    public string Slug => string.IsNullOrEmpty(File) ? "" : Path.GetFileNameWithoutExtension(File);

    /// <summary>The card a Blocked by entry names: by id, else by heading (case aside); null when it names none.</summary>
    public PlanCard? Find(string reference) =>
        Cards.FirstOrDefault(c => c.Made && c.Id.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? Cards.FirstOrDefault(c => c.Title.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Blocked by entries that name no card in this plan and aren't shaped like a card id (which may be a
    /// card on a board): a typo, shown as a warning.</summary>
    public IReadOnlyList<string> UnknownBlockers() =>
        Cards.SelectMany(c => c.BlockedBy).Where(r => Find(r) is null && !IdRx().IsMatch(r)).Distinct().ToList();

    public static PlanDoc Parse(string text, string file = "")
    {
        text = text.Replace("\r\n", "\n");
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n').ToList();
        var body = FrontMatterEnd(lines);
        for (var i = 1; i < body - 1; i++)
            if (PropRx().Match(lines[i]) is { Success: true } p) props[p.Groups[1].Value] = p.Groups[2].Value.Trim().Trim('"', '\'');
        string P(string k) => props.TryGetValue(k, out var v) ? v : "";

        string title = "";
        var intro = new StringBuilder();
        var sections = new List<(string Name, List<string> Lines)>();
        for (var i = body; i < lines.Count; i++)
        {
            var l = lines[i];
            if (sections.Count == 0 && title.Length == 0 && TitleRx().Match(l) is { Success: true } t) { title = t.Groups[1].Value.Trim(); continue; }
            if (H2Rx().Match(l) is { Success: true } h) { sections.Add((h.Groups[1].Value.Trim(), [])); continue; }
            if (sections.Count == 0) intro.Append(l).Append('\n'); else sections[^1].Lines.Add(l);
        }
        List<string> Of(params string[] names) =>
            sections.Where(s => names.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).SelectMany(s => s.Lines).ToList();

        var kind = P("kind");
        var features = Of("Features");
        return new PlanDoc(file, kind.Length > 0 ? kind.ToLowerInvariant() : features.Count > 0 ? "project" : "feature",
            P("status").Length > 0 ? P("status").ToLowerInvariant() : "draft", P("board"), P("parent"), title, intro.ToString().Trim(),
            Items(Of("Goals")), Items(Of("Not doing", "Non-goals", "Out of scope")),
            Subsections(Of("Questions")).Select(s => Question(s.Heading, s.Lines)).ToList(),
            string.Join("\n", Of("Design")).Trim(),
            Subsections(Of("Cards")).Select(s => Card(s.Heading, s.Lines)).ToList(),
            ReadFeatures(features));
    }

    public static PlanDoc Load(string file) => Parse(System.IO.File.ReadAllText(file), file);

    /// <summary>First line after the closing <c>---</c> of the properties, or 0 when there are none.</summary>
    internal static int FrontMatterEnd(List<string> lines)
    {
        if (lines.Count == 0 || lines[0].Trim() != "---") return 0;
        for (var i = 1; i < lines.Count; i++) if (lines[i].Trim() == "---") return i + 1;
        return 0;
    }

    static List<string> Items(List<string> lines) =>
        lines.Select(l => BulletRx().Replace(l, "").Trim()).Where(l => l.Length > 0).ToList();

    internal static List<(string Heading, List<string> Lines)> Subsections(List<string> lines)
    {
        var result = new List<(string, List<string>)>();
        foreach (var l in lines)
        {
            if (H3Rx().Match(l) is { Success: true } h) result.Add((h.Groups[1].Value.Trim().Trim('*').Trim(), []));
            else if (result.Count > 0) result[^1].Item2.Add(l);
        }
        return result;
    }

    static PlanQuestion Question(string heading, List<string> lines)
    {
        string answer = "";
        var rest = new List<string>();
        foreach (var l in lines)
            if (AnswerRx().Match(l) is { Success: true } a) answer = a.Groups[1].Value.Trim();
            else rest.Add(l);
        var d = CardDecision.Parse("**Question:** " + heading + "\n" + string.Join("\n", rest));
        return new PlanQuestion(heading, d.Options, d.Context, answer);
    }

    static readonly string[] Labels = ["Where", "Done when", "Blocked by"];

    static PlanCard Card(string heading, List<string> lines)
    {
        var id = "";
        var title = heading;
        if (IdHeadingRx().Match(heading) is { Success: true } m) { id = m.Groups[1].Value.ToUpperInvariant(); title = m.Groups[2].Value.Trim(); }
        var desc = new List<string>();
        var fields = Labels.ToDictionary(l => l, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        foreach (var l in lines)
        {
            if (LabelRx().Match(l) is { Success: true } f
                && Labels.FirstOrDefault(x => x.Equals(Regex.Replace(f.Groups[1].Value.Trim(), @"\s+", " "), StringComparison.OrdinalIgnoreCase)) is { } name)
            {
                current = fields[name];
                if (f.Groups[2].Value.Trim().Length > 0) current.Add(f.Groups[2].Value.Trim());
            }
            else if (l.Trim().Length == 0) current = null;
            else (current ?? desc).Add(l.Trim());
        }
        var blocked = string.Join(",", fields["Blocked by"]).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(s => AndRx().Split(s)).Select(s => s.Trim().Trim('.', '`', '*').Trim())
            .Where(s => s.Length > 0 && !NoneRx().IsMatch(s)).ToList();
        return new PlanCard(id, title, string.Join("\n", desc).Trim(), string.Join(" ", fields["Where"]), string.Join(" ", fields["Done when"]), blocked);
    }

    /// <summary>A project's features: a <c>###</c> heading each, else a bullet each.</summary>
    static List<PlanFeature> ReadFeatures(List<string> lines)
    {
        var headed = Subsections(lines);
        if (headed.Count > 0)
            return headed.Select(s => Feature(s.Heading, string.Join(" ", s.Lines.Select(b => b.Trim()).Where(b => b.Length > 0)))).ToList();
        return lines.Where(l => BulletRx().IsMatch(l)).Select(l => Feature(BulletRx().Replace(l, "").Trim(), "")).ToList();
    }

    static PlanFeature Feature(string text, string description)
    {
        var (head, rest) = SplitFeature(text);
        // only a link in the title is the feature's plan; one in its line (a doc it points to) stays in the description
        var link = "";
        if (LinkRx().Match(head) is { Success: true } m) { link = m.Groups[2].Value; head = head.Replace(m.Value, m.Groups[1].Value); }
        return new PlanFeature(head.Trim().Trim('*').Trim(), (rest.Trim() + " " + description).Trim(), link);
    }

    /// <summary>A feature's line cut into its title and the rest at the first colon or spaced dash outside a link:
    /// (<c>**[Android](android.md)**</c>, <c>the phone</c>); the rest is "" with no cut.</summary>
    internal static (string Head, string After) SplitFeature(string text)
    {
        var masked = LinkRx().Replace(text, m => new string('\u0001', m.Length));
        var cut = Regex.Match(masked, @"\s*(?::|\s[–—]\s|\s-\s)\s*");
        return cut.Success ? (text[..cut.Index], text[(cut.Index + cut.Length)..]) : (text, "");
    }

    [GeneratedRegex(@"^([A-Za-z_][\w \-]*?)\s*:\s*(.*)$")] private static partial Regex PropRx();
    [GeneratedRegex(@"^#[ \t]+(.+?)\s*$")] private static partial Regex TitleRx();
    [GeneratedRegex(@"^##[ \t]+(.+?)\s*#*\s*$")] private static partial Regex H2Rx();
    [GeneratedRegex(@"^###[ \t]+(.+?)\s*#*\s*$")] private static partial Regex H3Rx();
    [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+(?:\[[ xX]?\]\s*)?")] private static partial Regex BulletRx();
    [GeneratedRegex(@"^\s*(?:[-*+]\s+)?\**\s*Answer(?:ed)?\s*\**\s*:\s*\**\s*(.*)$", RegexOptions.IgnoreCase)] private static partial Regex AnswerRx();
    [GeneratedRegex(@"^\s*(?:[-*+]\s+)?(?:\*\*|__)?\s*([A-Za-z][A-Za-z ]{2,12}?)\s*(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*(.*)$")] private static partial Regex LabelRx();
    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9]*-\d+)\s*(?:[:\-–—]\s*)?(.*)$")] private static partial Regex IdHeadingRx();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*-\d+$")] private static partial Regex IdRx();
    [GeneratedRegex(@"\s+and\s+", RegexOptions.IgnoreCase)] private static partial Regex AndRx();
    [GeneratedRegex(@"^(none|nothing|n/a|-|–|—)\.?$", RegexOptions.IgnoreCase)] private static partial Regex NoneRx();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")] private static partial Regex LinkRx();
}

/// <summary>A project's plan files (WOR-0174): <c>design/</c> in its docs folder, beside Stories/.</summary>
public static class PlanStore
{
    public static string? FolderFor(ProjectConfig project) =>
        string.IsNullOrWhiteSpace(project.Docs) ? null : Path.Combine(project.Docs, "design");

    /// <summary>Every plan in the folder, by file name; a file that won't read is left out.</summary>
    public static List<PlanDoc> List(string? folder)
    {
        var plans = new List<PlanDoc>();
        if (folder is null || !Directory.Exists(folder)) return plans;
        foreach (var f in Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            try { plans.Add(PlanDoc.Load(f)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return plans;
    }

    public sealed record Node(PlanDoc Plan, List<Node> Children);

    /// <summary>The plans as a tree by <c>parent</c> (a plan's file name or title, case aside). A plan with no
    /// <c>parent</c> sits under a project plan whose Features link to it (WOR-0179: a feature plan written by hand
    /// still lands under its project). A plan whose parent names none here, or itself, or a loop, is a root.</summary>
    public static List<Node> Tree(IReadOnlyList<PlanDoc> plans)
    {
        var nodes = plans.Select(p => new Node(p, [])).ToList();
        Node? ParentOf(Node n)
        {
            var p = Direct(n);
            // follow direct parents upward: arriving back at n (or never ending) is a loop, so n is a root
            var up = p;
            for (var steps = 0; up is not null; up = Direct(up), steps++)
                if (up == n || steps > nodes.Count) return null;
            return p;
        }
        Node? Direct(Node n) =>
            n.Plan.Parent.Length > 0
                ? nodes.FirstOrDefault(x => x != n && Names(x.Plan, n.Plan.Parent))
                : nodes.FirstOrDefault(x => x != n && x.Plan.IsProject && x.Plan.Features.Any(f => f.LinkSlug.Equals(n.Plan.Slug, StringComparison.OrdinalIgnoreCase)));
        var roots = new List<Node>();
        foreach (var n in nodes) (ParentOf(n)?.Children ?? roots).Add(n);
        return roots;
    }

    /// <summary>A plan's <c>parent</c> value names this plan: its file name without .md, or its title (case aside).</summary>
    public static bool Names(PlanDoc plan, string parent) =>
        parent.Length > 0 && (plan.Slug.Equals(parent, StringComparison.OrdinalIgnoreCase) || plan.Title.Equals(parent, StringComparison.OrdinalIgnoreCase));

    /// <summary>A project plan's feature's own plan (WOR-0179): the plan its link names, else one under the project
    /// (its <c>parent</c>) with the feature's title; null when it has none yet.</summary>
    public static PlanDoc? FeaturePlan(PlanDoc project, PlanFeature feature, IReadOnlyList<PlanDoc> plans) =>
        (feature.LinkSlug.Length > 0 ? plans.FirstOrDefault(p => p.Slug.Equals(feature.LinkSlug, StringComparison.OrdinalIgnoreCase)) : null)
        ?? plans.FirstOrDefault(p => p != project && Names(project, p.Parent) && p.Title.Equals(feature.Title, StringComparison.OrdinalIgnoreCase));

    /// <summary>The features of a project plan still without a plan of their own (WOR-0179), so Make features can start a
    /// planner on them: none found by <see cref="FeaturePlan"/>, and no planner still drafting the file its link names
    /// (<paramref name="drafting"/>, given that file's name without .md). A link to a file that was never written, with no
    /// planner at it, counts as no plan: pressing again starts another.</summary>
    public static IReadOnlyList<PlanFeature> FeaturesToPlan(PlanDoc project, IReadOnlyList<PlanDoc> plans, Func<string, bool> drafting) =>
        project.Features.Where(f => FeaturePlan(project, f, plans) is null && !(f.LinkSlug.Length > 0 && drafting(f.LinkSlug))).ToList();

    /// <summary>Link a feature's title to its plan file (WOR-0179): <c>- **Android**: …</c> becomes
    /// <c>- **[Android](android.md)**: …</c>, a <c>### Android</c> heading <c>### [Android](android.md)</c>, and a link
    /// already there is replaced. Found by the feature's title; false when the Features part has none.</summary>
    public static bool SetFeatureLink(string file, string title, string link) =>
        Edit(file, lines =>
        {
            var inPart = false;
            for (var i = PlanDoc.FrontMatterEnd(lines); i < lines.Count; i++)
            {
                var l = lines[i];
                if (Regex.Match(l, @"^##[ \t]+(.+?)\s*#*\s*$") is { Success: true } h) { inPart = h.Groups[1].Value.Trim().Equals("Features", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inPart) continue;
                var heading = Regex.Match(l, @"^(###[ \t]+)(.*)$");
                var bullet = heading.Success ? Match.Empty : Regex.Match(l, @"^(\s*(?:[-*+]|\d+[.)])\s+(?:\[[ xX]?\]\s*)?)(.*)$");
                var m = heading.Success ? heading : bullet;
                if (!m.Success) continue;
                var line = m.Groups[2].Value;
                // the title as the parser reads it; what follows it (the description, its own links) stays as written
                var (head, rest) = PlanDoc.SplitFeature(line);
                var name = Regex.Replace(head.Trim().Trim('*').Trim(), @"^\[([^\]]+)\]\([^)]*\)$", "$1");
                if (!name.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                var stars = Regex.Match(head.Trim(), @"^(\**)").Groups[1].Value;
                lines[i] = m.Groups[1].Value + stars + $"[{name}]({link})" + stars + line[head.Length..];
                return true;
            }
            return false;
        });

    // ---- writing one part back: each edits its own lines and leaves every other byte alone ----

    /// <summary>Record an answer under a question: an <c>**Answer:**</c> line at the end of its section, or the
    /// existing one replaced. False when no question has that heading.</summary>
    public static bool Answer(string file, string question, string answer, string by, DateTime when) =>
        Edit(file, lines =>
        {
            var (start, end) = Subsection(lines, "Questions", question);
            if (start < 0) return false;
            var line = $"**Answer:** {answer.Trim()} ({by}, {when:yyyy-MM-dd})";
            var at = -1;
            for (var i = start + 1; i < end && at < 0; i++) if (AnswerLine(lines[i])) at = i;
            if (at >= 0) { lines[at] = line; return true; }
            var last = end;
            while (last > start + 1 && lines[last - 1].Trim().Length == 0) last--;
            lines.Insert(last, line);
            lines.Insert(last, "");
            return true;
        });

    /// <summary>Put the made card's id in its heading (<c>### Title</c> becomes <c>### AND-0010: Title</c>), found by
    /// its current heading text. False when there's no such card.</summary>
    public static bool SetCardId(string file, string heading, string id) =>
        Edit(file, lines =>
        {
            var (start, _) = Subsection(lines, "Cards", heading);
            if (start < 0) return false;
            var title = Regex.Replace(lines[start], @"^###[ \t]+", "").Trim();
            title = Regex.Replace(title, @"^[A-Za-z][A-Za-z0-9]*-\d+\s*[:\-–—]?\s*", "");
            lines[start] = $"### {id}: {title}";
            return true;
        });

    /// <summary>Set a property (status, board, parent, kind), adding the properties block if the file has none.</summary>
    public static bool SetProperty(string file, string key, string value) =>
        Edit(file, lines =>
        {
            var end = PlanDoc.FrontMatterEnd(lines);
            if (end == 0) { lines.InsertRange(0, ["---", $"{key}: {value}", "---"]); return true; }
            for (var i = 1; i < end - 1; i++)
                if (Regex.IsMatch(lines[i], @"^" + Regex.Escape(key) + @"\s*:", RegexOptions.IgnoreCase)) { lines[i] = $"{key}: {value}"; return true; }
            lines.Insert(end - 1, $"{key}: {value}");
            return true;
        });

    /// <summary>The parts a plan has, in the order a file holds them: where <see cref="SetSection"/> puts a missing one.</summary>
    public static readonly IReadOnlyList<string> Parts = ["Goals", "Not doing", "Questions", "Design", "Cards", "Features"];

    /// <summary>Replace the body of a <c>##</c> part (Goals, Not doing, Design: the ones edited as text on the Design
    /// page, WOR-0175) with <paramref name="text"/>, its heading and every other part left as they were. A part the file
    /// lacks is added before the first part that follows it in <see cref="Parts"/>, else at the end.</summary>
    public static bool SetSection(string file, string part, string text) =>
        Edit(file, lines =>
        {
            var body = text.Replace("\r\n", "\n").Trim('\n').Split('\n').Select(l => l.TrimEnd()).ToList();
            var start = PlanDoc.FrontMatterEnd(lines);
            int heading = -1, end = lines.Count;
            for (var i = start; i < lines.Count; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^##[ \t]+\S")) continue;
                if (heading >= 0) { end = i; break; }
                if (Regex.IsMatch(lines[i], @"^##[ \t]+" + Regex.Escape(part) + @"\s*#*\s*$", RegexOptions.IgnoreCase)) heading = i;
            }
            var block = new List<string>();
            if (body.Any(l => l.Length > 0)) block.AddRange(body);
            if (heading >= 0)
            {
                // the body, then one blank line before the next part (none at the end of the file)
                var next = end < lines.Count ? new List<string> { "" } : [];
                if (end == lines.Count && lines.Count > 0 && lines[^1].Length == 0) next.Add("");
                lines.RemoveRange(heading + 1, end - heading - 1);
                lines.InsertRange(heading + 1, block.Count > 0 ? [.. block, .. next] : next);
                return true;
            }
            var later = Parts.SkipWhile(p => !p.Equals(part, StringComparison.OrdinalIgnoreCase)).Skip(1).ToList();
            var at = lines.Count;
            for (var i = start; i < lines.Count; i++)
                if (Regex.Match(lines[i], @"^##[ \t]+(.+?)\s*#*\s*$") is { Success: true } m && later.Contains(m.Groups[1].Value.Trim(), StringComparer.OrdinalIgnoreCase)) { at = i; break; }
            var insert = new List<string> { $"## {part}" };
            insert.AddRange(block);
            if (at < lines.Count) { insert.Add(""); lines.InsertRange(at, insert); return true; }
            // at the end: after one blank line, keeping a final newline if the file had one
            var finalNewline = lines.Count > 0 && lines[^1].Length == 0;
            if (finalNewline) lines.RemoveAt(lines.Count - 1);
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > 0) lines.Add("");
            lines.AddRange(insert);
            if (finalNewline) lines.Add("");
            return true;
        });

    /// <summary>What <see cref="MakeCards"/> did, or why it did nothing: <see cref="Problem"/> says what stopped it
    /// (nothing made then), <see cref="NeedsConfirm"/> that open questions want a second press.</summary>
    public sealed record MadeCards(string Board, IReadOnlyList<(string Id, string Title)> Made, string Problem, bool NeedsConfirm,
        IReadOnlyList<string> OpenQuestions, bool CreatedBoard)
    {
        public bool Ok => Problem.Length == 0 && !NeedsConfirm;
    }

    /// <summary>Make a plan's cards on its story board (WOR-0177, docs/FEATURE-PLANNING.md "From plan to board"). The board is
    /// the plan's <c>board:</c>, or one named for the plan, made with the project's columns and a prefix from the plan's name
    /// (AND for Android) when there is none. The rows with no id (all of them, or those whose heading is in
    /// <paramref name="only"/>) are made in Blocked by order, each reference turned into the new id, each id written into
    /// the plan's heading as soon as its card exists, and the plan set to agreed. All or nothing before anything is made:
    /// a row under 3/3 (its missing parts named), an unknown or circular Blocked by, a repeated heading, or open questions
    /// without <paramref name="confirmOpen"/> make nothing. Run again it makes only the rows still without an id; it never
    /// deletes a card.</summary>
    public static MadeCards MakeCards(string file, string boardsFolder, IEnumerable<string>? only = null, bool confirmOpen = false)
    {
        var plan = PlanDoc.Load(file);
        var open = plan.OpenQuestions.Select(q => q.Question).ToList();
        MadeCards Stop(string problem, bool confirm = false) => new(plan.Board, [], problem, confirm, open, false);

        var pick = only?.Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
        if (pick is not null)
            foreach (var o in pick.Where(o => !plan.Cards.Any(c => !c.Made && c.Title.Equals(o, StringComparison.OrdinalIgnoreCase))))
                return Stop($"No card \"{o}\" in the plan without an id.");
        var rows = plan.Cards.Where(c => !c.Made && (pick is null || pick.Any(o => o.Equals(c.Title, StringComparison.OrdinalIgnoreCase)))).ToList();
        if (rows.Count == 0) return Stop("Every card in the plan has been made already.");
        if (plan.Status == "dropped") return Stop("The plan is dropped.");

        var unready = rows.Where(c => !c.Ready).ToList();
        if (unready.Count > 0)
            return Stop("Not made: " + string.Join("; ", unready.Select(c =>
                $"\"{c.Title}\" is {c.Score.Stars}/{TicketScore.Max} and doesn't say {string.Join(" or ", c.Score.Missing)}")) + ". Fix the plan first.");
        var dup = plan.Cards.GroupBy(c => c.Title, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) return Stop($"Two cards in the plan are headed \"{dup.Key}\": give each its own heading.");

        // order: a row after every row it waits on
        var chosen = rows.ToHashSet();
        var ordered = new List<PlanCard>();
        var visiting = new HashSet<PlanCard>();
        string? bad = null;
        void Visit(PlanCard c)
        {
            if (bad is not null || ordered.Contains(c)) return;
            if (!visiting.Add(c)) { bad = $"\"{c.Title}\" waits on itself through a loop of Blocked by."; return; }
            foreach (var r in c.BlockedBy)
            {
                var t = plan.Find(r);
                if (t is null) { if (Blockers.ParseIds(r).Count == 0) bad = $"\"{c.Title}\" is blocked by \"{r}\", which is no card in the plan."; continue; }
                if (t.Made) continue;
                if (!chosen.Contains(t)) { bad = $"\"{c.Title}\" waits on \"{t.Title}\", which isn't being made: include it too."; return; }
                Visit(t);
            }
            visiting.Remove(c);
            if (bad is null) ordered.Add(c);
        }
        foreach (var c in rows) Visit(c);
        if (bad is not null) return Stop(bad);

        if (open.Count > 0 && !confirmOpen) return Stop("", confirm: true);

        // the board
        var name = plan.Board.Length > 0 ? plan.Board : (plan.Title.Length > 0 ? plan.Title : plan.Slug);
        name = Regex.Replace(name, "[\\\\/:*?\"<>|]", " ").Trim().TrimStart('.').Trim();
        if (name.Length == 0) return Stop("The plan has no name to give its board: set board: in its properties.");
        var folder = Path.Combine(boardsFolder, name);
        var created = false;
        if (!File.Exists(Path.Combine(folder, "board.json")))
        {
            if (Directory.Exists(folder)) return Stop($"{folder} exists but isn't a board.");
            var columns = BoardStore.List(boardsFolder).Select(b => BoardStore.Load(Path.Combine(boardsFolder, b)).Columns.Select(c => c.Name).ToList())
                .FirstOrDefault(c => c.Count > 0) ?? [.. BoardStore.DefaultColumns];
            BoardStore.CreateBoard(boardsFolder, name, columns, PrefixFor(plan.Title.Length > 0 ? plan.Title : name));
            created = true;
        }
        if (!plan.Board.Equals(name, StringComparison.Ordinal)) SetProperty(file, "board", name);

        var ids = new Dictionary<PlanCard, string>();
        var made = new List<(string, string)>();
        foreach (var c in ordered)
        {
            var card = BoardStore.Create(folder, c.Title, c.Ticket, null, "", CardTypes.Guess(c.Title + "\n" + c.Description, []));
            ids[c] = card.Id;
            SetCardId(file, c.Title, card.Id);   // at once, so a failure further on leaves the plan knowing what exists
            made.Add((card.Id, c.Title));
            var waits = c.BlockedBy.Select(r => plan.Find(r) is { } t ? (t.Made ? t.Id : ids[t]) : r).ToList();
            if (waits.Count > 0) BoardStore.SetBlockedBy(folder, card.Id, waits);
        }
        if (plan.Status != "agreed") SetProperty(file, "status", "agreed");
        return new MadeCards(name, made, "", false, open, created);
    }

    /// <summary>A board prefix from a plan's name: its first three letters upper-cased (Android is AND); T with none.</summary>
    public static string PrefixFor(string name)
    {
        var letters = new string(name.Where(char.IsAsciiLetter).Take(3).ToArray()).ToUpperInvariant();
        return letters.Length == 0 ? "T" : letters;
    }

    static bool AnswerLine(string l) => Regex.IsMatch(l, @"^\s*(?:[-*+]\s+)?\**\s*Answer(?:ed)?\s*\**\s*:", RegexOptions.IgnoreCase);

    /// <summary>The line range of a <c>###</c> section inside a <c>##</c> one: its heading line and the line after its
    /// last. A heading matches whole or by its text after a card id; (-1, n) when there's none.</summary>
    static (int Start, int End) Subsection(List<string> lines, string part, string heading)
    {
        var inPart = false;
        var start = -1;
        for (var i = PlanDoc.FrontMatterEnd(lines); i < lines.Count; i++)
        {
            var l = lines[i];
            if (Regex.IsMatch(l, @"^##[ \t]+\S"))
            {
                if (start >= 0) return (start, i);
                inPart = Regex.IsMatch(l, @"^##[ \t]+" + Regex.Escape(part) + @"\s*$", RegexOptions.IgnoreCase);
                continue;
            }
            if (!inPart) continue;
            if (Regex.Match(l, @"^###[ \t]+(.+?)\s*$") is { Success: true } m)
            {
                if (start >= 0) return (start, i);
                var h = m.Groups[1].Value.Trim().Trim('*').Trim();
                if (h.Equals(heading.Trim(), StringComparison.OrdinalIgnoreCase)
                    || Regex.Replace(h, @"^[A-Za-z][A-Za-z0-9]*-\d+\s*[:\-–—]?\s*", "").Equals(heading.Trim(), StringComparison.OrdinalIgnoreCase)) start = i;
            }
        }
        return start >= 0 ? (start, lines.Count) : (-1, lines.Count);
    }

    static bool Edit(string file, Func<List<string>, bool> change)
    {
        var raw = File.ReadAllText(file);
        var crlf = raw.Contains("\r\n");
        var lines = raw.Replace("\r\n", "\n").Split('\n').ToList();
        if (!change(lines)) return false;
        var text = string.Join("\n", lines);
        File.WriteAllText(file, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
        return true;
    }
}
