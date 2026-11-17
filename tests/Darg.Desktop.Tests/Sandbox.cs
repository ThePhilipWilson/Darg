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
        var appHome = Directory.CreateDirectory(Path.Combine(Root, "_app")).FullName;
        Environment.SetEnvironmentVariable("DARG_HOME", appHome);
        Environment.SetEnvironmentVariable("WORKBENCH_HOME", null);
        foreach (var dev in new[] { "DARG_SECTION", "DARG_AGENT", "DARG_AGENT_CWD", "DARG_FLOW", "DARG_ADD_FOLDER" })
            Environment.SetEnvironmentVariable(dev, null);

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
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "1");
        Environment.SetEnvironmentVariable("FAKE_RATE", "4");
        Environment.SetEnvironmentVariable("FAKE_RESULT", "200");
        Environment.SetEnvironmentVariable("FAKE_TOUCH", "0");

        ChildProcesses.TieToThisProcess(); // fake agents die with the test run, as real ones die with the app
        Notifier.Capture = n => { lock (Notifications) Notifications.Add(n); };
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
