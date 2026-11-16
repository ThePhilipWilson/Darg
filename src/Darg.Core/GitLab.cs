using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>GitLab for a project, through the glab CLI, read-only: open merge requests, open issues and
/// recent pipelines, in the same shape as GitHub's so one page shows either. Untested against a live
/// GitLab here (no glab on the home PC); written for the work instance.</summary>
public static class GitLab
{
    /// <summary>(host, group/project path) from an origin on a GitLab host (gitlab.com or a host with
    /// "gitlab" in its name, ssh or https form).</summary>
    public static (string Host, string Path)? ParseRemote(string url)
    {
        var m = Regex.Match(url.Trim(), @"^(?:https?://|ssh://)?(?:[^@/]+@)?([^/:]*gitlab[^/:]*)(?::\d+)?[:/]+(.+?)(?:\.git)?/?$", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    public static HubRepo Load(string repoPath, string host, string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var owner = path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
        var id = Uri.EscapeDataString(path);
        string? error = null;
        JsonArray Get(string api)
        {
            var (code, output) = Run(repoPath, host, "glab", "api", api);
            if (code != 0) { error ??= output.Trim().Split('\n')[0]; return []; }
            return JsonNode.Parse(output) as JsonArray ?? [];
        }
        var mrs = Get($"projects/{id}/merge_requests?state=opened&per_page=30").Select(n => Item(n, "iid", "web_url")).ToList();
        if (error is not null && error.Contains("isn't installed")) return new HubRepo(owner, name, [], [], [], error);
        var issues = Get($"projects/{id}/issues?state=opened&per_page=50").Select(n => Item(n, "iid", "web_url")).ToList();
        var runs = Get($"projects/{id}/pipelines?per_page=8").Select(Run).ToList();
        return new HubRepo(owner, name, mrs, issues, runs, error ?? "");
    }

    public static HubItem Item(JsonNode? n, string number, string url) => new(
        (int?)n?[number] ?? 0, (string?)n?["title"] ?? "", (string?)n?["author"]?["username"] ?? "", (string?)n?[url] ?? "",
        DateTime.TryParse((string?)n?["updated_at"], out var d) ? d : DateTime.MinValue,
        (n?["labels"] as JsonArray ?? []).Select(l => (string?)l).OfType<string>().ToList(), (string?)n?["description"] ?? "");

    /// <summary>A pipeline as a run: GitLab's statuses mapped onto GitHub's words (success, failure, in_progress…).</summary>
    public static HubRun Run(JsonNode? n)
    {
        var status = (string?)n?["status"] ?? "";
        var (s, c) = status switch
        {
            "success" => ("completed", "success"),
            "failed" => ("completed", "failure"),
            "canceled" => ("completed", "cancelled"),
            "skipped" => ("completed", "skipped"),
            "running" => ("in_progress", ""),
            _ => (status, ""), // created, pending, manual, scheduled…
        };
        return new HubRun($"pipeline #{(int?)n?["id"] ?? 0}", (string?)n?["ref"] ?? "", s, c, (string?)n?["web_url"] ?? "",
            DateTime.TryParse((string?)n?["updated_at"], out var d) ? d : DateTime.MinValue);
    }

    static (int Code, string Output) Run(string cwd, string host, string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GITLAB_HOST"] = host;
        try
        {
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000)) { p.Kill(); return (-1, "timed out"); }
            return (p.ExitCode, p.ExitCode == 0 ? output.Result : err.Result + output.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, "glab (GitLab's CLI) isn't installed or isn't on PATH: install it and run glab auth login."); }
    }
}
