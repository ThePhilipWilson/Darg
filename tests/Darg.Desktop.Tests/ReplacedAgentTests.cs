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

    static string DecisionBy(string agent) =>
        $"\n\n## Decision\n\n**Question:** Keep it?\n**Asked by:** agent {agent}, 2026-11-22\n- **Yes** (recommended) → done: keep.\n- **No** → back: redo.\n**Context:** test.\n";

    [AvaloniaFact]
    public async Task An_agent_whose_card_waits_on_the_person_goes_after_its_turn_and_the_decision_stays_on_the_card()
    {
        // Philip, 2026-11-22: the agent that asked a card's decision goes as well; the decision stays on the card
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "3");
        try
        {
            using var app = DargApp.Make();
            var card = app.Card("fix the logo aliasing", "To Do");
            await app.Start();
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = app.Agents.ForCard(card.Id)!;
            await app.Until(() => agent.State == AgentState.Working, "the agent to start working");
            // as a card agent's rules have it: the decision written, then the card moved
            var file = Path.Combine(app.BoardFolder, "items", card.File);
            File.WriteAllText(file, File.ReadAllText(file) + DecisionBy(agent.Id));
            Assert.Null(app.Board.MoveToAttention(card.Id));

            await app.Until(() => !app.Agents.Agents.Contains(agent), "the asker to go once its turn ended");
            Assert.Contains(app.Board.OpenDecisions(), d => d.Card.Id == card.Id); // the card still waits on its decision
            await app.Api("decide", new { project = "proj", card = card.Id, answer = "Yes" });
            Assert.Equal("Done", app.Read(card.Id).Status); // and a pick still runs its action
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task A_saved_agent_whose_card_waits_on_the_person_goes_after_a_restart()
    {
        // the restart keeps the card's decision, not the agent that asked it (Philip, 2026-11-22)
        using var app = DargApp.Make();
        var card = app.Card("voice mode", "For Human Attention", "Voice." + DecisionBy("asker1") + HowToCheck);
        var store = AgentStore.Folder("proj", app.Home);
        AgentStore.Save(store, new AgentRecord
        {
            Id = "asker1", Name = $"{card.Id} asker", Cwd = app.Repo, CardId = card.Id, Purpose = AgentPurposes.Work, SessionId = "s1",
            Created = DateTime.Now.AddHours(-1), Updated = DateTime.Now.AddHours(-1),
            Items = [new ItemRecord { Kind = "note", Text = "Turn done" }],
        });
        await app.Start();
        await DargApp.Settle(1500);
        AgentStore.Flush();

        Assert.DoesNotContain(app.Agents.Agents, a => a.Id == "asker1");
        Assert.False(File.Exists(Path.Combine(store, "asker1.json")));
        Assert.Contains(app.Board.OpenDecisions(), d => d.Card.Id == card.Id);
        Assert.Equal("", app.Crashes());
    }

    /// <summary>The kinds of saved agent a restart meets on one card: id, purpose, transcript, set to carry on after a reset.</summary>
    static readonly (string Id, string Purpose, ItemRecord[] Items, bool CarriesOn)[] Saved =
    [
        // cut off mid-turn by a crash: the last item a tool call, its text, the prompt itself or nothing at all
        ("midtool", AgentPurposes.Qa, [new() { Kind = "user", Text = "check it" }, new() { Kind = "text", Text = "Running the suite." }, new() { Kind = "tool", Name = "Bash", Summary = "test" }], false),
        ("midtext", AgentPurposes.Rework, [new() { Kind = "user", Text = "fix it" }, new() { Kind = "text", Text = "Looking." }], false),
        ("promptonly", AgentPurposes.Work, [new() { Kind = "user", Text = "work on it" }], false),
        ("profileonly", AgentPurposes.Work, [new() { Kind = "user", Text = "work on it" }, new() { Kind = "note", Text = "Profile Standard (sonnet) · Haiku: x" }], false),
        ("empty", AgentPurposes.Improve, [], false),
        // closed mid-turn (Close, an update)
        ("closedmid", AgentPurposes.Work, [new() { Kind = "user", Text = "work" }, new() { Kind = "tool", Name = "Read" }, new() { Kind = "note", Text = "Session closed by Darg mid-turn. Send a message to carry on." }], false),
        // these stay: a failed turn (the person sees why), a session that died, one set to carry on after a usage reset
        ("failed", AgentPurposes.Work, [new() { Kind = "user", Text = "work" }, new() { Kind = "note", Text = "API Error: 500", Bad = true }, new() { Kind = "note", Text = "Turn done" }, new() { Kind = "note", Text = "Session closed by Darg. Send a message to carry on." }], false),
        ("died", AgentPurposes.Qa, [new() { Kind = "user", Text = "check" }, new() { Kind = "note", Text = "Session ended (1) boom", Bad = true }], false),
        ("carrieson", AgentPurposes.Work, [new() { Kind = "user", Text = "work" }, new() { Kind = "note", Text = "You've hit your limit", Bad = true }], true),
    ];
    static readonly string[] Stay = ["failed", "died", "carrieson"];

    static void SeedSaved(DargApp app, Card card, string store)
    {
        var n = 0;
        foreach (var (id, purpose, items, carriesOn) in Saved)
            AgentStore.Save(store, new AgentRecord
            {
                Id = id, Name = $"{card.Id} {id}", Cwd = app.Repo, CardId = card.Id, Purpose = purpose, SessionId = "s-" + id,
                Created = DateTime.Now.AddHours(-1).AddMinutes(n++), Updated = DateTime.Now.AddHours(-1),
                ContinueAt = carriesOn ? DateTimeOffset.Now.AddHours(2) : null, Items = [.. items],
            });
        // a chat on the card, cut off mid-turn: chats are left alone
        AgentStore.Save(store, new AgentRecord
        {
            Id = "chat", Name = "a chat", Cwd = app.Repo, CardId = card.Id, Purpose = AgentPurposes.Chat, SessionId = "s-chat",
            Created = DateTime.Now.AddMinutes(-5), Updated = DateTime.Now.AddMinutes(-5), Items = [new() { Kind = "user", Text = "hi" }],
        });
    }

    [AvaloniaFact]
    public async Task A_saved_task_agent_cut_off_mid_turn_goes_after_a_restart_with_its_file()
    {
        // QA round 2 on Philip's instance: agents cut off by a crash or an update, or saved with only their prompt,
        // never went, since only a turn that had ended counted as finished
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "In Progress");
        var store = AgentStore.Folder("proj", app.Home);
        SeedSaved(app, card, store);
        await app.Start();
        await DargApp.Settle(1500);
        AgentStore.Flush();

        var left = app.Agents.Agents.Where(a => a.CardId == card.Id).Select(a => a.Id).Order().ToList();
        Assert.Equal([.. Stay.Append("chat").Order()], left);
        foreach (var (id, _, _, _) in Saved)
            Assert.True(Stay.Contains(id) == File.Exists(Path.Combine(store, id + ".json")), $"{id}'s file: kept {File.Exists(Path.Combine(store, id + ".json"))}");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Clear_finished_takes_every_task_agent_that_isnt_running()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "In Progress");
        var store = Path.Combine(app.Home, "seeded-agents");
        Directory.CreateDirectory(store);
        await app.Start();
        SeedSaved(app, card, store);
        AgentStore.Flush();
        // read as a restart reads them, but kept off the list (which takes them at once): what Clear finished would take
        var agents = AgentStore.List(store).Select(r => new AgentViewModel(r, store)).ToList();
        Assert.Equal(Saved.Length + 1, agents.Count);
        var finished = agents.Where(app.Agents.IsFinished).Select(a => a.Id).Order().ToList();
        Assert.Equal([.. Saved.Select(s => s.Id).Except(Stay).Order()], finished);
        foreach (var a in agents) a.Shut();
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

            // a longer turn, so the newer one is still at work when the older one goes (a task agent leaves once it stops)
            Environment.SetEnvironmentVariable("FAKE_SECONDS", "12");
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
        void Seed(string id, string? purpose, int daysAgo, DateTimeOffset? carriesOn = null) => AgentStore.Save(store, new AgentRecord
        {
            Id = id, Name = $"{card.Id} {id}", Cwd = app.Repo, CardId = card.Id, Purpose = purpose, SessionId = "s-" + id,
            Created = DateTime.Now.AddDays(-daysAgo), Updated = DateTime.Now.AddDays(-daysAgo), ContinueAt = carriesOn,
            Items = [new ItemRecord { Kind = "note", Text = "Turn done" }],
        });
        Seed("oldchat", null, 3); // an agent from before agents had a purpose, settled
        Seed("newwork", AgentPurposes.Work, 1, DateTimeOffset.Now.AddHours(2)); // work started since, set to carry on after a usage reset: stays
        await app.Start();
        Assert.Equal(2, app.Agents.Agents.Count(a => a.CardId == card.Id)); // loaded as they were left

        app.Agents.ClearFinishedCommand.Execute(null);

        var left = Assert.Single(app.Agents.Agents, a => a.CardId == card.Id);
        Assert.Equal("newwork", left.Id); // the settled agent a newer work agent replaced goes
        Assert.Equal("", app.Crashes());
    }
}
