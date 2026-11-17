using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

public sealed class LiveCopyTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-live-").FullName;

    [Fact]
    public void Is_a_live_copy_only_when_live_ps1_said_so()
    {
        var env = new Dictionary<string, string?>();
        Assert.Null(LiveCopy.From(k => env.GetValueOrDefault(k)));
        env[LiveCopy.TreeVar] = @"D:\repo\.claude\worktrees\live";
        env[LiveCopy.BranchVar] = "main";
        Assert.Null(LiveCopy.From(k => env.GetValueOrDefault(k))); // no script to restart through
        env[LiveCopy.ScriptVar] = @"D:\repo\tools\live.ps1";
        env[LiveCopy.HostVar] = "4242";
        var live = LiveCopy.From(k => env.GetValueOrDefault(k));
        Assert.Equal(new LiveCopy(@"D:\repo\.claude\worktrees\live", "main", @"D:\repo\tools\live.ps1", 4242), live);
    }

    [Fact]
    public void Pulls_in_the_branchs_new_commits_but_never_over_its_own_edits()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_dir, "repo")).FullName;
        Git(repo, "init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "one");
        Git(repo, "add", "."); Git(repo, "commit", "-q", "-m", "one");
        var tree = Path.Combine(_dir, "live");
        Git(repo, "worktree", "add", "-q", "--detach", tree, "main");
        var live = new LiveCopy(tree, "main", "live.ps1", null);
        Assert.Equal(0, live.Behind());

        File.WriteAllText(Path.Combine(repo, "a.txt"), "two");
        Git(repo, "commit", "-q", "-am", "two");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "three");
        Git(repo, "commit", "-q", "-am", "three");
        Assert.Equal(2, live.Behind());

        File.WriteAllText(Path.Combine(tree, "a.txt"), "edited in the live tree");
        Assert.Contains("edits of its own", live.PullIn());
        Assert.Equal("edited in the live tree", File.ReadAllText(Path.Combine(tree, "a.txt")));

        Git(tree, "checkout", "--", "a.txt");
        Assert.Null(live.PullIn());
        Assert.Equal("three", File.ReadAllText(Path.Combine(tree, "a.txt")));
        Assert.Equal(0, live.Behind());
        Assert.Equal("main", Git(repo, "branch", "--show-current").Trim()); // the worktree holds no branch
    }

    [Fact]
    public void Restarts_through_live_ps1_once_darg_and_its_watch_have_gone()
    {
        var script = new LiveCopy(@"D:\my repo\.claude\worktrees\live", "main", @"D:\my repo\tools\live.ps1", 77).HelperScript(55);
        Assert.Contains("Wait-Process -Id 55", script);
        Assert.Contains("Wait-Process -Id 77", script);
        Assert.Contains("Remove-Item -LiteralPath 'Env:DARG_LIVE_TREE'", script);
        Assert.Contains(@"'""D:\my repo\tools\live.ps1""'", script);
        Assert.Contains("'-Branch', 'main'", script);
        Assert.True(script.IndexOf("Wait-Process -Id 77") < script.IndexOf("Start-Process"));
    }

    static string Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@example.com" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)}: {error}");
        return output;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
