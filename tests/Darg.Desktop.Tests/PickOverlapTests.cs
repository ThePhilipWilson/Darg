using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0233: a new agent's claude starts while Haiku picks its model, on the person's own settings, and its
/// first message waits for the pick: the picked model and effort go first. Fake claude's FAKE_PICK_SECONDS holds the
/// pick's answer back and logs "answer" when it gives it, and it logs every control request but initialize.</summary>
public class PickOverlapTests
{
    const string PickSeconds = "2";

    [AvaloniaFact]
    public async Task Claude_starts_during_the_pick_and_the_picked_model_goes_before_the_first_message()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do", "The logo's edges are jagged.");
        await app.Start();
        Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", PickSeconds);
        try
        {
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = Assert.Single(app.Agents.Agents);
            await app.Until(() => app.Prompts().Count > 0, "the agent to be sent its prompt");

            var calls = app.ClaudeCalls();
            var start = Index(calls, c => Kind(c) == "start");
            Assert.True(start >= 0, "claude was started");
            var pid = (int)calls[start]["pid"]!;
            var answer = Index(calls, c => Kind(c) == "answer");
            var setModel = Index(calls, c => Kind(c) == "control" && (int)c["pid"]! == pid && (string?)c["subtype"] == "set_model");
            var setEffort = Index(calls, c => Kind(c) == "control" && (int)c["pid"]! == pid && (string?)c["subtype"] == "apply_flag_settings");
            var message = Index(calls, c => Kind(c) == "message" && (int)c["pid"]! == pid);
            var order = string.Join(", ", calls.Select(Kind));
            Assert.True(answer >= 0, $"the pick answered: {order}");
            Assert.True(start < answer, $"claude started before the pick ended: {order}");
            Assert.True(answer < setModel && answer < setEffort, $"the model and effort went after the pick: {order}");
            Assert.True(setModel < message && setEffort < message, $"the message went after the model and effort: {order}");
            // the starter profiles' Standard, which fake Haiku picks: sent, not given on its command line
            Assert.Equal("sonnet", (string?)calls[setModel]["request"]!["model"]);
            Assert.Equal("medium", (string?)calls[setEffort]["request"]!["settings"]!["effortLevel"]);
            Assert.DoesNotContain("--model", Args(calls[start]));
            Assert.Contains(card.Id, (string)calls[message]["text"]!);
            Assert.Equal("Standard", agent.Profile);
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", null); }
    }

    [AvaloniaFact]
    public async Task A_model_picked_by_hand_during_the_pick_still_wins()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", PickSeconds);
        try
        {
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = Assert.Single(app.Agents.Agents);
            await app.Until(() => app.ClaudeCalls().Any(c => Kind(c) == "start"), "claude to start during the pick");
            Assert.DoesNotContain(app.ClaudeCalls(), c => Kind(c) == "answer");
            agent.ModelChoice = AgentChoices.Find(agent.ModelChoices, "opus");
            await app.Until(() => app.Prompts().Count > 0, "the agent to be sent its prompt");

            var calls = app.ClaudeCalls();
            var order = string.Join(", ", calls.Select(Kind));
            var models = calls.Where(c => Kind(c) == "control" && (string?)c["subtype"] == "set_model").Select(c => (string?)c["request"]!["model"]).ToList();
            Assert.Equal(["opus"], models);
            Assert.True(Index(calls, c => Kind(c) == "control" && (string?)c["subtype"] == "set_model") < Index(calls, c => Kind(c) == "message"), order);
            Assert.Equal("", agent.Profile); // the hand pick wins over the profile, as before
        }
        finally { Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", null); }
    }

    [AvaloniaFact]
    public async Task An_agent_closed_during_the_pick_ends_its_claude_and_sends_nothing()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", PickSeconds);
        try
        {
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = Assert.Single(app.Agents.Agents);
            await app.Until(() => app.ClaudeCalls().Any(c => Kind(c) == "start"), "claude to start during the pick");
            var pid = (int)app.ClaudeCalls().First(c => Kind(c) == "start")["pid"]!;
            Assert.True(agent.CloseSession());

            await app.Until(() => Gone(pid), "the claude started for it to end");
            await app.Until(() => app.ClaudeCalls().Any(c => Kind(c) == "answer"), "the pick to answer");
            await Task.Delay(500);
            Assert.Empty(app.Prompts());
            Assert.Single(app.ClaudeCalls(), c => Kind(c) == "start"); // and none started once the pick came in
            Assert.False(agent.Open);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", null); }
    }

