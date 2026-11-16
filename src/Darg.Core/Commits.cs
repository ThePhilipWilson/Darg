namespace Darg.Core;

/// <summary>What an agent may do with git in a folder: commit and push freely, commit only, ask first, or not touch it.</summary>
public enum CommitRule { Push, Commit, Ask, Never }

/// <summary>Commit and push rules per folder (WOR-0039): one repo's are not another's. Darg's own repo is
/// committed and pushed freely while the live site needs Philip's OK each time, so one line for every agent
/// ("Never commit or push") was wrong for both. A project's <see cref="ProjectConfig.Commits"/> maps a folder
/// (a repo, or the vault its boards live in) to a rule; the rule for a folder is the nearest one at or above it.</summary>
public static class Commits
{
    /// <summary>A rule's name as instance.json and the Setup box write it.</summary>
    public static string Name(CommitRule r) => r.ToString().ToLowerInvariant();

    public static CommitRule? Parse(string? s) =>
        Enum.TryParse<CommitRule>(s?.Trim(), ignoreCase: true, out var r) && Enum.IsDefined(r) && !int.TryParse(s, out _) ? r : null;

    /// <summary>The rule for a folder: the deepest folder in the project's rules that holds it, or null when none does.</summary>
    public static CommitRule? For(ProjectConfig p, string? folder)
    {
        if (p.Commits is not { Count: > 0 } || string.IsNullOrWhiteSpace(folder)) return null;
        var target = Norm(folder);
        return p.Commits
            .Select(kv => (Folder: Norm(kv.Key), Rule: Parse(kv.Value)))
            .Where(x => x.Rule is not null && (target == x.Folder || target.StartsWith(x.Folder + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Folder.Length)
            .Select(x => x.Rule).FirstOrDefault();
    }

    static string Norm(string folder)
    {
        try { folder = Path.GetFullPath(folder); } catch (Exception) { }
        return folder.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>The rule as an instruction to an agent, for the work in <paramref name="folder"/>.</summary>
    public static string Describe(CommitRule rule, string folder) => rule switch
    {
        CommitRule.Push => $"In {folder}, commit your work and push it without asking.",
        CommitRule.Commit => $"In {folder}, commit your work without asking, but never push.",
        CommitRule.Ask => $"In {folder}, ask with AskUserQuestion before each commit and each push, saying what it holds; do neither without a yes.",
        _ => $"In {folder}, never commit or push.",
    };

    /// <summary>Every rule a project sets, one instruction each, deepest folder last so it reads as the exception.</summary>
    public static IEnumerable<string> Describe(ProjectConfig p) =>
        (p.Commits ?? []).Select(kv => (kv.Key, Rule: Parse(kv.Value))).Where(x => x.Rule is not null)
            .OrderBy(x => Norm(x.Key).Length).Select(x => Describe(x.Rule!.Value, x.Key));

    /// <summary>The Setup box's text: "rule folder" per line (rule first, so a drive letter's colon needs no escaping).</summary>
    public static string Render(Dictionary<string, string>? commits) =>
        string.Join(Environment.NewLine, (commits ?? []).Select(kv => $"{(Parse(kv.Value) is { } r ? Name(r) : kv.Value)} {kv.Key}"));

    /// <summary>The Setup box back into rules. A line that doesn't start with a rule is taken as a folder with
    /// rule "ask", the safe reading of a typo or a forgotten rule; a rule with no folder is dropped.</summary>
    public static Dictionary<string, string>? ParseText(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var sp = line.IndexOfAny([' ', '\t']);
            var rule = Parse(sp > 0 ? line[..sp] : line);
            if (rule is null) map[line] = Name(CommitRule.Ask);
            else if (sp > 0) map[line[(sp + 1)..].Trim()] = Name(rule.Value);
        }
        return map.Count > 0 ? map : null;
    }
}
