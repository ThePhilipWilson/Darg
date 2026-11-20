using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

public enum HubPartKind { Markdown, Lines, Log, Diff }

/// <summary>A line in a detail's list: a check, a job step, a changed file, an asset. <see cref="State"/>
/// is a GitHub word (success, failure, in_progress…) that colours <see cref="Tag"/>.</summary>
public sealed record HubLine(string Tag, string Text, string Meta = "", string State = "", string Url = "");

/// <summary>A piece of a detail: a markdown body or comment, a list of lines, a log excerpt (with the whole
/// log in <see cref="Full"/>), or a diff.</summary>
public sealed record HubPart(string Heading, HubPartKind Kind, string Text = "", IReadOnlyList<HubLine>? Lines = null, string Full = "");

/// <summary>Everything about one pull request, issue, run, commit or release, so the browser isn't needed.
/// <see cref="Excerpt"/> is what a card made from it carries (a failed run's errors).</summary>
public sealed record HubDetail(string Title, string Sub, string Url, IReadOnlyList<HubPart> Parts, string Error = "")
{
    public string Excerpt { get; init; } = "";
}

/// <summary>The detail behind a row of the GitHub page, read when it's picked.</summary>
public static class GitHubDetail
{
    const string PullQuery = """
        query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name){pullRequest(number:$number){
          number title url body state isDraft createdAt headRefName baseRefName additions deletions changedFiles reviewDecision mergeable author{login}
          labels(first:10){nodes{name}}
          files(first:100){nodes{path additions deletions changeType}}
          commits(last:1){nodes{commit{statusCheckRollup{state contexts(first:50){nodes{ __typename ... on CheckRun{name conclusion status detailsUrl} ... on StatusContext{context state targetUrl description}}}}}}}
          comments(first:100){nodes{author{login} body createdAt url}}
          reviews(first:50){nodes{author{login} body state submittedAt url comments(first:50){nodes{path line originalLine body createdAt author{login}}}}}
        }}}
        """;

    const string IssueQuery = """
        query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name){issue(number:$number){
          number title url body state createdAt author{login} labels(first:10){nodes{name}} assignees(first:5){nodes{login}}
          comments(first:100){nodes{author{login} body createdAt url}}
        }}}
        """;

    public static HubDetail Pull(HubRepo repo, string? account, int number)
    {
        if (GitHub.Gh.For(account) is not { } gh) return NoLogin(account);
        var pr = gh.Graph(PullQuery, ("owner", repo.Owner), ("name", repo.Name), ("number", number))?["repository"]?["pullRequest"];
        return pr is null ? Failed($"#{number}", gh.Error) : ParsePull(pr);
    }

    public static HubDetail Issue(HubRepo repo, string? account, int number)
    {
        if (GitHub.Gh.For(account) is not { } gh) return NoLogin(account);
        var issue = gh.Graph(IssueQuery, ("owner", repo.Owner), ("name", repo.Name), ("number", number))?["repository"]?["issue"];
        return issue is null ? Failed($"#{number}", gh.Error) : ParseIssue(issue);
    }

    /// <summary>A run's jobs and steps; for each failed job, GitHub's annotations and its log's telling lines.</summary>
    public static HubDetail Run(HubRepo repo, string? account, HubRun run)
    {
        if (GitHub.Gh.For(account) is not { } gh) return NoLogin(account);
        var jobs = gh.Get($"repos/{repo.Owner}/{repo.Name}/actions/runs/{run.Id}/jobs?per_page=50")?["jobs"] as JsonArray;
        if (jobs is null) return Failed(run.Title, gh.Error);
        return ParseRun(run, jobs, id => (
            gh.Get($"repos/{repo.Owner}/{repo.Name}/check-runs/{id}/annotations", quiet: true) as JsonArray ?? [],
            gh.Text($"repos/{repo.Owner}/{repo.Name}/actions/jobs/{id}/logs")));
    }

