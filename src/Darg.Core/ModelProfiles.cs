using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A named model and effort for a kind of job ("Quick: haiku, low"), and what it's for, in words
/// Haiku reads when it picks one.</summary>
public sealed record ModelProfile(string Name, string Model, string Effort, string UseFor)
{
    /// <summary>"opus, effort high"; "your default model" for a profile that leaves both to the person's settings.</summary>
    public string Summary
    {
        get
        {
            var model = Model == AgentChoices.Default ? "default model" : Model;
            return Effort == AgentChoices.Default ? model : $"{model}, effort {Effort}";
        }
    }
}

/// <summary>A rule: a job whose <see cref="Key"/> (kind, type, label, role or project) is <see cref="Value"/> gets <see cref="Profile"/>.</summary>
public sealed record ProfileRule(string Key, string Value, string Profile)
{
    public override string ToString() => $"{Key}: {Value}";
}

/// <summary>The instance's profiles and rules. <see cref="AskHaiku"/>: when no rule matches, Haiku reads the
/// task and picks; <see cref="Otherwise"/> is the profile when it doesn't (null: the person's own settings).</summary>
public sealed record ProfileSet(IReadOnlyList<ModelProfile> Profiles, IReadOnlyList<ProfileRule> Rules, bool AskHaiku, string? Otherwise)
{
    public static readonly ProfileSet None = new([], [], false, null);

    public ModelProfile? Find(string? name) =>
        name is null ? null : Profiles.FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>What an agent is being started for: what the rules look at, and the words Haiku reads.</summary>
/// <param name="Kind">One of <see cref="JobKinds"/>.</param>
/// <param name="Text">The task in plain words (a card's title and description, not the house rules around it).</param>
public sealed record AgentJob(string Kind, string Text, string? CardType = null, IReadOnlyList<string>? Labels = null,
    string? Role = null, string? Project = null);

public static class JobKinds
{
    /// <summary>Chat: started from the Agents view's box. Card: Work on a card. Flow: a stage of a flow. Fettle: a Fettle chat.</summary>
    public const string Chat = "chat", Card = "card", Flow = "flow", Fettle = "fettle";
}

/// <summary>The profile picked for a job and why ("rule type: Bug", or Haiku's reason).</summary>
public sealed record ProfilePick(ModelProfile Profile, string Why);

/// <summary>Model profiles (WOR-0034): each new agent starts on the model and effort that suit its job. The first
/// rule that matches wins; else Haiku reads the task and picks a profile by what each is for; else the
/// Otherwise profile, else the person's own settings. Kept as profiles.md in the instance folder, written the
/// first time with a starter set:
/// <code>
/// # Model profiles
/// Ask Haiku: yes
/// Otherwise: default
///
/// ## Quick
/// Model: haiku
/// Effort: low
/// Use for: small mechanical jobs…
///
/// ## Rules
/// - kind: fettle -> Standard
/// - label: deep -> Deep
/// </code></summary>
public static partial class ModelProfiles
{
    public static string FilePath(string? instanceFolder = null) => Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "profiles.md");

