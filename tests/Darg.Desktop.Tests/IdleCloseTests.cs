using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0209: an agent idle past the limit gives its claude process back, stays listed and resumes on its next message.</summary>
public class IdleCloseTests
{
    static bool Alive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
    }

    static int StartPid(DargApp app, int n) => (int)app.ClaudeCalls().Where(c => (string?)c["kind"] == "start").ElementAt(n)["pid"]!;

    [AvaloniaFact]
    public async Task An_idle_agent_ends_its_process_stays_listed_and_resumes_on_a_message()
    {
        var before = IdleClose.Limit;
        try
        {
            using var app = DargApp.Make();
            await app.Start();
            IdleClose.Limit = TimeSpan.FromMilliseconds(800); // after Start, which reads the instance's setting
            var a = app.Agents.Start("say hello", app.Repo, name: "idle one");
            await app.Until(() => a.State == AgentState.Idle, "the turn to end");
            var sid = a.SessionId;
            var pid = StartPid(app, 0);
            Assert.NotNull(sid);

            await app.Until(() => a.State == AgentState.Saved, "the idle limit to close it");
            await app.Until(() => !Alive(pid), "its claude process to end");
            Assert.Contains(a, app.Agents.Agents);
            Assert.Equal("resting · send to carry on", a.StateText);
            Assert.Contains(a.Items.OfType<NoteItem>(), n => n.Text.StartsWith("Session closed by Darg (idle)"));
            Assert.Contains(a.Items.OfType<TextItem>(), _ => true); // the transcript is still there

            IdleClose.Limit = TimeSpan.Zero; // so it doesn't close again under the check
            a.Compose = "and again";
            a.SendCommand.Execute(null);
            await app.Until(() => a.State == AgentState.Idle && app.ClaudeCalls().Count(c => (string?)c["kind"] == "start") == 2, "the resumed turn to end");

            var resume = app.ClaudeCalls().Where(c => (string?)c["kind"] == "start").ElementAt(1)["args"]!.AsArray().Select(x => (string)x!).ToList();
            Assert.Equal(sid, resume[resume.IndexOf("--resume") + 1]);
            Assert.Equal(sid, a.SessionId);
            Assert.False(a.Resting);
            Assert.Contains("and again", app.Prompts().Last());
        }
        finally { IdleClose.Limit = before; }
    }

    [AvaloniaFact]
    public async Task A_message_half_typed_keeps_the_session_open()
    {
        var before = IdleClose.Limit;
        try
        {
            using var app = DargApp.Make();
            await app.Start();
            IdleClose.Limit = TimeSpan.FromMilliseconds(600);
            var a = app.Agents.Start("say hello", app.Repo, name: "typing");
            await app.Until(() => a.State == AgentState.Idle, "the turn to end");
            a.Compose = "still typing";
            await Task.Delay(2000);
            await DargApp.Settle();
            Assert.Equal(AgentState.Idle, a.State);
            Assert.True(a.Open);
        }
        finally { IdleClose.Limit = before; }
    }
}