    public static HubDetail Commit(HubRepo repo, string? account, string sha)
    {
        if (GitHub.Gh.For(account) is not { } gh) return NoLogin(account);
        var c = gh.Get($"repos/{repo.Owner}/{repo.Name}/commits/{sha}");
        if (c is null) return Failed(sha, gh.Error);
        var checks = gh.Get($"repos/{repo.Owner}/{repo.Name}/commits/{sha}/check-runs?per_page=50", quiet: true)?["check_runs"] as JsonArray;
        return ParseCommit(c, checks);
    }

    static HubDetail NoLogin(string? account) => Failed("", $"gh has no login for {account}: run gh auth login and pick that account.");
    static HubDetail Failed(string title, string error) => new(title, "", "", [], error.Length > 0 ? error : "GitHub gave nothing back.");

    public static HubDetail ParsePull(JsonNode pr)
    {
        var parts = new List<HubPart>();
        Body(parts, pr);
        var rollup = GitHub.Nodes(pr["commits"]).FirstOrDefault()?["commit"]?["statusCheckRollup"];
        var checks = GitHub.Nodes(rollup?["contexts"]).Select(c => (string?)c?["__typename"] == "StatusContext"
            ? new HubLine(Mark((string?)c?["state"] ?? ""), (string?)c?["context"] ?? "", (string?)c?["description"] ?? "", Lower((string?)c?["state"]), (string?)c?["targetUrl"] ?? "")
            : new HubLine(Mark(CheckState(c)), (string?)c?["name"] ?? "", "", CheckState(c), (string?)c?["detailsUrl"] ?? "")).ToList();
        if (checks.Count > 0) parts.Add(new HubPart($"Checks · {Lower((string?)rollup?["state"])}", HubPartKind.Lines, Lines: checks));
        var files = GitHub.Nodes(pr["files"]).Select(f => new HubLine(Lower((string?)f?["changeType"]), (string?)f?["path"] ?? "",
            $"+{(int?)f?["additions"] ?? 0} −{(int?)f?["deletions"] ?? 0}")).ToList();
        if (files.Count > 0) parts.Add(new HubPart($"Files changed · {(int?)pr["changedFiles"] ?? files.Count}", HubPartKind.Lines, Lines: files));

        // the conversation in order: comments, reviews, and the line comments under each review
        var said = new List<(DateTime When, HubPart Part)>();
        foreach (var c in GitHub.Nodes(pr["comments"]))
            said.Add((GitHub.Date(c?["createdAt"]), Said(c, "commented", (string?)c?["body"] ?? "")));
        foreach (var r in GitHub.Nodes(pr["reviews"]))
        {
            var state = Lower((string?)r?["state"]).Replace('_', ' ');
            var body = (string?)r?["body"] ?? "";
            var lines = GitHub.Nodes(r?["comments"]).Select(c => $"**{(string?)c?["path"]}:{(int?)c?["line"] ?? (int?)c?["originalLine"] ?? 0}**\n\n{(string?)c?["body"]}");
            var all = string.Join("\n\n", new[] { body }.Concat(lines).Where(s => s.Trim().Length > 0));
            if (all.Length == 0 && state == "commented") continue; // an empty review is a stray click
            said.Add((GitHub.Date(r?["submittedAt"]), Said(r, state, all)));
        }
        parts.AddRange(said.OrderBy(s => s.When).Select(s => s.Part));

        var bits = new List<string> { $"{(string?)pr["author"]?["login"]} wants {(string?)pr["headRefName"]} into {(string?)pr["baseRefName"]}",
            $"+{(int?)pr["additions"] ?? 0} −{(int?)pr["deletions"] ?? 0}" };
        if ((bool?)pr["isDraft"] == true) bits.Insert(0, "draft");
        if ((string?)pr["reviewDecision"] is { Length: > 0 } rd) bits.Add(Lower(rd).Replace('_', ' '));
        if ((string?)pr["mergeable"] == "CONFLICTING") bits.Add("has conflicts");
        bits.Add(Labels(pr));
        return new HubDetail($"#{(int?)pr["number"]} {(string?)pr["title"]}", Join(bits), (string?)pr["url"] ?? "", parts);
    }

