using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>Working on cards end to end: the board, the agent it starts (the fake claude) and the card file,
/// through the same paths as the buttons. Each test names the card whose behaviour it pins down.</summary>
public class CardWorkTests
{
    const string HowToCheck = "\n\n## How to check\n\nOpen the board and look at the logo.\n";

    [AvaloniaFact] // WOR-0107: Work on from a board card keeps you on the board
    public async Task Work_on_starts_an_agent_on_the_card_and_leaves_you_on_the_board()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do", "The logo's edges are jagged.");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        await app.Api("work-on", new { project = "proj", card = card.Id });

        var agent = Assert.Single(app.Agents.Agents);
        Assert.Equal(card.Id, agent.CardId);
        Assert.Equal(AgentPurposes.Work, agent.Purpose);
        Assert.Equal(Section.Board, app.Project.Section);
        Assert.Equal("In Progress", app.Read(card.Id).Status);
        await app.Until(() => app.Prompts().Count > 0, "the agent to be sent its prompt");
        var prompt = app.Prompts()[0];
        Assert.Contains(card.Id, prompt);
        Assert.Contains("Card file:", prompt);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact] // WOR-0143: an agent's header links back to its card
    public async Task An_agent_links_back_to_its_card()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = Assert.Single(app.Agents.Agents);
        Assert.True(agent.HasCard);
        Assert.Contains(card.Id, agent.CardLabel);

        await app.Api("show", new { project = "proj", section = "Agents" });
        Assert.Equal(Section.Agents, app.Project.Section);
        agent.ShowCardCommand.Execute(null);

        Assert.Equal(Section.Board, app.Project.Section);
        Assert.Equal(card.Id, app.Project.Board!.Editor?.CardId);
    }

    [AvaloniaFact] // the protocol (CLAUDE.md): permission prompts and AskUserQuestion come to Darg, and the agent gets its folders
    public async Task A_card_agent_is_started_with_the_protocol_and_its_folders()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "start"), "the agent's process to start");

        var args = app.ClaudeCalls().First(c => (string?)c["kind"] == "start")["args"]!.AsArray().Select(a => (string)a!).ToList();
        Assert.Contains("--permission-prompt-tool", args);
        Assert.Equal("stdio", args[args.IndexOf("--permission-prompt-tool") + 1]);
        Assert.Equal("stream-json", args[args.IndexOf("--input-format") + 1]);
        var dirs = args.Select((a, i) => (a, i)).Where(x => x.a == "--add-dir").Select(x => Path.GetFullPath(args[x.i + 1])).ToList();
        var project = Path.Combine(app.Repo, ".darg"); // WOR-0144: a project with a repo keeps these there
        foreach (var folder in new[] { "reports", "mistakes" })
            Assert.Contains(dirs, d => d.StartsWith(Path.Combine(project, folder), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dirs, d => d.StartsWith(Path.Combine(project, "artifacts"), StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact] // WOR-0078: a short turn that ends well notifies nobody
    public async Task A_short_turn_ends_without_a_notification()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.ForCard(card.Id)!;
        await app.Until(() => agent.State == AgentState.Idle, "the fake agent's turn to end");
        Assert.False(agent.IsFailed);
        await DargApp.Settle(300);
        Assert.Empty(Sandbox.Notifications);
    }

    [AvaloniaFact] // WOR-0079: Send back writes the notes into the card, moves it back and starts a rework agent
    public async Task Send_back_writes_the_notes_moves_the_card_and_starts_a_rework_agent()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "QA Ready", "The logo's edges are jagged." + HowToCheck);
        await app.Start();

        app.Project.Section = Section.Board;
        await app.Api("send-back", new { project = "proj", card = card.Id, notes = "still jagged at 150% zoom" });
        Assert.Equal(Section.Board, app.Project.Section); // WOR-0129: no jump to Agents

        var after = app.Read(card.Id);
        Assert.Equal("In Progress", after.Status);
        var text = File.ReadAllText(Path.Combine(app.BoardFolder, "items", after.File));
        Assert.Contains("## QA notes", text);
        Assert.Contains("still jagged at 150% zoom", text);
        var agent = app.Agents.ForCard(card.Id);
        Assert.NotNull(agent);
        Assert.Equal(AgentPurposes.Rework, agent.Purpose);
        await app.Until(() => app.Prompts().Count > 0, "the rework agent to be sent its prompt");
        Assert.StartsWith("This card came back from QA.", app.Prompts()[0]);
        Assert.Contains("still jagged at 150% zoom", app.Prompts()[0]);
    }

    [AvaloniaFact] // send-back refuses a card that isn't in QA (or the attention column)
    public async Task Send_back_refuses_a_card_still_in_progress()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "In Progress");
        await app.Start();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("send-back", new { project = "proj", card = card.Id, notes = "x" }));
        Assert.Contains("400", error.Message);
        Assert.Empty(app.Agents.Agents);
        Assert.Equal("In Progress", app.Read(card.Id).Status);
    }

    [AvaloniaFact] // WOR-0115: a reworked card back in QA gets a QA agent once the rework agent's turn has ended
    public async Task A_reworked_card_back_in_QA_gets_a_QA_agent_after_the_turn()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "4");
        try
        {
            using var app = DargApp.Make();
            var card = app.Card("fix the logo aliasing", "QA Ready", "The logo's edges are jagged." + HowToCheck);
            await app.Start();
            await app.Api("send-back", new { project = "proj", card = card.Id, notes = "still jagged" });
            var rework = app.Agents.ForCard(card.Id)!;
            await app.Until(() => rework.State == AgentState.Working, "the rework agent to start working");

            // the agent puts the card back in QA mid-turn, as a real one does as its last steps
            BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("QA Ready"));
            await DargApp.Settle(1000);
            Assert.DoesNotContain(app.Agents.Agents, a => a.Purpose == AgentPurposes.Qa); // not while it's still working

            await app.Until(() => app.Agents.Agents.Any(a => a.CardId == card.Id && a.Purpose == AgentPurposes.Qa),
                "a QA agent on the card once the rework turn ended");
            Assert.DoesNotContain(rework, app.Agents.Agents); // QA now has the card: the settled rework agent leaves (WOR-0183)
            // and its process has started: one still waiting on its model pick when the test ends starts during the next
            // test, logs into that test's fake-claude log and fails it (WOR-0131 hit this in Send back's test)
            await app.Until(() => app.Prompts().Any(p => p.Contains("QA it for me")), "the QA agent to be sent its prompt");
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact] // WOR-0115: a card reaching QA from ordinary work gets no QA agent by itself
    public async Task A_card_reaching_QA_from_ordinary_work_gets_no_QA_agent()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.ForCard(card.Id)!;
        BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("QA Ready"));
        await app.Until(() => agent.State == AgentState.Idle, "the agent's turn to end");
        await DargApp.Settle(1500);
        Assert.DoesNotContain(app.Agents.Agents, a => a.Purpose == AgentPurposes.Qa);
    }

    [AvaloniaFact] // WOR-0155: Improve shows its agent on the card and locks it, until the agent's turn ends
    public async Task Improve_puts_the_agent_on_the_card_and_locks_it_until_its_turn_ends()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo", "To Do", "jagged");
        await app.Start();
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            await app.Api("improve-card", new { project = "proj", card = card.Id });
            var agent = Assert.Single(app.Agents.Agents);
            Assert.Equal(AgentPurposes.Improve, agent.Purpose);
            Assert.Equal(card.Id, agent.CardId);
            Assert.Same(agent, app.Agents.ForCard(card.Id));

            var tile = app.Board.Show(card.Id)!;
            Assert.True(tile.HasAgent);
            Assert.False(tile.CanStart); // Work on and Improve go
            app.Board.Open(tile);
            Assert.False(app.Board.Editor!.CanWorkOn);

            await app.Api("improve-card", new { project = "proj", card = card.Id }); // a repeat click
            Assert.Single(app.Agents.Agents);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }
}