    /// <summary>The profiles on file; the starter set is written there the first time. Never throws: a file
    /// that can't be read means no profiles, and agents start on the person's own settings as before.</summary>
    public static ProfileSet Load(string? instanceFolder = null)
    {
        try
        {
            var path = FilePath(instanceFolder);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Starter);
            }
            return Parse(File.ReadAllText(path));
        }
        catch (Exception) { return ProfileSet.None; }
    }

    public static ProfileSet Parse(string text)
    {
        var profiles = new List<ModelProfile>();
        var rules = new List<ProfileRule>();
        bool askHaiku = true;
        string? otherwise = null;
        string? section = null;
        string model = AgentChoices.Default, effort = AgentChoices.Default, useFor = "";

        void Close()
        {
            if (section is not null && !section.Equals("Rules", StringComparison.OrdinalIgnoreCase))
                profiles.Add(new ModelProfile(section, model, effort, useFor));
            model = effort = AgentChoices.Default;
            useFor = "";
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## "))
            {
                Close();
                section = line[3..].Trim();
                continue;
            }
            if (line.StartsWith('#') || line.Length == 0) continue;
            if (section is not null && section.Equals("Rules", StringComparison.OrdinalIgnoreCase))
            {
                if (RuleLine().Match(line) is { Success: true } r)
                    rules.Add(new ProfileRule(r.Groups[1].Value.ToLowerInvariant(), r.Groups[2].Value.Trim(), r.Groups[3].Value.Trim()));
                continue;
            }
            if (Field().Match(line) is not { Success: true } f) continue;
            var key = f.Groups[1].Value.Trim().ToLowerInvariant();
            var value = f.Groups[2].Value.Trim();
            if (section is null)
            {
                if (key == "ask haiku") askHaiku = !Regex.IsMatch(value, @"^(no|off|false)\b", RegexOptions.IgnoreCase);
                else if (key == "otherwise") otherwise = value.Equals(AgentChoices.Default, StringComparison.OrdinalIgnoreCase) ? null : value;
                continue;
            }
            switch (key)
            {
                case "model": model = value.Length == 0 ? AgentChoices.Default : value.ToLowerInvariant(); break;
                case "effort": effort = value.Length == 0 ? AgentChoices.Default : value.ToLowerInvariant(); break;
                case "use for": useFor = value; break;
            }
        }
        Close();
        // a rule naming a profile that isn't there is dropped, so it can't block the ones after it
        var set = new ProfileSet(profiles, [], askHaiku, otherwise);
        return set with { Rules = rules.Where(r => set.Find(r.Profile) is not null).ToList() };
    }

    /// <summary>The first rule that matches the job, or null.</summary>
    public static ProfilePick? ByRule(ProfileSet set, AgentJob job)
    {
        foreach (var rule in set.Rules)
        {
            bool Is(string? v) => v is not null && v.Trim().Equals(rule.Value, StringComparison.OrdinalIgnoreCase);
            var hit = rule.Key switch
            {
                "kind" => Is(job.Kind),
                "type" => Is(job.CardType),
                "label" => job.Labels?.Any(Is) == true,
                "role" => Is(job.Role),
                "project" => Is(job.Project),
                _ => false,
            };
            if (hit && set.Find(rule.Profile) is { } p) return new ProfilePick(p, $"rule {rule}");
        }
        return null;
    }

    /// <summary>The profile for a job: by rule, else Haiku's pick, else Otherwise; null for the person's own
    /// settings (no profiles, or none fits). Never throws.</summary>
    public static async Task<ProfilePick?> Pick(ProfileSet set, AgentJob job, TimeSpan? timeout = null)
    {
        if (set.Profiles.Count == 0) return null;
        if (ByRule(set, job) is { } byRule) return byRule;
        if (set.AskHaiku && set.Profiles.Count > 1 && job.Text.Trim().Length > 0)
        {
            var reply = await Haiku.Ask(AskPrompt(set, job), timeout ?? TimeSpan.FromSeconds(30));
            if (reply is not null && ReadReply(set, reply) is { } picked) return picked;
        }
        return set.Find(set.Otherwise) is { } fallback ? new ProfilePick(fallback, "no rule matched") : null;
    }

    /// <summary>What Haiku is asked: the profiles with what each is for, and the task.</summary>
    public static string AskPrompt(ProfileSet set, AgentJob job)
    {
        var task = job.Text.Trim();
        if (task.Length > 4000) task = task[..4000] + "…";
        var lines = new List<string>
        {
            "An AI coding agent is about to start on the task below. Pick the profile that suits it best: the cheapest one that can do it well.",
            "",
            "Profiles:",
        };
        lines.AddRange(set.Profiles.Select(p => $"- {p.Name}: {(p.UseFor.Length > 0 ? p.UseFor : p.Summary)}"));
        lines.Add("");
        lines.Add("Reply with one line and nothing else: the profile's name, a | and a reason of a few words. For example:");
        lines.Add($"{set.Profiles[0].Name} | a one-line fix in a single file");
        lines.Add("");
        if (job.Role is { Length: > 0 }) lines.Add($"The agent's role: {job.Role}");
        if (job.CardType is { Length: > 0 }) lines.Add($"Card type: {job.CardType}");
        if (job.Labels is { Count: > 0 }) lines.Add($"Labels: {string.Join(", ", job.Labels)}");
        lines.Add("Task:");
        lines.Add(task);
        return string.Join("\n", lines);
    }

    /// <summary>Haiku's reply as a pick, or null when it doesn't name exactly one profile. Takes the name with
    /// markdown, quotes or a "Profile:" in front, any separator after it, or the name alone somewhere in the line.</summary>
    public static ProfilePick? ReadReply(ProfileSet set, string reply)
    {
        var line = reply.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = Regex.Replace(line, @"[*`_""'#>]", "").Trim();
        line = Regex.Replace(line, @"^(profile|pick|answer)\s*[:=-]\s*", "", RegexOptions.IgnoreCase).Trim();
        // the line starts with a name (longest first, so "Deep plus" beats "Deep")
        foreach (var p in set.Profiles.OrderByDescending(p => p.Name.Length))
        {
            if (!line.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = line[p.Name.Length..];
            if (rest.Length > 0 && char.IsLetterOrDigit(rest[0])) continue; // "Quickly" isn't "Quick"
            return new ProfilePick(p, Reason(rest));
        }
        // else exactly one name appears anywhere in the reply
        var named = set.Profiles.Where(p => Regex.IsMatch(reply, $@"\b{Regex.Escape(p.Name)}\b", RegexOptions.IgnoreCase)).ToList();
        return named.Count == 1 ? new ProfilePick(named[0], "picked by Haiku") : null;
    }

    static string Reason(string rest)
    {
        var why = rest.TrimStart(' ', '|', ':', '-', '–', '—', ',', '.', '(').TrimEnd(')', ' ', '.');
        if (why.Length > 120) why = why[..120].TrimEnd() + "…";
        return why.Length > 0 ? $"Haiku: {why}" : "picked by Haiku";
    }

    [GeneratedRegex(@"^[-*]?\s*(kind|type|label|role|project)\s*:\s*(.+?)\s*(?:->|→|=>)\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RuleLine();
    [GeneratedRegex(@"^[-*]?\s*([A-Za-z ]+?)\s*:\s*(.*)$")]
    private static partial Regex Field();

    public const string Starter = """
        # Model profiles
        Each new agent starts on one of these. The first rule below that matches the job wins; when none does,
        Haiku reads the task and picks a profile by its "Use for". Otherwise is the profile when Haiku is off or
        can't decide ("default" leaves it to your own Claude settings). You can still change an agent's model and
        effort once it's going.

        Model: opus, fable, sonnet, haiku, or default. Effort: low, medium, high, xhigh, max, or default.

        Ask Haiku: yes
        Otherwise: default

        ## Quick
        Model: haiku
        Effort: low
        Use for: small mechanical jobs that need no judgement: a rename, a typo, moving or tidying files, looking something up.

        ## Standard
        Model: sonnet
        Effort: medium
        Use for: ordinary work: a feature or bug fix in a few files with a clear cause, answering a question about the code, checking another agent's work.

        ## Deep
        Model: opus
        Effort: high
        Use for: design and planning, a bug with no clear cause, a change across many files, anything vague or where a wrong call is costly.

        ## Rules
        One per line: kind, type, label, role or project, then -> and a profile. Kinds: card, flow, chat, fettle.
        Type and label are a card's; role is a flow stage's.

        - label: quick -> Quick
        - label: deep -> Deep
        - kind: fettle -> Standard
        """;
}
