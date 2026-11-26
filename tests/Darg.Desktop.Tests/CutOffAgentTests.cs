using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0221: Darg closing, crashing or updating while a work or rework agent was mid-turn on a card left the card in
/// In Progress with no agent: the agent came back saved, counted as a finished task (WOR-0183) and was forgotten at once.
/// Now it resumes its own session with a carry-on message when Darg opens again, once per cut-off.</summary>
public class CutOffAgentTests
{
    const string HowToCheck = "\n\n## How to check\n\nLook at the logo.\n";

    /// <summary>Agents' turns long enough to be cut off (and a resumed one still at work while the test looks).</summary>
    static async Task Slow(Func<Task> body)
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "60");
        try { await body(); }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    /// <summary>How many times fake claude was started resuming this session.</summary>
    static int Resumes(DargApp app, string session) =>
        app.ClaudeCalls().Count(c => (string?)c["kind"] == "start" && c["args"]!.AsArray().Select(a => (string?)a).ToList() is var args
            && args.IndexOf("--resume") is var i and >= 0 && i + 1 < args.Count && args[i + 1] == session);

    /// <summary>Wait until the agent is mid-turn, past a tool call; its session id.</summary>
    static async Task<string> MidTurn(DargApp app, AgentViewModel agent)
    {
        await app.Until(() => agent.SessionId is not null && agent.State == AgentState.Working && agent.Items.OfType<ToolItem>().Any(),
            "the agent to be mid-turn, past a tool call");
        return agent.SessionId!;
    }

    /// <summary>The agent came back listed, resumed its own session with the carry-on message, its transcript says why,
    /// and it holds its card, still in In Progress.</summary>
    static async Task AssertCarriesOn(DargApp app, string id, string session, string cardId)
    {
        var back = app.Agents.Find(id);
        Assert.NotNull(back); // listed, not forgotten as a finished task
        await app.Until(() => Resumes(app, session) == 1, "the agent's own session to be resumed");
        await app.Until(() => app.Prompts().Contains(AgentViewModel.CutOffMessage), "the carry-on message to be sent");
        Assert.Contains(back.Items, i => i is NoteItem n && n.Text == AgentViewModel.CutOffNote);
        Assert.Equal(session, back.SessionId);
        Assert.Equal("In Progress", app.Read(cardId).Status);
        Assert.Same(back, app.Agents.ForCard(cardId)); // live on its card: Work on doesn't start another
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public Task A_work_agent_cut_off_mid_turn_resumes_its_own_session_when_Darg_reopens() => Slow(async () =>
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.ForCard(card.Id)!;
        Assert.Equal(AgentPurposes.Work, agent.Purpose);
        var session = await MidTurn(app, agent);

        await app.Reopen();

        await AssertCarriesOn(app, agent.Id, session, card.Id);
        app.Shot("resumed");
    });

    [AvaloniaFact]
    public Task A_rework_agent_from_Send_back_cut_off_mid_turn_resumes_when_Darg_reopens() => Slow(async () =>
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "QA Ready", "The logo." + HowToCheck);
        await app.Start();
        await app.Api("send-back", new { project = "proj", card = card.Id, notes = "still jagged" });
        await app.Until(() => app.Agents.ForCard(card.Id) is not null, "the rework agent to start");
        var agent = app.Agents.ForCard(card.Id)!;
        Assert.Equal(AgentPurposes.Rework, agent.Purpose);
        var session = await MidTurn(app, agent);

        await app.Reopen();

        await AssertCarriesOn(app, agent.Id, session, card.Id);
    });

    [AvaloniaFact]
    public Task A_cut_off_agent_whose_card_moved_on_while_Darg_was_closed_goes() => Slow(async () =>
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.ForCard(card.Id)!;
        var session = await MidTurn(app, agent);

        // someone moved it on (to QA) while Darg was closed: its task is over (WOR-0183)
        await app.Reopen(() => BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("QA Ready")));
        await DargApp.Settle(1500);
        AgentStore.Flush();

        Assert.Null(app.Agents.Find(agent.Id));
        Assert.False(File.Exists(Path.Combine(AgentStore.Folder("proj", app.Home), agent.Id + ".json")));
        Assert.Equal(0, Resumes(app, session));
        Assert.Equal("", app.Crashes());
    });

    [AvaloniaFact]
    public async Task A_work_agent_that_never_got_a_session_goes_after_a_restart()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "In Progress");
        var store = AgentStore.Folder("proj", app.Home);
        AgentStore.Save(store, new AgentRecord
        {
            Id = "nosession", Name = $"{card.Id} work", Cwd = app.Repo, CardId = card.Id, Purpose = AgentPurposes.Work,
            Created = DateTime.Now.AddMinutes(-5), Updated = DateTime.Now.AddMinutes(-5), Items = [new() { Kind = "user", Text = "work on it" }],
        });
        await app.Start();
        await DargApp.Settle(1500);
        AgentStore.Flush();

        Assert.Null(app.Agents.Find("nosession"));
        Assert.False(File.Exists(Path.Combine(store, "nosession.json")));
        Assert.DoesNotContain(app.ClaudeCalls(), c => (string?)c["kind"] == "start");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public Task An_agent_cut_off_again_on_its_resumed_turn_is_left_saved_not_resumed_again() => Slow(async () =>
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var first = app.Agents.ForCard(card.Id)!;
        var (id, session) = (first.Id, await MidTurn(app, first));

        await app.Reopen();
        var resumed = app.Agents.Find(id)!;
        await app.Until(() => Resumes(app, session) == 1, "the first resume");
        var tools = resumed.Items.OfType<ToolItem>().Count();
        await app.Until(() => resumed.State == AgentState.Working && resumed.Items.OfType<ToolItem>().Count() > tools, "the resumed turn to be under way");

        // a crash loop: cut off again on the turn Darg resumed it for, twice over
        for (var restart = 2; restart <= 3; restart++)
        {
            await app.Reopen();
            await DargApp.Settle(1500);
            AgentStore.Flush();
            var left = app.Agents.Find(id);
            Assert.NotNull(left); // still listed, for the person
            Assert.Equal(AgentState.Saved, left.State);
            Assert.True(left.LeftCutOff);
            Assert.Equal(1, Resumes(app, session)); // not resumed again
            Assert.Single(left.Items, i => i is NoteItem n && n.Text == AgentViewModel.CutOffAgainNote);
            Assert.True(File.Exists(Path.Combine(AgentStore.Folder("proj", app.Home), id + ".json")), $"its file after restart {restart}");
        }
        Assert.Equal("In Progress", app.Read(card.Id).Status);

        // a message from the person carries it on, as any saved agent
        var agent = app.Agents.Find(id)!;
        agent.Compose = "go on";
        agent.SendCommand.Execute(null);
        await app.Until(() => Resumes(app, session) == 2, "the person's message to resume it");
        Assert.False(agent.LeftCutOff);
        Assert.Equal("", app.Crashes());
    });
}
