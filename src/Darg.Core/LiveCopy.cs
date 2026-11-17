using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>A live copy of Darg (WOR-0126): started by tools/live.ps1, it runs under <c>dotnet watch</c>
/// from a worktree of its own (<see cref="Tree"/>, detached at <see cref="Branch"/>), so pulling the branch's
/// new commits into that worktree applies them while Darg runs: C# through .NET hot reload, .axaml through
/// HotAvalonia (a Debug-only package). An edit hot reload can't take (a changed signature, a new package)
/// makes dotnet watch close and restart Darg by itself.
///
/// The worktree is its own so that agents' half-made edits in the repo never reach the running app: only
/// what has landed on the branch does, and only when pulled in.
///
/// live.ps1 hands the copy these through the environment, which dotnet watch passes on.</summary>
public sealed record LiveCopy(string Tree, string Branch, string Script, int? Host)
{
    public const string TreeVar = "DARG_LIVE_TREE", BranchVar = "DARG_LIVE_BRANCH", ScriptVar = "DARG_LIVE_SCRIPT", HostVar = "DARG_LIVE_HOST";

    /// <summary>This process's live copy, or null when it isn't one.</summary>
    public static LiveCopy? FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static LiveCopy? From(Func<string, string?> env)
    {
        var tree = env(TreeVar); var branch = env(BranchVar); var script = env(ScriptVar);
        if (string.IsNullOrWhiteSpace(tree) || string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(script)) return null;
        return new LiveCopy(tree.Trim(), branch.Trim(), script.Trim(), int.TryParse(env(HostVar), out var host) ? host : null);
    }

    /// <summary>How many commits the branch has that the worktree hasn't taken in; null if git can't say.</summary>
    public int? Behind()
    {
        var (code, output) = Git(Tree, "rev-list", "--count", $"HEAD..{Branch}");
        return code == 0 && int.TryParse(output.Trim(), out var n) ? n : null;
    }

    /// <summary>Move the worktree to the branch's newest commit, which dotnet watch then applies. Returns
    /// null when done, else why not: the worktree should only ever change this way, so local edits in it
    /// are left alone rather than overwritten.</summary>
    public string? PullIn()
    {
        var (code, dirty) = Git(Tree, "status", "--porcelain", "--untracked-files=no");
        if (code != 0) return $"git couldn't read {Tree}: {dirty.Trim()}";
        if (dirty.Trim().Length > 0)
            return $"{Tree} has edits of its own, so pulling in could lose them. Commit or discard them there first:\n{dirty.TrimEnd()}";
        var (c, output) = Git(Tree, "checkout", "--detach", Branch);
        return c == 0 ? null : $"git checkout --detach {Branch} failed: {output.Trim()}";
    }

    /// <summary>The helper that restarts a live copy: Darg closes, which ends live.ps1's dotnet watch (a
    /// copy closed from its window stops the session); once both are gone, live.ps1 is started again, in a
    /// window of its own, and builds and opens the worktree as it is.</summary>
    public string HelperScript(int pid)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var sb = new StringBuilder();
        sb.AppendLine($"try {{ Wait-Process -Id {pid} -Timeout 120 -ErrorAction Stop }} catch {{ }}");
        if (Host is { } host) sb.AppendLine($"try {{ Wait-Process -Id {host} -Timeout 60 -ErrorAction Stop }} catch {{ }}");
        foreach (var v in new[] { TreeVar, BranchVar, ScriptVar, HostVar, "DOTNET_WATCH", "DOTNET_WATCH_ITERATION", "DOTNET_MODIFIABLE_ASSEMBLIES", "DARG_AGENT", "DARG_AGENT_CWD", "DARG_ADD_FOLDER" })
            sb.AppendLine($"Remove-Item -LiteralPath 'Env:{v}' -ErrorAction SilentlyContinue");
        var window = OperatingSystem.IsWindows() ? " -WindowStyle Normal" : "";
        var pwsh = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        sb.AppendLine($"Start-Process -FilePath {Q(pwsh)} -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', {Q("\"" + Script + "\"")}, '-Branch', {Q(Branch)}, '-Tree', {Q("\"" + Tree + "\"")}){window}");
        return sb.ToString();
    }

    static (int Code, string Output) Git(string root, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60_000)) { p.Kill(); return (-1, "git took over a minute"); }
            return (p.ExitCode, output.Result + error.Result);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { return (-1, e.Message); }
    }
}