    public static HubDetail ParseIssue(JsonNode issue)
    {
        var parts = new List<HubPart>();
        Body(parts, issue);
        parts.AddRange(GitHub.Nodes(issue["comments"]).Select(c => Said(c, "commented", (string?)c?["body"] ?? "")));
        var assignees = GitHub.Nodes(issue["assignees"]).Select(a => (string?)a?["login"]).OfType<string>().ToList();
        var bits = new List<string> { $"opened by {(string?)issue["author"]?["login"]} {Day(GitHub.Date(issue["createdAt"]))}",
            assignees.Count > 0 ? "assigned to " + string.Join(", ", assignees) : "", Labels(issue) };
        return new HubDetail($"#{(int?)issue["number"]} {(string?)issue["title"]}", Join(bits), (string?)issue["url"] ?? "", parts);
    }

    /// <param name="failed">For a failed job's id: its annotations and its log (null when GitHub has none).</param>
    public static HubDetail ParseRun(HubRun run, JsonArray jobs, Func<long, (JsonArray Annotations, string? Log)> failed)
    {
        var parts = new List<HubPart>();
        var excerpt = new StringBuilder();
        foreach (var j in jobs)
        {
            var state = StepState(j);
            var steps = (j?["steps"] as JsonArray ?? []).Select(s => new HubLine(Mark(StepState(s)), (string?)s?["name"] ?? "", Took(s), StepState(s))).ToList();
            parts.Add(new HubPart($"{Mark(state)} {(string?)j?["name"]} · {state.Replace('_', ' ')}{(Took(j) is { Length: > 0 } t ? " · " + t : "")}", HubPartKind.Lines, Lines: steps));
            if (state is not ("failure" or "timed_out" or "startup_failure")) continue;
            var (notes, log) = failed((long?)j?["id"] ?? 0);
            // failures first: GitHub mixes them with warnings (deprecated actions, compiler warnings)
            var said = notes.OrderBy(a => (string?)a?["annotation_level"] == "failure" ? 0 : 1).Select(a => new HubLine(Mark((string?)a?["annotation_level"] == "failure" ? "failure" : "neutral"),
                    ((string?)a?["title"] is { Length: > 0 } title ? title + ": " : "") + FirstLines((string?)a?["message"] ?? "", 6),
                    (string?)a?["path"] is { Length: > 0 } p && p != ".github" ? $"{p}:{(int?)a?["start_line"]}" : "", (string?)a?["annotation_level"] ?? ""))
                .ToList();
            if (said.Count > 0) parts.Add(new HubPart("What GitHub says", HubPartKind.Lines, Lines: said));
            excerpt.AppendLine($"Job {(string?)j?["name"]} failed.");
            foreach (var a in said.Where(a => a.State == "failure")) excerpt.AppendLine($"- {a.Text}");
            if (log is not null)
            {
                var clean = CleanLog(log);
                var cut = LogExcerpt(clean);
                parts.Add(new HubPart($"Log of {(string?)j?["name"]}: the lines that matter", HubPartKind.Log, cut, Full: clean));
                excerpt.AppendLine("```").AppendLine(cut).AppendLine("```");
            }
            else if (said.Count == 0) parts.Add(new HubPart("", HubPartKind.Markdown, "GitHub kept no log for this job."));
            excerpt.AppendLine();
        }
        if (jobs.Count == 0) parts.Add(new HubPart("", HubPartKind.Markdown, "No jobs ran."));
        var sub = Join([run.Name, $"run {run.Number}", run.Branch, run.Event, run.Sha.Length > 7 ? run.Sha[..7] : run.Sha, run.State.Replace('_', ' ')]);
        return new HubDetail(run.Title.Length > 0 ? run.Title : run.Name, sub, run.Url, parts) { Excerpt = excerpt.ToString().Trim() };
    }

