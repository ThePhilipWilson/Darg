using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>What keeps the integration tests off Philip's real data and machine: every instance lives
/// under the repo's .shots/integration (ignored by git), claude is tools/perf/fake_claude.py, and
/// notifications are captured instead of shown. Set once, before the app starts.</summary>
public static class Sandbox
{
    /// <summary>The repo the tests were built from (the folder with Darg.sln).</summary>
    public static string Repo { get; private set; } = "";

    /// <summary>.shots/integration: a folder per test, holding its instance, its project and its pictures.</summary>
    public static string Root { get; private set; } = "";

    /// <summary>Every notification the app raised since the current test began.</summary>
    public static List<Notifier.Notification> Notifications { get; } = [];

    static bool _done;

    public static void Init()
    {
        if (_done) return;
        _done = true;
        Repo = FindRepo();
        Root = Path.Combine(Repo, ".shots", "integration");
        Directory.CreateDirectory(Root);
        TakeFolder();
        ClearInherited();
        var appHome = Directory.CreateDirectory(Path.Combine(Root, "_app")).FullName;
        Environment.SetEnvironmentVariable("DARG_HOME", appHome);

        // the person's own ~/.claude stays out of it: no real timings in the header, settings or CLAUDE.md read
        UserHome.Folder = Directory.CreateDirectory(Path.Combine(Root, "_user")).FullName;
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Directory.CreateDirectory(Path.Combine(UserHome.Folder, ".claude")).FullName);
        Environment.SetEnvironmentVariable("ANTHROPIC_MODEL", null);
        Environment.SetEnvironmentVariable("CLAUDE_DRAW_PORT", "9"); // nothing listens: the real claude-draw daemon is left alone

        // claude is the fake: a short turn of made-up tool calls, logging what it was given (FAKE_LOG, per test)
        var fake = Path.Combine(Repo, "tools", "perf", "fake_claude.py");
        var python = Environment.GetEnvironmentVariable("DARG_TEST_PYTHON") ?? (OperatingSystem.IsWindows() ? "python" : "python3");
        string claude;
        if (OperatingSystem.IsWindows())
        {
            claude = Path.Combine(Root, "claude.cmd");
            File.WriteAllText(claude, $"@\"{python}\" \"{fake}\" %*\r\n");
        }
        else
        {
            claude = Path.Combine(Root, "claude");
            File.WriteAllText(claude, $"#!/bin/sh\nexec \"{python}\" \"{fake}\" \"$@\"\n");
            File.SetUnixFileMode(claude, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Environment.SetEnvironmentVariable("CLAUDE_PATH", claude);

        // gh is a fake too (tools/perf/fake_gh.py): the GitHub page reads a made-up repo, never GitHub itself
        var fakeGh = Path.Combine(Repo, "tools", "perf", "fake_gh.py");
        string gh;
        if (OperatingSystem.IsWindows())
        {
            gh = Path.Combine(Root, "gh.cmd");
            File.WriteAllText(gh, $"@\"{python}\" \"{fakeGh}\" %*\r\n");
        }
        else
        {
            gh = Path.Combine(Root, "gh");
            File.WriteAllText(gh, $"#!/bin/sh\nexec \"{python}\" \"{fakeGh}\" \"$@\"\n");
            File.SetUnixFileMode(gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Environment.SetEnvironmentVariable("GH_PATH", gh);
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "1");
        Environment.SetEnvironmentVariable("FAKE_RATE", "4");
        Environment.SetEnvironmentVariable("FAKE_RESULT", "200");
        Environment.SetEnvironmentVariable("FAKE_TOUCH", "0");
        Darg.Desktop.ViewModels.RemoteHost.PortOverride = 0; // phone access on any free port, never the real app's 47818 (not a DARG_ variable: a test clears those)

        ChildProcesses.TieToThisProcess(); // fake agents die with the test run, as real ones die with the app
        Notifier.Capture = n => { lock (Notifications) Notifications.Add(n); };
    }

    /// <summary>Environment variables a test run may keep: the python for the fake claude, Core's live-test switch.</summary>
    static readonly string[] Keep = ["DARG_TEST_PYTHON", "DARG_LIVE"];

    /// <summary>Clear every DARG_ and WORKBENCH_ variable this process inherited (but <see cref="Keep"/>): whatever
    /// started the run (the live copy's DARG_LIVE_*, a debug DARG_SECTION or DARG_AGENT, a DARG_INSTANCE) must
    /// not reach the app under test, which would otherwise think it is the live copy and point at the real one.
    /// Every one, not a list of known names, so a variable added later is cleared too.</summary>
    public static void ClearInherited()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
            if ((name.StartsWith("DARG_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WORKBENCH_", StringComparison.OrdinalIgnoreCase))
                && !Keep.Contains(name, StringComparer.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable(name, null);
    }

    static FileStream? _lock;

    /// <summary>One test run per repo folder at a time: each test wipes its own folder under .shots/integration,
    /// so a second run there would delete the first one's instance mid-test. A run that finds the folder taken
    /// waits for it (saying whose it is) and gives up after ten minutes. The lock goes when the process does.</summary>
    static void TakeFolder()
    {
        var path = Path.Combine(Root, ".lock");
        var end = DateTime.UtcNow.AddMinutes(10);
        var said = false;
        while (true)
        {
            try
            {
                _lock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                _lock.SetLength(0);
                var me = System.Text.Encoding.UTF8.GetBytes($"pid {Environment.ProcessId}, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _lock.Write(me);
                _lock.Flush();
                return;
            }
            catch (IOException)
            {
                var who = Holder(path);
                if (DateTime.UtcNow > end)
                    throw new InvalidOperationException($"Another integration test run has had {Root} for ten minutes ({who}). Not running alongside it.");
                if (!said) { Console.Error.WriteLine($"Waiting for another integration test run in this folder to finish ({who})."); said = true; }
                Thread.Sleep(1000);
            }
        }
    }

    static string Holder(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(f);
            return r.ReadToEnd();
        }
        catch (IOException) { return "unknown"; }
    }

    /// <summary>Throws unless the app's instance folder is inside the sandbox: a test must never reach the real one.</summary>
    public static void Check()
    {
        var folder = Path.GetFullPath(InstanceConfig.Folder());
        if (!folder.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The instance folder is {folder}, outside the test sandbox {Root}. Refusing to run.");
    }

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Darg.sln"))) return d.FullName;
        throw new InvalidOperationException("Couldn't find Darg.sln above " + AppContext.BaseDirectory);
    }
}
