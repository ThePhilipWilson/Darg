using Avalonia.Headless.XUnit;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>A project that has closed starts no agent, whatever was still queued to (WOR-0131). Work a test's project
/// had queued (a send-back's rework agent picking its model, a message due when a turn ended) started agents during
/// the next test, logging into its fake-claude log and failing it.</summary>
public class ClosedProjectTests
{
    [AvaloniaFact]
    public async Task A_closed_project_starts_no_agent_and_resumes_none()
    {
        using var app = DargApp.Make();
        await app.Start();
        var ran = app.Agents.Start("a chat that finishes its turn");
        await app.Until(() => ran.State == AgentState.Idle && ran.SessionId is not null, "the first turn to end");
        var agents = app.Agents;

        app.Project.Close();
        var late = agents.Start("started by work queued before the project closed");
        ran.Say("carry on", "carry on"); // a message to an ended agent with a session resumes it
        await DargApp.Settle(3000);

        Assert.Single(app.ClaudeCalls(), c => (string?)c["kind"] == "start"); // the first turn's, and no other
        Assert.DoesNotContain(late, agents.Agents);
        Assert.NotEqual(AgentState.Working, late.State);
        Assert.NotEqual(AgentState.Working, ran.State);
    }
}
