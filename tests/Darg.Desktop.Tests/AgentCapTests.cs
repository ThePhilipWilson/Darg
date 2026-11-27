using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0245: a project's most agents at work at once. Past it a new agent waits to start (shown in the list and on its
/// card), and the waiting ones start by themselves, first asked first, as agents at work stop; a restart keeps them waiting.</summary>
public class AgentCapTests
{
    static CardViewModel Tile(DargApp app, string id) =>
        app.Board.Rows.SelectMany(r => r.Lists).SelectMany(l => l.Cards).First(c => c.Id == id);

    static int Starts(DargApp app) => app.ClaudeCalls().Count(c => (string?)c["kind"] == "start");

    [AvaloniaFact]
    public async Task Past_the_cap_new_agents_wait_and_start_in_order_as_others_stop()
    {
        using var app = DargApp.Make();
        var a = app.Card("first card", "To Do");
        var b = app.Card("second card", "To Do");
        var c = app.Card("third card", "To Do", "Thin.");
        await app.Start();
        await app.Api("max-agents", new { project = "proj", max = 1 });
        var first = app.HoldTurns(name: "first");

        await app.Api("work-on", new { project = "proj", card = a.Id });
        await app.Api("work-on", new { project = "proj", card = b.Id });
        await app.Api("improve-card", new { project = "proj", card = c.Id }); // every way in is held, not only Work on

        AgentViewModel On(string id) => app.Agents.Agents.First(x => x.CardId == id);
        await app.Until(() => On(a.Id).State == AgentState.Working, "the first agent to be at work");
        Assert.True(On(b.Id).IsPending);
        Assert.True(On(c.Id).IsPending);
        Assert.Equal("waiting to start · 1st in line", On(b.Id).StateText);
        Assert.Equal("waiting to start · 2nd in line", On(c.Id).StateText);
        Assert.Contains("most at once is 1", On(b.Id).Activity);
        Assert.Equal(1, Starts(app)); // nothing of the waiting ones' runs yet
        // its card shows it waiting, and Work on isn't offered again
        app.Board.ReloadAll();
        await app.Until(() => Tile(app, b.Id).Agent?.IsPending == true, "the second card's tile to show its waiting agent");
        Assert.Equal(CardState.Waiting, Tile(app, b.Id).State);
        Assert.False(Tile(app, b.Id).CanStart);
        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("work-on", new { project = "proj", card = b.Id }));
        Assert.Contains("already has an agent", again.Message);
        Assert.Contains("1 at work, 2 waiting", await app.Api("max-agents", new { project = "proj" }));
        await app.Api("show", new { project = "proj", section = "Board" });
        app.Shot("board-waiting");
        await app.Api("show", new { project = "proj", agent = On(b.Id).Id });
        app.Shot("agents-waiting");

        // the first stops: the one asked for next starts, the last still waits
        var second = app.HoldTurns(name: "second");
        first.Release();
        await app.Until(() => app.Agents.Agents.FirstOrDefault(x => x.CardId == b.Id)?.State == AgentState.Working, "the second agent to start once the first stopped");
        Assert.True(On(c.Id).IsPending);
        Assert.Equal("waiting to start · 1st in line", On(c.Id).StateText);
        await app.Until(() => app.Prompts().Any(p => p.Contains(b.Id)), "the second agent to be sent its card");
        Assert.Equal(2, Starts(app));
        Assert.DoesNotContain(app.Prompts(), p => p.Contains(c.Id));

