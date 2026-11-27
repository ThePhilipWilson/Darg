using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0246: a queue can name a model profile or a flow, which every card it starts is then worked with, in place
/// of the default agent; set when making the queue or from its lane, and shown on it.</summary>
public class QueueWithTests
{
    static List<string> Args(JsonObject call) => call["args"]!.AsArray().Select(a => (string)a!).ToList();

    static bool HooksOff(List<string> args) =>
        args.Select((a, i) => (a, i)).Any(x => x.a == "--settings" && x.i + 1 < args.Count
            && string.Equals(Path.GetFullPath(args[x.i + 1]), Path.GetFullPath(ModelProfiles.NoHooksFile), StringComparison.OrdinalIgnoreCase));

    static void Profiles(DargApp app) => File.WriteAllText(Path.Combine(app.Home, "profiles.md"), """
        # Model profiles
        Ask Haiku: no

        ## Quiet
        Model: sonnet
        Hooks: off

        ## Plain
        Model: sonnet

        ## Rules
        - kind: card -> Plain
        """);

    [AvaloniaFact]
    public async Task A_queue_with_a_profile_works_its_cards_on_it_whatever_the_rules_say()
    {
        using var app = DargApp.Make();
        Profiles(app);
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        // an unknown profile is refused, naming the real ones
        var bad = await Assert.ThrowsAnyAsync<Exception>(() => app.Api("queue-new", new { project = "proj", name = "Q", profile = "Nope" }));
        Assert.Contains("Quiet", bad.Message);
        var made = JsonNode.Parse(await app.Api("queue-new", new { project = "proj", name = "Quiet work", profile = "quiet" }))!;
        Assert.Equal("Quiet", (string)made["profile"]!); // spelt as the file has it
        var lane = app.Project.Queues!.Queues.Single();
        Assert.Equal("Profile Quiet", lane.With);
        Assert.Contains("Profile Quiet", lane.WithChoices);

        await app.Api("queue-run", new { project = "proj", queue = "Quiet work" });
        await app.Until(() => app.Agents.ForCard(card.Id) is not null, "the queue to start the card");
        var agent = app.Agents.ForCard(card.Id)!;
        await app.Until(() => agent.Profile == "Quiet", "the agent to be on the queue's profile, not the card rule's Plain");
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "start"), "its process to start");
        Assert.True(HooksOff(Args(app.ClaudeCalls().First(c => (string?)c["kind"] == "start"))), "Quiet has Hooks: off, Plain doesn't");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_queue_with_a_flow_runs_the_flow_on_each_card_and_can_be_set_afterwards()
    {
        using var app = DargApp.Make();
        Directory.CreateDirectory(Path.Combine(app.Home, "flows"));
        File.WriteAllText(Path.Combine(app.Home, "flows", "solo.md"), "# Solo\n\nMax rounds: 1\n\n## Builder\nBuild what the task says.\n");
        var card = app.Card("a thing to build", "To Do");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await app.Api("queue-new", new { project = "proj", name = "Flowing" });
        var q = app.Project.Queues!.All.Single();
        Assert.Null(q.Flow);
        Assert.Equal(QueueWith.Default, app.Project.Queues.Queues.Single().With);

        // set from the lane's box, kept in queues.json, and not both at once
        app.Project.Queues.Queues.Single().With = "Flow Solo";
        Assert.Equal("Solo", q.Flow);
        Assert.Equal("Solo", (string?)JsonNode.Parse(await app.Api("queues", new { project = "proj" }))!.AsArray().Single()!["flow"]);
        Assert.Contains("not both", (await Assert.ThrowsAnyAsync<Exception>(() => app.Api("queue-with", new { project = "proj", queue = "Flowing", profile = "Plain", flow = "Solo" }))).Message);

        await app.Api("queue-run", new { project = "proj", queue = "Flowing" });
        await app.Until(() => app.Agents.ForCard(card.Id) is not null, "the queue to start the flow's first stage on the card");
        var stage = app.Agents.ForCard(card.Id)!;
        Assert.NotNull(stage.Flow);
        Assert.Contains("Builder", stage.Name);
        Assert.Equal(Darg.Desktop.ViewModels.Section.Board, app.Main.Selected.Section); // a queue never takes the screen to Agents
        Assert.Equal("", app.Crashes());
    }
}
