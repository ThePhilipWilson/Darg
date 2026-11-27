using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0235: a new Fettle chat's first reply waited on Claude Code starting up. Darg keeps one Fettle session
/// started and idle, as the next chat would start it, hands it to the next new chat and starts another; one whose
/// MCP ticks or briefing changed is replaced, never used. The sandbox has spares off; these tests turn them on.</summary>
public class FettleSpareTests
{
    static List<JsonObject> Starts(DargApp app) => app.ClaudeCalls().Where(c => (string?)c["kind"] == "start").ToList();
    static int Pid(JsonObject call) => (int)call["pid"]!;
    static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Spares on, at once, for the test; off again after it (the sandbox's way).</summary>
    static async Task WithSpares(Func<Task> test)
    {
        MascotViewModel.SpareAfter = TimeSpan.Zero;
        try { await test(); }
        finally { MascotViewModel.SpareAfter = null; }
    }

    [AvaloniaFact]
    public Task A_new_chat_sends_its_first_message_to_the_spare_and_another_spare_starts() => WithSpares(async () =>
    {
        using var app = DargApp.Make();
        await app.Start();
        var fettle = app.Main.Mascot;
        await app.Until(() => fettle.Spare is not null && Starts(app).Count == 1, "a spare Fettle session to start");
        var spare = Starts(app)[0];
        await DargApp.Settle();
        Assert.Empty(app.Prompts()); // nothing is sent to a spare: it costs no model call

        fettle.Compose = "what's on the board?";
        fettle.SendCommand.Execute(null);
        var chat = fettle.Agent!;
        await app.Until(() => app.Prompts().Any(p => p.Contains("what's on the board?")), "the message to reach claude");

        // one process start for the chat, not two: the message went to the spare's process
        var message = app.ClaudeCalls().First(c => (string?)c["kind"] == "message");
        Assert.Equal(Pid(spare), Pid(message));
        var calls = app.ClaudeCalls();
        Assert.Equal(1, calls.TakeWhile(c => c != calls.First(m => (string?)m["kind"] == "message")).Count(c => (string?)c["kind"] == "start"));

        await app.Until(() => chat.State == AgentState.Idle, "Fettle's turn to end");
        Assert.Contains("--append-system-prompt-file", spare["args"]!.AsArray().Select(a => (string)a!));
        // and another starts for the chat after, while the first runs on as the chat's
        await app.Until(() => Starts(app).Count == 2 && fettle.Spare is not null, "the next spare to start");
        Assert.NotEqual(Pid(spare), Pid(Starts(app)[1]));
        Assert.True(Alive(Pid(spare)), "the chat's session (the old spare) runs on");

        // agents.log: the chat's first turn has no process start of its own, so its start-up reads as none
        await AgentTimings.Writer.Flush();
        var lines = File.ReadAllLines(Path.Combine(app.Home, "agents.log")).Where(l => l.Contains($" {chat.Id} +")).ToList();
        Assert.Contains(lines, l => l.Contains(" · took spare session spare-"));
        Assert.DoesNotContain(lines, l => l.Contains(" · process start"));
        Assert.Contains(lines, l => l.Contains(" · turn end ("));
        Assert.Contains(lines, l => l.EndsWith(" · agent fettle", StringComparison.Ordinal));
        Assert.Equal("", app.Crashes());
    });

    [AvaloniaFact] // WOR-0236: a Fettle profile with Hooks: off has its spare started without hooks too, so a chat still takes it
    public Task A_spare_on_a_hooks_off_profile_starts_without_hooks_and_is_taken() => WithSpares(async () =>
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Home, "profiles.md"), """
            # Model profiles
            Ask Haiku: no

            ## Fast
            Model: sonnet
            Hooks: off

            ## Rules
            - kind: fettle -> Fast
            """);
        await app.Start();
        var fettle = app.Main.Mascot;
        await app.Until(() => fettle.Spare is not null && Starts(app).Count == 1, "a spare Fettle session to start");
        var spare = Starts(app)[0];
        var args = spare["args"]!.AsArray().Select(a => (string)a!).ToList();
        Assert.Equal(Path.GetFullPath(ModelProfiles.NoHooksFile), Path.GetFullPath(args[args.IndexOf("--settings") + 1]));