    [AvaloniaFact]
    public async Task A_rule_pick_starts_claude_on_the_profile_as_before()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Home, "profiles.md"), """
            # Model profiles

            ## Standard
            Model: sonnet

            ## Deep
            Model: opus
            Effort: high

            ## Rules
            - kind: card -> Deep
            """);
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        await app.Until(() => app.Prompts().Count > 0, "the agent to be sent its prompt");

        var calls = app.ClaudeCalls();
        var start = Assert.Single(calls, c => Kind(c) == "start");
        var args = Args(start);
        Assert.Equal("opus", args[args.IndexOf("--model") + 1]); // on its command line, so a spare with the same still fits (WOR-0235)
        Assert.DoesNotContain(calls, c => Kind(c) == "control" && (string?)c["subtype"] == "set_model");
        Assert.DoesNotContain(calls, c => Kind(c) == "ask"); // no Haiku
        Assert.Equal("Deep", Assert.Single(app.Agents.Agents).Profile);
    }

    [AvaloniaFact]
    public async Task A_haiku_pick_with_hooks_off_starts_claude_again_without_them()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Home, "profiles.md"), """
            # Model profiles

            ## Standard
            Model: sonnet
            Hooks: off
            Use for: ordinary work

            ## Deep
            Model: opus
            Use for: hard problems
            """);
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", PickSeconds);
        try
        {
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = Assert.Single(app.Agents.Agents);
            await app.Until(() => app.Prompts().Count > 0, "the agent to be sent its prompt");

            var calls = app.ClaudeCalls();
            var order = string.Join(", ", calls.Select(Kind));
            var starts = calls.Where(c => Kind(c) == "start").ToList();
            Assert.Equal(2, starts.Count);
            Assert.DoesNotContain("--settings", Args(starts[0])); // with hooks while Haiku picks: Deep keeps them
            Assert.True(Index(calls, c => Kind(c) == "start") < Index(calls, c => Kind(c) == "answer"), $"the first started during the pick: {order}");
            var second = Args(starts[1]);
            Assert.Equal(Path.GetFullPath(ModelProfiles.NoHooksFile), Path.GetFullPath(second[second.IndexOf("--settings") + 1]));
            Assert.Equal("sonnet", second[second.IndexOf("--model") + 1]);
            var message = Assert.Single(calls, c => Kind(c) == "message");
            Assert.Equal((int)starts[1]["pid"]!, (int)message["pid"]!);
            await app.Until(() => Gone((int)starts[0]["pid"]!), "the first claude to end");
            Assert.True(agent.NoHooks);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", null); }
    }

    [AvaloniaFact]
    public async Task A_haiku_pick_among_profiles_all_without_hooks_starts_without_them_during_the_pick()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Home, "profiles.md"), """
            # Model profiles

            ## Standard
            Model: sonnet
            Hooks: off
            Use for: ordinary work

            ## Deep
            Model: opus
            Hooks: off
            Use for: hard problems
            """);
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", PickSeconds);
        try
        {
            await app.Api("work-on", new { project = "proj", card = card.Id });
            await app.Until(() => app.Prompts().Count > 0, "the agent to be sent its prompt");
            var calls = app.ClaudeCalls();
            var start = Assert.Single(calls, c => Kind(c) == "start");
            Assert.Contains("--settings", Args(start));
            Assert.True(Index(calls, c => Kind(c) == "start") < Index(calls, c => Kind(c) == "answer"), string.Join(", ", calls.Select(Kind)));
            Assert.Equal("sonnet", (string?)calls.Single(c => Kind(c) == "control" && (string?)c["subtype"] == "set_model")["request"]!["model"]);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", null); }
    }

    [AvaloniaFact] // with WOR-0245: a pending agent's model is picked while it waits, but no claude starts for it until it may
    public async Task An_agent_waiting_past_the_cap_starts_no_claude_during_its_pick()
    {
        using var app = DargApp.Make();
        var a = app.Card("first card", "To Do");
        var b = app.Card("second card", "To Do");
        await app.Start();
        await app.Api("max-agents", new { project = "proj", max = 1 });
        using var hold = app.HoldTurns();
        Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", PickSeconds);
        try
        {
            await app.Api("work-on", new { project = "proj", card = a.Id });
            await app.Until(() => app.Prompts().Count > 0, "the first agent to be sent its prompt");
            await app.Api("work-on", new { project = "proj", card = b.Id });
            var waiting = app.Agents.Agents.First(x => x.CardId == b.Id);
            Assert.True(waiting.IsPending);
            await app.Until(() => app.ClaudeCalls().Count(c => Kind(c) == "answer") == 2, "the waiting agent's pick to answer");
            await Task.Delay(500);
            Assert.Single(app.ClaudeCalls(), c => Kind(c) == "start");
            Assert.True(waiting.IsPending);
            Assert.Equal("Standard", waiting.Profile);

            hold.Release();
            await app.Until(() => app.Prompts().Any(p => p.Contains(b.Id)), "the waiting agent to start once the first stopped");
            var starts = app.ClaudeCalls().Where(c => Kind(c) == "start").ToList();
            Assert.Equal(2, starts.Count);
            var second = Args(starts[1]);
            Assert.Equal("sonnet", second[second.IndexOf("--model") + 1]); // its pick was in: started on it, as before
        }
        finally { Environment.SetEnvironmentVariable("FAKE_PICK_SECONDS", null); }
    }

    static string? Kind(JsonObject c) => (string?)c["kind"];
    static int Index(IReadOnlyList<JsonObject> calls, Func<JsonObject, bool> match)
    {
        for (var i = 0; i < calls.Count; i++) if (match(calls[i])) return i;
        return -1;
    }
    static List<string> Args(JsonObject c) => c["args"]!.AsArray().Select(a => (string)a!).ToList();

    static bool Gone(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.HasExited; }
        catch (ArgumentException) { return true; }
    }
}
