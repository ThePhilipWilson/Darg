using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0183: the Agents list kept every agent a newer one had replaced on a card (5 on one card, 3 QA agents
/// closed by Darg) and every finished Improve agent. A new work, rework or QA agent lets the older settled ones go; a QA
/// verdict stays until then; an Improve agent goes when its turn ends well; Clear finished takes what had piled up.</summary>
public class ReplacedAgentTests
{
    const string HowToCheck = "\n\n## How to check\n\nLook at the logo.\n";

    [AvaloniaFact]
    public async Task A_work_agent_leaves_the_list_once_its_turn_ends_well()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var work = app.Agents.ForCard(card.Id)!;
        Assert.Equal(AgentPurposes.Work, work.Purpose);

        await app.Until(() => !app.Agents.Agents.Contains(work), "the work agent to go once its turn ended");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_QA_agent_leaves_the_list_once_it_has_given_its_verdict()
    {
        using var app = DargApp.Make();
        var card = app.Card("check the logo", "QA Ready", "The logo." + HowToCheck);
        await app.Start();
        await app.Api("qa-all", new { project = "proj" });
        var qa = app.Agents.Agents.Single(a => a.CardId == card.Id && a.Purpose == AgentPurposes.Qa);

        await app.Until(() => !app.Agents.Agents.Contains(qa), "the QA agent to go once its turn ended");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task An_agent_whose_card_waits_on_the_person_stays_after_its_turn()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "3");
        try
        {
            using var app = DargApp.Make();
            var card = app.Card("fix the logo aliasing", "To Do");
            await app.Start();
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = app.Agents.ForCard(card.Id)!;
            await app.Until(() => agent.State == AgentState.Working, "the agent to start working");
            Assert.Null(app.Board.MoveToAttention(card.Id));

            await app.Until(() => agent.State == AgentState.Idle, "the agent's turn to end");
            await DargApp.Settle(1000);
            Assert.Contains(agent, app.Agents.Agents); // it asked the person something: it stays for the answer
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task A_saved_agent_whose_card_waits_on_the_person_stays_after_a_restart()
    {
        // QA round 1: the first check at start ran before the project knew which cards wait on the person, so every
        // asker on a card in For Human Attention was forgotten, file and all, on each restart
        using var app = DargApp.Make();
        var card = app.Card("voice mode", "For Human Attention",
            "Voice.\n\n## Decision\n\n**Question:** Keep it?\n**Asked by:** agent asker1, 2026-11-22\n- **Yes** (recommended) → done: keep.\n- **No** → back: redo.\n**Context:** test.\n" + HowToCheck);
        var store = AgentStore.Folder("proj", app.Home);
        AgentStore.Save(store, new AgentRecord
        {
            Id = "asker1", Name = $"{card.Id} asker", Cwd = app.Repo, CardId = card.Id, Purpose = AgentPurposes.Work, SessionId = "s1",
            Created = DateTime.Now.AddHours(-1), Updated = DateTime.Now.AddHours(-1),
            Items = [new ItemRecord { Kind = "note", Text = "Turn done" }],
        });
        await app.Start();
        await DargApp.Settle(1500);

        Assert.Contains(app.Agents.Agents, a => a.Id == "asker1");
        Assert.True(File.Exists(Path.Combine(store, "asker1.json")));
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_finished_task_agent_is_not_saved_back_once_it_has_gone()
    {
        // QA round 1: closing a forgotten agent's session ends it a moment later, and that save wrote its file back
        using var app = DargApp.Make();
        var card = app.Card("check the logo", "QA Ready", "The logo." + HowToCheck);
        await app.Start();
        await app.Api("qa-all", new { project = "proj" });
        var qa = app.Agents.Agents.Single(a => a.CardId == card.Id && a.Purpose == AgentPurposes.Qa);
        var file = Path.Combine(AgentStore.Folder("proj", app.Home), qa.Id + ".json");

        await app.Until(() => !app.Agents.Agents.Contains(qa), "the QA agent to go once its turn ended");
        await app.Until(() => qa.State == AgentState.Saved || qa.State == AgentState.Exited, "its session to end");
        await DargApp.Settle(1500);
        AgentStore.Flush();
        Assert.False(File.Exists(file), "the forgotten QA agent's file came back");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task An_older_agent_still_at_work_stays_then_goes_once_its_turn_ends()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "4");
        try
        {
            using var app = DargApp.Make();
            var card = app.Card("fix the logo aliasing", "To Do");
            await app.Start();
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var older = app.Agents.ForCard(card.Id)!;
            await app.Until(() => older.State == AgentState.Working, "the first agent to start working");

            var newer = app.Agents.Start("check it", app.Repo, cardId: card.Id, name: "check", purpose: AgentPurposes.Qa);
            await DargApp.Settle(500);
            Assert.Contains(older, app.Agents.Agents); // working: not touched

            await app.Until(() => !app.Agents.Agents.Contains(older), "the older agent to go once its turn ended");
            await app.Until(() => app.Prompts().Any(p => p.Contains("check it")), "the newer agent to be sent its prompt");
            Assert.Contains(newer, app.Agents.Agents);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task An_Improve_agent_leaves_the_list_once_its_turn_ends_well()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo", "To Do", "jagged");
        await app.Start();
        await app.Api("improve-card", new { project = "proj", card = card.Id });
        var improver = app.Agents.Agents.Single();
        Assert.Equal(AgentPurposes.Improve, improver.Purpose);

        await app.Until(() => !app.Agents.Agents.Contains(improver), "the Improve agent to go once its turn ended");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Clear_finished_takes_settled_agents_a_newer_one_has_replaced()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        var store = AgentStore.Folder("proj", app.Home);
        void Seed(string id, string? purpose, int daysAgo, bool turnDone = true) => AgentStore.Save(store, new AgentRecord
        {
            Id = id, Name = $"{card.Id} {id}", Cwd = app.Repo, CardId = card.Id, Purpose = purpose,
            Created = DateTime.Now.AddDays(-daysAgo), Updated = DateTime.Now.AddDays(-daysAgo),
            Items = turnDone ? [new ItemRecord { Kind = "note", Text = "Turn done" }] : [],
        });
        Seed("oldchat", null, 3); // a chat on the card, settled
        Seed("newwork", AgentPurposes.Work, 1, turnDone: false); // work started since, its turn not ended: stays
        await app.Start();
        Assert.Equal(2, app.Agents.Agents.Count(a => a.CardId == card.Id)); // loaded as they were left

        app.Agents.ClearFinishedCommand.Execute(null);

        var left = Assert.Single(app.Agents.Agents, a => a.CardId == card.Id);
        Assert.Equal("newwork", left.Id); // the settled agent a newer work agent replaced goes
        Assert.Equal("", app.Crashes());
    }
}
