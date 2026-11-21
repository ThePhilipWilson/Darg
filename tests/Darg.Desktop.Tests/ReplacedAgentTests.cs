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
    public async Task A_new_rework_agent_takes_the_settled_work_agent_off_the_list()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var work = app.Agents.ForCard(card.Id)!;
        await app.Until(() => work.State == AgentState.Idle, "the work agent's turn to end");
        BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("QA Ready"));
        await DargApp.Settle(500);
        Assert.Contains(work, app.Agents.Agents); // its work is still the newest on the card: it stays

        await app.Api("send-back", new { project = "proj", card = card.Id, notes = "still jagged" });

        var rework = app.Agents.Agents.Single(a => a.CardId == card.Id && a.Purpose == AgentPurposes.Rework);
        Assert.DoesNotContain(work, app.Agents.Agents);
        await app.Until(() => app.Prompts().Any(p => p.Contains("still jagged")), "the rework agent to be sent its prompt");
        Assert.Contains(rework, app.Agents.Agents);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_QA_verdict_stays_listed_until_the_next_agent_on_the_card_starts()
    {
        using var app = DargApp.Make();
        var card = app.Card("check the logo", "QA Ready", "The logo." + HowToCheck);
        await app.Start();
        await app.Api("qa-all", new { project = "proj" });
        var qa = app.Agents.Agents.Single(a => a.CardId == card.Id && a.Purpose == AgentPurposes.Qa);
        await app.Until(() => qa.FinishedTurn, "the QA agent's turn to end");
        await DargApp.Settle(1000); // the board reloads, which closes it ("Session closed by Darg")
        Assert.Contains(qa, app.Agents.Agents); // the verdict stays readable

        await app.Api("send-back", new { project = "proj", card = card.Id, notes = "wrong" });

        await app.Until(() => !app.Agents.Agents.Contains(qa), "the QA agent to go once a rework agent replaced it");
        await app.Until(() => app.Prompts().Any(p => p.Contains("wrong")), "the rework agent to be sent its prompt");
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
        void Seed(string id, string purpose, int daysAgo) => AgentStore.Save(store, new AgentRecord
        {
            Id = id, Name = $"{card.Id} {id}", Cwd = app.Repo, CardId = card.Id, Purpose = purpose,
            Created = DateTime.Now.AddDays(-daysAgo), Updated = DateTime.Now.AddDays(-daysAgo),
            Items = [new ItemRecord { Kind = "note", Text = "Turn done" }],
        });
        Seed("oldwork", AgentPurposes.Work, 3);
        Seed("oldqa", AgentPurposes.Qa, 2);
        Seed("newestqa", AgentPurposes.Qa, 1);
        await app.Start();
        Assert.Equal(3, app.Agents.Agents.Count(a => a.CardId == card.Id)); // loaded as they were left

        app.Agents.ClearFinishedCommand.Execute(null);

        var left = Assert.Single(app.Agents.Agents, a => a.CardId == card.Id);
        Assert.Equal("newestqa", left.Id); // the newest settled one stays: nothing has replaced it
        Assert.Equal("", app.Crashes());
    }
}