    public static HubDetail ParseCommit(JsonNode c, JsonArray? checks)
    {
        var parts = new List<HubPart>();
        var message = (string?)c["commit"]?["message"] ?? "";
        var nl = message.IndexOf('\n');
        if (nl > 0 && message[nl..].Trim().Length > 0) parts.Add(new HubPart("", HubPartKind.Markdown, message[nl..].Trim()));
        var runs = (checks ?? []).Select(r => new HubLine(Mark(CheckRunState(r)), (string?)r?["name"] ?? "", "", CheckRunState(r), (string?)r?["html_url"] ?? "")).ToList();
        if (runs.Count > 0) parts.Add(new HubPart("Checks", HubPartKind.Lines, Lines: runs));
        var files = c["files"] as JsonArray ?? [];
        parts.Add(new HubPart($"Files changed · {files.Count}", HubPartKind.Lines,
            Lines: files.Select(f => new HubLine((string?)f?["status"] ?? "", (string?)f?["filename"] ?? "", $"+{(int?)f?["additions"] ?? 0} −{(int?)f?["deletions"] ?? 0}")).ToList()));
        var diff = new StringBuilder();
        foreach (var f in files)
        {
            diff.AppendLine($"── {(string?)f?["filename"]}");
            diff.AppendLine((string?)f?["patch"] is { Length: > 0 } patch ? patch : "(no text diff: binary, or too big for GitHub to show)");
            diff.AppendLine();
        }
        if (files.Count > 0) parts.Add(new HubPart("Diff", HubPartKind.Diff, Cap(diff.ToString(), 1500)));
        var sha = (string?)c["sha"] ?? "";
        var who = (string?)c["author"]?["login"] ?? (string?)c["commit"]?["author"]?["name"] ?? "";
        var sub = Join([sha.Length > 7 ? sha[..7] : sha, who, Day(GitHub.Date(c["commit"]?["author"]?["date"])),
            $"+{(int?)c["stats"]?["additions"] ?? 0} −{(int?)c["stats"]?["deletions"] ?? 0}"]);
        return new HubDetail(nl > 0 ? message[..nl] : message, sub, (string?)c["html_url"] ?? "", parts);
    }

    public static HubDetail Release(HubRelease r)
    {
        var parts = new List<HubPart>();
        if (r.Body.Trim().Length > 0) parts.Add(new HubPart("", HubPartKind.Markdown, r.Body));
        if (r.Assets.Count > 0)
            parts.Add(new HubPart($"Assets · {r.Assets.Count}", HubPartKind.Lines,
                Lines: r.Assets.Select(a => new HubLine(Size(a.Size), a.Name, $"{a.Downloads} downloads", "", a.Url)).ToList()));
        var sub = Join([r.Tag, r.Draft ? "draft" : r.Prerelease ? "pre-release" : "", r.Author, Day(r.Date)]);
        return new HubDetail(r.Name.Length > 0 ? r.Name : r.Tag, sub, r.Url, parts);
    }

    /// <summary>What a GitLab item has: its description.</summary>
    public static HubDetail Plain(HubItem i) =>
        new($"#{i.Number} {i.Title}", Join([i.Author, string.Join(", ", i.Labels)]), i.Url,
            Readable(i.Body) is { Length: > 0 } b ? [new HubPart("", HubPartKind.Markdown, b)] : []);

