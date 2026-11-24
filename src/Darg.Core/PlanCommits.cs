using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>What the Design page writes to a plan is committed at once (WOR-0199; Philip's call, 2026-11-24, reversing his
/// 2026-11-20 one that the page commits nothing), so a plan's state moves with git as it is. Only the plan file goes in
/// the commit, whatever else in its repo is changed or staged; it is pushed as well where the project's rule for the
/// folder (<see cref="Commits"/>) says push. A folder whose rule is never is left uncommitted, and so is a plan outside git.
/// A planner's own turn isn't this: it commits per the repo's rules itself.</summary>
public static class PlanCommits
{
    public enum Outcome { Committed, Pushed, PushFailed, Nothing, NotInGit, Never, Failed }

    public sealed record Result(Outcome Outcome, string Detail = "")
    {
        /// <summary>A sentence for the page's status line, or "" when there was nothing to commit.</summary>
        public string Text => Outcome switch
        {
            Outcome.Committed => "Committed.",
            Outcome.Pushed => "Committed and pushed.",
            Outcome.PushFailed => $"Committed, but the push failed: {Detail}",
            Outcome.NotInGit => "Not committed: the plan isn't in a git repo.",
            Outcome.Never => "Not committed: the project's rule for that folder is never.",
            Outcome.Failed => $"Not committed: {Detail}",
            _ => "",
        };
    }

    static readonly object Gate = new();
    static Task _tail = Task.CompletedTask;

    /// <summary>Commit in the background, one after another in the order asked (two edits in a row commit in order).</summary>
    public static Task<Result> Queue(string file, string what, CommitRule? rule)
    {
        lock (Gate)
        {
            var next = _tail.ContinueWith(_ => Commit(file, what, rule), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            _tail = next;
            return next;
        }
    }

    /// <summary>Stage and commit just <paramref name="file"/>, as "Design: name.md: what", then push under the push rule.
    /// No rule commits without pushing; ask commits without pushing too (the person pressing on the page is the yes for
    /// the commit, not for a push).</summary>
    public static Result Commit(string file, string what, CommitRule? rule)
    {
        if (rule == CommitRule.Never) return new(Outcome.Never);
        var repo = RepoOf(file);
        if (repo is null) return new(Outcome.NotInGit);
        var rel = Path.GetRelativePath(repo, Path.GetFullPath(file)).Replace('\\', '/');
        var (code, output) = Git(repo, "status", "--porcelain", "--untracked-files=all", "--", rel);
        if (code != 0) return new(Outcome.Failed, Last(output));
        if (output.Trim().Length == 0) return new(Outcome.Nothing);
        (code, output) = Git(repo, "add", "--", rel);
        if (code != 0) return new(Outcome.Failed, Last(output));
        // --only with the path: this file's changes and nothing else, though other files stay staged as they were
        (code, output) = Git(repo, "commit", "-q", "--only", "-m", $"Design: {Path.GetFileName(file)}: {what}", "--", rel);
        if (code != 0) return new(Outcome.Failed, Last(output));
        if (rule != CommitRule.Push) return new(Outcome.Committed);
        (code, output) = Git(repo, "push", "-q");
        return code == 0 ? new(Outcome.Pushed) : new(Outcome.PushFailed, Last(output));
    }

    /// <summary>The repo holding the file: the nearest folder above it with a .git (a folder, or a worktree's file).</summary>
    public static string? RepoOf(string file)
    {
        if (string.IsNullOrEmpty(file) || !Path.IsPathRooted(file)) return null;
        for (var d = Path.GetDirectoryName(Path.GetFullPath(file)); d is not null; d = Path.GetDirectoryName(d))
            if (Repo.IsGit(d)) return d;
        return null;
    }

    static string Last(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "git failed";

    /// <summary>git with its output and errors together; another git at work in the repo (its index.lock) is waited out.</summary>
    static (int Code, string Output) Git(string repo, params string[] args)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (code, output) = Run(repo, args);
            if (code == 0 || attempt >= 10 || !output.Contains("index.lock", StringComparison.Ordinal)) return (code, output);
            Thread.Sleep(300);
        }
    }

    static (int Code, string Output) Run(string repo, string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";   // a push wanting a password fails rather than waiting for one
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.quotepath=off");
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var errors = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60_000)) { try { p.Kill(true); } catch (InvalidOperationException) { } return (-1, "git took over a minute"); }
            return (p.ExitCode, output.Result + errors.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, "no git on PATH"); }
    }
}
