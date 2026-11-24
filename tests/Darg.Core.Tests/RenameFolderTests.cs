using System.Diagnostics;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>tools/rename-folder.ps1 (WOR-0196) on a throwaway repo: the folder moves, its worktrees inside and beside
/// it still work, and the instance, Claude Code's per-folder data, ~/.claude.json and the install's source.txt follow,
/// while a sibling folder whose name starts the same (Workbench-wor-1) is left alone.</summary>
public sealed class RenameFolderTests : IDisposable
{
    readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "darg-rename-" + Guid.NewGuid().ToString("N")[..8])).FullName;

    [Fact]
    public void The_repo_folder_and_everything_naming_it_move_to_the_new_name()
    {
        if (!OperatingSystem.IsWindows()) return;
        var from = Path.Combine(_dir, "Workbench");
        var to = Path.Combine(_dir, "Darg");
        var sibling = Path.Combine(_dir, "Workbench-wor-1");
        Directory.CreateDirectory(Path.Combine(from, "tools"));
        File.Copy(Path.Combine(RepoRoot(), "tools", "rename-folder.ps1"), Path.Combine(from, "tools", "rename-folder.ps1"));
        File.WriteAllText(Path.Combine(from, "readme.txt"), "x");
        Git(from, "init", "-q", "-b", "main");
        Git(from, "add", "-A");
        Git(from, "-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "-q", "-m", "first");
        Git(from, "worktree", "add", "-q", ".claude/worktrees/wt1", "-b", "wt1");
        Git(from, "worktree", "add", "-q", sibling, "-b", "wt2");

        var fwd = from.Replace('\\', '/');
        var inst = Directory.CreateDirectory(Path.Combine(_dir, "instance")).FullName;
        File.WriteAllText(Path.Combine(inst, "instance.json"),
            $"{{\"projects\":[{{\"repos\":[\"{fwd}\"],\"boards\":\"{fwd}/.darg/tasks\",\"other\":\"{sibling.Replace('\\', '/')}\"}}]}}");
        Directory.CreateDirectory(Path.Combine(inst, "agents", "p"));
        File.WriteAllText(Path.Combine(inst, "agents", "p", "a.json"), $"{{\"cwd\":\"{from.Replace("\\", "\\\\")}\\\\.claude\\\\worktrees\\\\wt1\"}}");

        var claude = Path.Combine(_dir, "claude");
        string Mangle(string p) => System.Text.RegularExpressions.Regex.Replace(p, "[^A-Za-z0-9]", "-");
        foreach (var d in new[] { Mangle(from), Mangle(from) + "--claude-worktrees-wt1", Mangle(sibling) })
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(claude, "projects", d)).FullName, "s.jsonl"), "{}");
        var claudeJson = Path.Combine(_dir, "claude.json");
        File.WriteAllText(claudeJson, $"{{\"projects\":{{\"{fwd}\":{{\"x\":1}}}}}}");
        var install = Directory.CreateDirectory(Path.Combine(_dir, "install")).FullName;
        File.WriteAllText(Path.Combine(install, "source.txt"), from);
        var noShortcuts = Directory.CreateDirectory(Path.Combine(_dir, "shortcuts")).FullName;

        // run from the copy inside the folder it moves, as Philip will
        var (code, output) = Run(Path.Combine(from, "tools", "rename-folder.ps1"), "-Apply", "-SkipProcessCheck",
            "-InstanceHome", inst, "-ClaudeHome", claude, "-ClaudeJson", claudeJson, "-Install", install, "-ShortcutDirs", noShortcuts);
        Assert.True(code == 0, output);

        Assert.False(Directory.Exists(from), output);
        Assert.True(File.Exists(Path.Combine(to, "readme.txt")), output);
        Assert.Contains("wt1", Git(Path.Combine(to, ".claude", "worktrees", "wt1"), "branch", "--show-current"));
        Assert.Contains("wt2", Git(sibling, "branch", "--show-current"));
        Assert.Contains(Path.Combine(to, ".claude", "worktrees", "wt1").Replace('\\', '/'), Git(to, "worktree", "list"));

        var json = File.ReadAllText(Path.Combine(inst, "instance.json"));
        Assert.Contains($"\"{to.Replace('\\', '/')}\"", json);
        Assert.Contains($"\"{to.Replace('\\', '/')}/.darg/tasks\"", json);
        Assert.Contains(sibling.Replace('\\', '/'), json); // the sibling isn't the folder
        Assert.Contains(to.Replace("\\", "\\\\") + "\\\\.claude", File.ReadAllText(Path.Combine(inst, "agents", "p", "a.json")));
        Assert.True(File.Exists(Path.Combine(inst, "instance.json.pre-rename")));

        Assert.True(Directory.Exists(Path.Combine(claude, "projects", Mangle(to))));
        Assert.True(Directory.Exists(Path.Combine(claude, "projects", Mangle(to) + "--claude-worktrees-wt1")));
        Assert.True(Directory.Exists(Path.Combine(claude, "projects", Mangle(sibling))));
        Assert.False(Directory.Exists(Path.Combine(claude, "projects", Mangle(from))));
        Assert.Contains($"\"{to.Replace('\\', '/')}\"", File.ReadAllText(claudeJson));
        Assert.Equal(to, File.ReadAllText(Path.Combine(install, "source.txt")).Trim());

        var (again, second) = Run(Path.Combine(to, "tools", "rename-folder.ps1"), "-From", from, "-To", to, "-Apply", "-SkipProcessCheck",
            "-InstanceHome", inst, "-ClaudeHome", claude, "-ClaudeJson", claudeJson, "-Install", install, "-ShortcutDirs", noShortcuts);
        Assert.True(again == 0, second); // a second run finds it all done
        Assert.Contains("already", second);
    }

    (int code, string output) Run(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _dir,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var out_ = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(120_000), "the script didn't finish");
        return (p.ExitCode, out_ + err.Result);
    }

    static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(dir);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var out_ = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} in {dir}: {err.Result}");
        return out_;
    }

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Darg.sln"))) return d.FullName;
        throw new InvalidOperationException("Couldn't find Darg.sln above " + AppContext.BaseDirectory);
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); // git's read-only objects
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }
}