        fettle.Compose = "what's on the board?";
        fettle.SendCommand.Execute(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("what's on the board?")), "the message to reach claude");
        Assert.Equal(Pid(spare), Pid(app.ClaudeCalls().First(c => (string?)c["kind"] == "message"))); // taken, not replaced
        Assert.True(fettle.Agent!.NoHooks);
        Assert.Equal("", app.Crashes());
    });

    [AvaloniaFact]
    public Task A_spare_whose_MCP_ticks_changed_is_replaced_not_used() => WithSpares(async () =>
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Repo, ".mcp.json"), """{ "mcpServers": { "repo-tool": { "command": "node", "args": ["tool.js"] } } }""");
        await app.Start();
        var fettle = app.Main.Mascot;
        await app.Until(() => fettle.Spare is not null && Starts(app).Count == 1, "a spare Fettle session to start");
        var spare = Starts(app)[0];
        Assert.Contains("--mcp-config", spare["args"]!.AsArray().Select(a => (string)a!)); // repo-tool, ticked by default

        // repo-tool unticked on the Context page, which only writes instance.json: the spare was started with it
        var config = InstanceConfig.Load();
        config.Projects[0].Mcp = new McpChoice { Off = ["repo-tool"] };
        config.Save();

        fettle.Compose = "what's on the board?";
        fettle.SendCommand.Execute(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("what's on the board?")), "the message to reach claude");
        var message = app.ClaudeCalls().First(c => (string?)c["kind"] == "message");
        Assert.NotEqual(Pid(spare), Pid(message));
        var chatStart = Starts(app).Single(s => Pid(s) == Pid(message));
        Assert.DoesNotContain("--mcp-config", chatStart["args"]!.AsArray().Select(a => (string)a!)); // nothing ticked now
        await app.Until(() => !Alive(Pid(spare)), "the stale spare to end");
        // its replacement is started as a chat now would be
        await app.Until(() => fettle.Spare is not null && Starts(app).Count == 3, "a fresh spare");
        Assert.DoesNotContain("--mcp-config", Starts(app)[2]["args"]!.AsArray().Select(a => (string)a!));
    });

    [AvaloniaFact]
    public Task A_spare_is_replaced_when_the_briefing_changes_and_ends_with_the_app() => WithSpares(async () =>
    {
        using var app = DargApp.Make();
        await app.Start();
        var fettle = app.Main.Mascot;
        await app.Until(() => fettle.Spare is not null && Starts(app).Count == 1, "a spare Fettle session to start");
        var first = Pid(Starts(app)[0]);

        // a project added in Setup: the briefing lists it, so the spare started before is stale
        var config = InstanceConfig.Load();
        var extra = Directory.CreateDirectory(Path.Combine(app.Folder, "other")).FullName;
        fettle.Projects = [.. config.Projects, new ProjectConfig { Id = "other", Name = "Other Thing", Repos = [extra] }];
        await app.Until(() => !Alive(first) && fettle.Spare is not null && Starts(app).Count == 2, "the spare to be replaced");
        var second = Starts(app)[1];
        var args = second["args"]!.AsArray().Select(a => (string)a!).ToList();
        Assert.Contains("Other Thing", File.ReadAllText(args[args.IndexOf("--append-system-prompt-file") + 1]));

        fettle.Compose = "hello";
        fettle.SendCommand.Execute(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("hello")), "the message to reach claude");
        Assert.Equal(Pid(second), Pid(app.ClaudeCalls().First(c => (string?)c["kind"] == "message")));

        await app.Until(() => fettle.Spare is not null && Starts(app).Count == 3, "the next spare");
        var third = Pid(Starts(app)[2]);
        fettle.Close();
        await app.Until(() => !Alive(third), "the spare to end with the app");
        Assert.Null(fettle.Spare);
    });
}
