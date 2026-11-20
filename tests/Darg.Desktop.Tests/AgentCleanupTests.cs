using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0170: the heavy things an agent leaves running go when its session does. One night agents left an Android
/// emulator (started with Start-Process, whose shell then exited) and debug copies of Darg running after they were done;
/// they lived on until Darg closed, and with the emulator leaking, Windows ran out of memory and the PC bluescreened.</summary>
public class AgentCleanupTests
{
    /// <summary>The id the fake wrote, or null while it isn't there yet or pwsh is still writing it.</summary>
    static int? ReadPid(string file)
    {
        try { return File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var id) ? id : null; }
        catch (IOException) { return null; }
    }

    [AvaloniaFact]
    public async Task What_an_agent_left_running_ends_when_its_session_is_closed()
    {
        using var app = DargApp.Make();
        var pidFile = Path.Combine(app.Folder, "spawned.pid");
        Process? left = null;
        var leftBehind = AgentSession.LeftBehind;
        // the fake leaves a Windows PowerShell running, standing in for an emulator: no test starts a real one
        AgentSession.LeftBehind = n => leftBehind(n) || n.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase);
        try
        {
            await app.Start();
            await app.Api("start-agent", new { project = "proj", prompt = $"start the emulator and leave it up FAKE_SPAWN {pidFile}" });
            var agent = app.Agents.Agents.Single();
            int? pid = null;
            await app.Until(() => (pid = ReadPid(pidFile)) is not null, "the agent to leave a process running");
            left = Process.GetProcessById(pid!.Value);
            await app.Until(() => agent.State == AgentState.Idle, "the agent's turn to end");
            await DargApp.Settle(500);
            Assert.False(left.HasExited); // the turn ending leaves it: the agent may be using it next turn

            await app.Api("close-agents", new { ids = agent.Id });

            await app.Until(() => left.HasExited, "the process the agent left to end with its session", seconds: 15);
            Assert.Equal("", app.Crashes());
        }
        finally
        {
            AgentSession.LeftBehind = leftBehind;
            try { if (left is { HasExited: false }) left.Kill(); } catch (Exception) { }
        }
    }
}