        second.Release();
        await app.Until(() => app.Prompts().Any(p => p.Contains(c.Id)), "the third agent to start once the second stopped");
        Assert.Equal(3, Starts(app));
        second.Dispose();
        first.Dispose();
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Taking_the_cap_off_starts_every_waiting_agent()
    {
        using var app = DargApp.Make();
        var a = app.Card("first card", "To Do");
        var b = app.Card("second card", "To Do");
        await app.Start();
        await app.Api("max-agents", new { project = "proj", max = 1 });
        using var hold = app.HoldTurns();
        await app.Api("work-on", new { project = "proj", card = a.Id });
        await app.Api("work-on", new { project = "proj", card = b.Id });
        Assert.Single(app.Agents.Pending);

        await app.Api("max-agents", new { project = "proj", max = 0 });

        Assert.Empty(app.Agents.Pending);
        await app.Until(() => Starts(app) == 2, "the waiting agent to start once the cap was taken off");
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(app.Home, "instance.json")))!["projects"]![0]!;
        Assert.Null(saved["maxAgents"]);
    }

    [AvaloniaFact]
    public async Task Start_now_starts_a_waiting_agent_past_the_cap()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("max-agents", new { project = "proj", max = 1 });
        using var hold = app.HoldTurns();
        app.Agents.Start("one chat", purpose: AgentPurposes.Chat);
        var waiting = app.Agents.Start("another chat", purpose: AgentPurposes.Chat); // chats are held too
        Assert.True(waiting.IsPending);
        waiting.Say("hello?", "hello?"); // not sent: it has no session yet
        Assert.True(waiting.IsPending);

        waiting.StartNowCommand.Execute(null);

        await app.Until(() => app.Prompts().Contains("another chat"), "Start now to start it");
        Assert.Empty(app.Agents.Pending);
        await app.Until(() => Starts(app) == 2, "both chats' processes to be logged"); // either may log first
        Assert.DoesNotContain("hello?", app.Prompts());
    }

    [AvaloniaFact]
    public async Task Changing_the_cap_in_Setup_keeps_the_agents_running()
    {
        using var app = DargApp.Make();
        var a = app.Card("first card", "To Do");
        await app.Start();
        using var hold = app.HoldTurns();
        await app.Api("work-on", new { project = "proj", card = a.Id });
        var agents = app.Agents;
        var agent = Assert.Single(agents.Agents);
        await app.Until(() => agent.State == AgentState.Working, "the agent to be at work");

        app.Main.OpenSetupCommand.Execute(null);
        var setup = app.Main.Setup!;
        Assert.Equal("", setup.Selected!.MaxAgentsText);
        setup.Selected.MaxAgentsText = "2";
        setup.SaveCommand.Execute(null);

        Assert.Same(agents, app.Agents); // the project wasn't made again, which would have ended its agents
        Assert.Equal(AgentState.Working, agent.State);
        Assert.Equal(2, app.Project.Config.MaxAgents);
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(app.Home, "instance.json")))!["projects"]![0]!;
        Assert.Equal(2, (int)saved["maxAgents"]!);
    }

    [AvaloniaFact]
    public async Task A_waiting_agent_still_waits_after_a_restart_and_starts_in_its_turn()
    {
        using var app = DargApp.Make();
        var a = app.Card("first card", "To Do");
        var b = app.Card("second card", "To Do");
        await app.Start();
        await app.Api("max-agents", new { project = "proj", max = 1 });
        var hold = app.HoldTurns();
        await app.Api("work-on", new { project = "proj", card = a.Id });
        await app.Api("work-on", new { project = "proj", card = b.Id });
        await app.Until(() => app.Agents.Agents.First(x => x.CardId == a.Id).State == AgentState.Working, "the first agent to be at work");

        await app.Reopen(); // the first, cut off mid-turn, carries on (WOR-0221); the second waits again

        await app.Until(() => app.Agents.Agents.FirstOrDefault(x => x.CardId == a.Id)?.State == AgentState.Working, "the first agent to carry on");
        var waiting = app.Agents.Agents.First(x => x.CardId == b.Id);
        Assert.True(waiting.IsPending);
        await app.Until(() => waiting.PendingPlace == 1, "it to be told its place");
        Assert.DoesNotContain(app.Prompts(), p => p.Contains(b.Id));

        hold.Release();
        await app.Until(() => app.Prompts().Any(p => p.Contains(b.Id) && p.Contains("Card file:")), "the second agent to start with its card's prompt");
        hold.Dispose();
        Assert.Equal("", app.Crashes());
    }
}
