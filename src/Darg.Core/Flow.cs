using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>One role in a flow: its name and its brief. A checking stage (QA) ends with a verdict, and on
/// a fail the flow goes back to <see cref="SendsBackTo"/> with its report.</summary>
public sealed record FlowStage(string Role, string Brief, string? SendsBackTo = null)
{
    public bool Checks => SendsBackTo is not null;
}

/// <summary>A multi-agent flow: stages run one after another, each its own agent, each handed the task and
/// what the stages before it reported. Kept as a markdown file, one per flow, in the instance's flows folder:
/// <code>
/// # Designer, engineer, QA
/// What it's for, in a line.
/// Max rounds: 2
///
/// ## Designer
/// The brief…
///
/// ## QA
/// Sends back to: Engineer
/// The brief…
/// </code></summary>
public sealed record Flow(string Name, string Description, IReadOnlyList<FlowStage> Stages, int MaxRounds, string? File = null)
{
    public override string ToString() => Name;
}

public enum Verdict { None, Pass, Fail }

public static partial class FlowStore
{
    public static string Folder(string? instanceFolder = null) => Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "flows");

    /// <summary>The flows on file, by name. The first time (no folder yet) the starter flow is written there,
    /// so there is always one to run and one to copy.</summary>
    public static List<Flow> List(string? folder = null)
    {
        folder ??= Folder();
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "designer-engineer-qa.md"), Starter);
        }
        var flows = new List<Flow>();
        foreach (var f in Directory.GetFiles(folder, "*.md"))
            try { if (Parse(File.ReadAllText(f), f) is { } flow) flows.Add(flow); }
            catch (IOException) { }
        return flows.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A flow from its file's text; null when it has no stages.</summary>
    public static Flow? Parse(string text, string? file = null)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string name = file is null ? "Flow" : Path.GetFileNameWithoutExtension(file);
        var description = new List<string>();
        var rounds = 2;
        var stages = new List<FlowStage>();
        string? role = null, back = null;
        var brief = new List<string>();

        void EndStage()
        {
            if (role is not null) stages.Add(new FlowStage(role, string.Join("\n", brief).Trim(), back));
            role = null; back = null; brief.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## ")) { EndStage(); role = line[3..].Trim(); continue; }
            if (role is null)
            {
                if (line.StartsWith("# ")) name = line[2..].Trim();
                else if (RoundsLine().Match(line) is { Success: true } m) rounds = int.Parse(m.Groups[1].Value);
                else if (line.Trim().Length > 0) description.Add(line.Trim());
                continue;
            }
            if (back is null && brief.Count(b => b.Trim().Length > 0) == 0 && BackLine().Match(line) is { Success: true } b2)
                back = b2.Groups[1].Value.Trim();
            else brief.Add(line);
        }
        EndStage();
        if (stages.Count == 0) return null;
        // a stage can only send back to a stage before it; anything else is ignored rather than looping forever
        stages = stages.Select((s, i) => s.Checks && !stages.Take(i).Any(p => Same(p.Role, s.SendsBackTo!)) ? s with { SendsBackTo = null } : s).ToList();
        return new Flow(name, string.Join(" ", description), stages, Math.Clamp(rounds, 0, 10), file);
    }

    public static int IndexOf(Flow flow, string role) => flow.Stages.ToList().FindIndex(s => Same(s.Role, role));

    static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\s*Max rounds:\s*(\d+)\s*$", RegexOptions.IgnoreCase)] private static partial Regex RoundsLine();
    [GeneratedRegex(@"^\s*Sends back to:\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex BackLine();
    [GeneratedRegex(@"VERDICT:\s*\**\s*(PASS|FAIL)", RegexOptions.IgnoreCase)] private static partial Regex VerdictLine();

    /// <summary>What a checking stage decided: the last VERDICT: PASS or FAIL in its report.</summary>
    public static Verdict VerdictOf(string report)
    {
        var all = VerdictLine().Matches(report);
        if (all.Count == 0) return Verdict.None;
        return all[^1].Groups[1].Value.Equals("PASS", StringComparison.OrdinalIgnoreCase) ? Verdict.Pass : Verdict.Fail;
    }

    /// <summary>Whether a stage said the flow can't succeed (a FLOW: STOP line).</summary>
    public static bool AsksToStop(string report) => StopLine().IsMatch(report);
    [GeneratedRegex(@"^\W*FLOW:\s*STOP\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)] private static partial Regex StopLine();

    /// <summary>The first message to a stage's agent: who it is in the flow, the task, and the reports
    /// handed on so far (role, report), oldest first.</summary>
    public static string StagePrompt(Flow flow, int stage, string task, IReadOnlyList<(string Role, string Report)> handoffs)
    {
        var s = flow.Stages[stage];
        var sb = new StringBuilder();
        sb.AppendLine($"You are the {s.Role} in a multi-agent flow, \"{flow.Name}\": " +
                      string.Join(" → ", flow.Stages.Select(x => x.Role)) + $". You are stage {stage + 1} of {flow.Stages.Count}.");
        sb.AppendLine("The flow runs on its own: each stage's last message is handed to the next. Decide for yourself rather than asking; " +
                      "only use AskUserQuestion if you truly can't go on without an answer. If the flow can't succeed whatever you do " +
                      "(the task or the briefs contradict each other, or what's needed is outside every stage's reach), end your message " +
                      "with a line reading `FLOW: STOP` and say why: the flow stops there instead of going round again.");
        sb.AppendLine();
        sb.AppendLine($"## Your brief ({s.Role})");
        sb.AppendLine(s.Brief);
        if (s.Checks)
        {
            sb.AppendLine();
            sb.AppendLine($"End your last message with a line reading exactly `VERDICT: PASS` or `VERDICT: FAIL`. On FAIL the {s.SendsBackTo} " +
                          "gets your report to fix what you found, so make it specific: what is wrong, where, and how you saw it.");
        }
        sb.AppendLine();
        sb.AppendLine("## The task");
        sb.AppendLine(task.Trim());
        foreach (var (role, report) in handoffs)
        {
            sb.AppendLine();
            sb.AppendLine($"## Handed on by the {role}");
            sb.AppendLine(report.Trim());
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>The message to a stage being sent back: the checker's report to act on.</summary>
    public static string SendBackPrompt(string checker, string report, int round, int maxRounds) =>
        $"The {checker} sent this back (round {round} of {maxRounds}). Fix what it found, then end with a report of what you changed, " +
        $"as before; it goes to the {checker} again.\n\n## The {checker}'s report\n{report.Trim()}";

    /// <summary>The message to a checker after a fix: look again.</summary>
    public static string RecheckPrompt(string fixer, string report) =>
        $"The {fixer} has made changes after your report. Check again, the same way, and end with `VERDICT: PASS` or `VERDICT: FAIL`.\n\n" +
        $"## The {fixer}'s report\n{report.Trim()}";

    public const string Starter = """
        # Designer, engineer, QA
        A designer specifies the change, an engineer builds it, QA checks it and sends it back if it falls short.
        Max rounds: 2

        ## Designer
        Work out what should be built for the task: read the code and docs it touches, then write the spec.
        Don't change any code. Your last message is the whole hand-off to the engineer, so put the spec in it:
        what the change is, how it should look and behave, the files likely involved, edge cases, and how to tell it works.

        ## Engineer
        Build what the designer specified, following the project's CLAUDE.md. Build and run the tests.
        If the spec is wrong somewhere, do the right thing and say why. Commit your work if the project's rules say to.
        Your last message goes to QA: what you changed, where, how you checked it, and anything you didn't do.

        ## QA
        Sends back to: Engineer
        Check the engineer's work against the designer's spec and the task. Don't fix anything yourself.
        Read the diff, build, run the tests, and where you can, run the app and look. Report what you checked and what you found.
        """;
}
