using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>The app under test is cut off from whatever started the run and from the person's own files (WOR-0127).</summary>
public class SandboxTests
{
    // Run from the live copy (as Darg's own agents are), the app under test thought it was the live copy:
    // ⟳ live in its header, pointing at the real worktree and host process.
    [AvaloniaFact]
    public async Task The_app_under_test_is_not_the_live_copy()
    {
        using var app = DargApp.Make();
        await app.Start();
        Assert.Null(app.Main.Restart.Live);
    }

    // The header showed the real "today 6h 12m" read from ~/.claude/stats/timings, so every picture differed.
    [AvaloniaFact]
    public async Task The_header_reads_no_real_timings()
    {
        using var app = DargApp.Make();
        await app.Start();
        Assert.StartsWith(Path.GetFullPath(Sandbox.Root), Path.GetFullPath(UserHome.Folder), StringComparison.OrdinalIgnoreCase);
        Assert.Null(WorkTimer.FromConfig()); // what the header's UsageViewModel reads: null hides "today"
        await DargApp.Settle(500);
        Assert.Equal("", app.Main.Usage.Today);
    }

    [AvaloniaFact]
    public void Every_inherited_darg_variable_is_cleared()
    {
        var home = Environment.GetEnvironmentVariable("DARG_HOME");
        try
        {
            foreach (var name in new[] { "DARG_LIVE_TREE", "DARG_LIVE_HOST", "DARG_SECTION", "DARG_INSTANCE", "DARG_SOMETHING_NEW", "WORKBENCH_HOME" })
                Environment.SetEnvironmentVariable(name, "x");
            Sandbox.ClearInherited();
            foreach (var name in new[] { "DARG_LIVE_TREE", "DARG_LIVE_HOST", "DARG_SECTION", "DARG_INSTANCE", "DARG_SOMETHING_NEW", "WORKBENCH_HOME" })
                Assert.Null(Environment.GetEnvironmentVariable(name));
        }
        finally { Environment.SetEnvironmentVariable("DARG_HOME", home); }
    }
}
