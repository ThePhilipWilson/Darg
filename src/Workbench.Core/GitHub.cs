using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Workbench.Core;

public sealed record HubItem(int Number, string Title, string Author, string Url, DateTime Updated, IReadOnlyList<string> Labels, string Body);
public sealed record HubRun(string Name, string Branch, string Status, string Conclusion, string Url, DateTime Updated)
{
    /// <summary>One word for the run: running, success, failure, cancelled…</summary>
    public string State => Status == "completed" ? (Conclusion.Length > 0 ? Conclusion : "done") : Status;
}
public sealed record HubRepo(string Owner, string Name, IReadOnlyList<HubItem> Pulls, IReadOnlyList<HubItem> Issues, IReadOnlyList<HubRun> Runs, string Error)
{
    public string Full => $"{Owner}/{Name}";
}

/// <summary>GitHub for a project, through the gh CLI, read-only. Each project names the gh account it
/// signs in as (work, home, game): that account's token is passed to gh for the call only, so the
/// person's active gh account is never switched and accounts never mix.</summary>
public static class GitHub
{
    static readonly ConcurrentDictionary<string, string?> Tokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>owner/name from a repo's origin, when it's on github.com (ssh or https form).</summary>
    public static (string Owner, string Name)? ParseRemote(string url)
    {
        var m = Regex.Match(url.Trim(), @"github\.com(?:-[^:/\s]+)?[:/]+([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    public static (string Owner, string Name)? RepoOf(string repoPath)
    {
        var (code, output) = Run(repoPath, null, "git", "remote", "get-url", "origin");
        return code == 0 ? ParseRemote(output) : null;
    }

    public static HubRepo Load(string repoPath, string? account)
    {
        var repo = RepoOf(repoPath);
        if (repo is null)
        {
            // not GitHub: GitLab, if the origin is there
            var (code, origin) = Run(repoPath, null, "git", "remote", "get-url", "origin");
            if (code == 0 && GitLab.ParseRemote(origin) is { } lab) return GitLab.Load(repoPath, lab.Host, lab.Path);
            return new HubRepo("", Path.GetFileName(repoPath), [], [], [], "Its origin isn't on GitHub or GitLab.");
        }
        var (owner, name) = repo.Value;
        var token = account is { Length: > 0 } ? Tokens.GetOrAdd(account, TokenFor) : null;
        if (account is { Length: > 0 } && token is null)
            return new HubRepo(owner, name, [], [], [], $"gh has no login for {account}: run gh auth login and pick that account.");
        string? error = null;
        JsonArray Get(string path)
        {
            var (code, output) = Run(repoPath, token, "gh", "api", path);
            if (code != 0)
            {
                var said = output.Trim().Split('\n')[0];
                // GitHub answers 404 for a private repo the account can't see, not 403
                error ??= said.Contains("404") ? $"Not found as {account ?? "gh's active account"}: if the repo is private, set this project's GitHub account in Setup." : said;
                return [];
            }
            return JsonNode.Parse(output) switch { JsonArray a => a, JsonObject o when o["workflow_runs"] is JsonArray r => r, _ => [] };
        }
        var pulls = Get($"repos/{owner}/{name}/pulls?state=open&per_page=30").Select(Item).ToList();
        var issues = Get($"repos/{owner}/{name}/issues?state=open&per_page=50")
            .Where(i => i?["pull_request"] is null).Select(Item).ToList(); // the issues endpoint lists PRs too
        var runs = Get($"repos/{owner}/{name}/actions/runs?per_page=8").Select(r => new HubRun(
            (string?)r?["name"] ?? "", (string?)r?["head_branch"] ?? "", (string?)r?["status"] ?? "", (string?)r?["conclusion"] ?? "",
            (string?)r?["html_url"] ?? "", Date(r?["updated_at"]))).ToList();
        return new HubRepo(owner, name, pulls, issues, runs, error ?? "");
    }

    public static HubItem Item(JsonNode? n) => new(
        (int?)n?["number"] ?? 0, (string?)n?["title"] ?? "", (string?)n?["user"]?["login"] ?? "", (string?)n?["html_url"] ?? "",
        Date(n?["updated_at"]), (n?["labels"] as JsonArray ?? []).Select(l => (string?)l?["name"]).OfType<string>().ToList(),
        (string?)n?["body"] ?? "");

    static DateTime Date(JsonNode? n) => DateTime.TryParse((string?)n, out var d) ? d : DateTime.MinValue;

    static string? TokenFor(string account)
    {
        var (code, output) = Run(null, null, "gh", "auth", "token", "--user", account);
        return code == 0 && output.Trim().Length > 0 ? output.Trim() : null;
    }

    /// <summary>The gh accounts this machine is logged in to (for Setup's choices).</summary>
    public static List<string> Accounts()
    {
        var (_, output) = Run(null, null, "gh", "auth", "status");
        return Regex.Matches(output, @"account (\S+)").Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    static (int Code, string Output) Run(string? cwd, string? token, string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (cwd is not null) psi.WorkingDirectory = cwd;
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (token is not null) psi.Environment["GH_TOKEN"] = token;
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        try
        {
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000)) { p.Kill(); return (-1, "timed out"); }
            return (p.ExitCode, p.ExitCode == 0 ? output.Result : err.Result + output.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, $"{exe} isn't installed or isn't on PATH."); }
    }
}