    /// <summary>A job log as people read it: no timestamps, no ##[group] markers, errors marked ✗.</summary>
    public static string CleanLog(string log)
    {
        var sb = new StringBuilder();
        foreach (var raw in log.TrimStart('﻿').Split('\n'))
        {
            var line = Regex.Replace(raw.TrimEnd('\r'), @"^\d{4}-\d\d-\d\dT[\d:.]+Z ?", "");
            if (line.StartsWith("##[endgroup]")) continue;
            line = line.Replace("##[group]", "▸ ").Replace("##[error]", "✗ ").Replace("##[warning]", "⚠ ");
            sb.Append(line).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    static readonly Regex Telling = new(@"✗ |\berror\b|\bFailed\b|\bFAIL(ED)?\b|Exception\b|Traceback|exit code [1-9]|Assert", RegexOptions.IgnoreCase);
    /// <summary>What only looks like a failure: a count of none (dotnet pads it, <c>Failed:     0</c>; others write
    /// <c>0 failed</c>, <c>failures: 0</c>, <c>errors=0</c>, <c>failures="0"</c>, <c>0 Error(s)</c>) and switches
    /// that name failing (<c>--fail-on-empty</c>, <c>fail-fast</c>, <c>continue-on-error</c>). Taken out of a line
    /// before it is read for <see cref="Telling"/> words, so "Failed: 1, Errors: 0" still tells.</summary>
    static readonly Regex Harmless = new(
        @"(?<![\w.])0\s+(?:error\(s\)|(?:fail(?:ed|ures?|ing)?|errors?)\b)" +
        @"|\b(?:error\(s\)|fail(?:ed|ures?|ing)?|errors?)\s*[:=]?\s*""?0(?![\w.])" +
        @"|--fail[\w-]*|fail-fast|continue-on-error", RegexOptions.IgnoreCase);

    /// <summary>A test that passed, which says nothing about why a job failed, whatever it's called.</summary>
    static readonly Regex Passing = new(@"^\s*(Passed|PASS|ok|✓|√)\b", RegexOptions.IgnoreCase);

    /// <summary>The noise a run makes on the way to its last error: passing tests and summaries, build output
    /// (<c>Proj -> bin/Proj.dll</c>), restores, test-run banners, warnings, blank lines.</summary>
    static readonly Regex Quiet = new(
        @"^\s*$|^\s*(Passed|PASS|ok|✓|√)\b|^\s*\S+ -> \S|^Test run for |^A total of \d+ test files|^⚠ |^\s*Restored |^\s*Determining projects|^\[command\]",
        RegexOptions.IgnoreCase);

    /// <summary>Whether a log line says something failed.</summary>
    public static bool Tells(string line) => !Passing.IsMatch(line) && Telling.IsMatch(Harmless.Replace(line, " "));

    /// <summary>The lines of a failed job's (cleaned) log that say why: each telling line with two either side
    /// (leaving out noise: passing tests, build output), and the lines leading up to the job's last error (or the log's end when it
    /// marked none) that aren't noise, which is where a build or test run sums up what broke. What follows that
    /// error is the runner tidying up.</summary>
    public static string LogExcerpt(string log, int around = 2, int tail = 10, int max = 80)
    {
        var lines = log.Split('\n');
        var keep = new bool[lines.Length];
        var end = Array.FindLastIndex(lines, l => l.StartsWith("✗ "));
        if (end < 0) end = lines.Length - 1;
        var hits = 0;
        for (var i = 0; i <= end && hits < 25; i++)
            if (Tells(lines[i]))
            {
                hits++;
                for (var k = Math.Max(0, i - around); k <= Math.Min(end, i + around); k++)
                    if (k == i || !Quiet.IsMatch(lines[k])) keep[k] = true;
            }
        // the run-up to the last error, looking back a few screens at most
        keep[end] = true;
        var tailFrom = end;
        for (int k = end - 1, n = 0; k >= 0 && k >= end - tail * 4 && n < tail; k--)
            if (!Quiet.IsMatch(lines[k])) { keep[k] = true; tailFrom = k; n++; }
        var sb = new StringBuilder();
        var count = 0;
        var gap = false;
        for (var i = 0; i <= end; i++)
        {
            if (!keep[i] || (i < tailFrom && count >= max))
            {
                if (lines[i].Trim().Length > 0) gap = true; // a blank line left out isn't worth a …
                continue;
            }
            if (gap && sb.Length > 0) sb.Append("…\n");
            gap = false;
            sb.Append(lines[i]).Append('\n');
            if (i < tailFrom) count++;
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>A pull request's or issue's text as GitHub shows it: without the HTML comments a template leaves
    /// in (<c>&lt;!-- Describe your change --&gt;</c>), an unclosed one running to the end.</summary>
    public static string Readable(string body) => Regex.Replace(body, @"<!--.*?(-->|$)", "", RegexOptions.Singleline).Trim();

    static void Body(List<HubPart> parts, JsonNode n)
    {
        var body = Readable((string?)n["body"] ?? "");
        parts.Add(new HubPart($"{(string?)n["author"]?["login"]} · {Day(GitHub.Date(n["createdAt"]))}", HubPartKind.Markdown,
            body.Length > 0 ? body : "*No description.*"));
    }

    static HubPart Said(JsonNode? c, string what, string body) =>
        new($"{(string?)c?["author"]?["login"]} {what} · {Day(GitHub.Date(c?["createdAt"] ?? c?["submittedAt"]))}", HubPartKind.Markdown,
            Readable(body) is { Length: > 0 } b ? b : "*(nothing written)*");

    static string Labels(JsonNode n) => string.Join(", ", GitHub.Nodes(n["labels"]).Select(l => (string?)l?["name"]).OfType<string>());

    static string CheckState(JsonNode? c) => (string?)c?["status"] is "COMPLETED" or null ? Lower((string?)c?["conclusion"]) : Lower((string?)c?["status"]);
    static string CheckRunState(JsonNode? r) => (string?)r?["status"] == "completed" ? (string?)r?["conclusion"] ?? "done" : (string?)r?["status"] ?? "";
    static string StepState(JsonNode? s) => (string?)s?["status"] == "completed" ? (string?)s?["conclusion"] ?? "done" : (string?)s?["status"] ?? "";

    static string Took(JsonNode? s)
    {
        var a = GitHub.Date(s?["started_at"]);
        var b = GitHub.Date(s?["completed_at"]);
        return a == DateTime.MinValue || b == DateTime.MinValue || b < a ? "" : Duration(b - a);
    }

    /// <summary>A duration as a person says it: 40s, 3m 12s, 1h 5m.</summary>
    public static string Duration(TimeSpan t) => t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds}s"
        : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m {t.Seconds}s" : $"{(int)t.TotalHours}h {t.Minutes}m";

    /// <summary>A mark for a state: ✓ passed, ✗ failed, ● running, ○ waiting, – skipped or cancelled.</summary>
    public static string Mark(string state) => Lower(state) switch
    {
        "success" or "completed" or "approved" => "✓",
        "failure" or "error" or "timed_out" or "startup_failure" or "action_required" or "changes_requested" => "✗",
        "in_progress" or "pending" or "running" => "●",
        "queued" or "waiting" or "requested" or "expected" => "○",
        "cancelled" or "skipped" or "neutral" or "stale" => "–",
        _ => "·",
    };

    static string Lower(string? s) => (s ?? "").ToLowerInvariant();
    static string Day(DateTime t) => t == DateTime.MinValue ? "" : t.ToLocalTime().ToString("d MMM yyyy HH:mm");
    static string Join(IEnumerable<string> bits) => string.Join(" · ", bits.Where(b => b.Length > 0));
    static string FirstLines(string s, int n) => string.Join("\n", s.Split('\n').Take(n)) + (s.Split('\n').Length > n ? "\n…" : "");
    static string Cap(string s, int lines) => s.Split('\n') is var l && l.Length > lines ? string.Join('\n', l.Take(lines)) + $"\n… {l.Length - lines} more lines: Open ↗ for the rest" : s;
    static string Size(long b) => b < 1024 ? $"{b} B" : b < 1 << 20 ? $"{b / 1024.0:0.#} KB" : b < 1L << 30 ? $"{b / 1048576.0:0.#} MB" : $"{b / 1073741824.0:0.##} GB";
}
