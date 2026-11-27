using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0236: a profile with <c>Hooks: off</c> starts its agents without the person's Claude Code hooks (the
/// <c>--settings</c> file that sets disableAllHooks), and only those: an agent on another profile gets no such flag.</summary>
public class ProfileHooksTests
{
    static List<string> Args(JsonObject call) => call["args"]!.AsArray().Select(a => (string)a!).ToList();

    static bool HooksOff(List<string> args) =>
        args.Select((a, i) => (a, i)).Any(x => x.a == "--settings" && x.i + 1 < args.Count
            && string.Equals(Path.GetFullPath(args[x.i + 1]), Path.GetFullPath(ModelProfiles.NoHooksFile), StringComparison.OrdinalIgnoreCase));

    [AvaloniaFact]
    public async Task Only_a_profile_with_hooks_off_starts_its_agents_without_hooks()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Home, "profiles.md"), """
            # Model profiles
            Ask Haiku: no

            ## Fast
            Model: sonnet
            Hooks: off

            ## Plain
            Model: sonnet

            ## Rules
            - kind: fettle -> Fast
            - kind: card -> Plain
            """);
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();

        var fettle = app.Main.Mascot;
        fettle.Compose = "what's on the board?";
        fettle.SendCommand.Execute(null);
        await app.Until(() => app.ClaudeCalls().Count(c => (string?)c["kind"] == "start") == 1, "Fettle's process to start");
        var fettleArgs = Args(app.ClaudeCalls().First(c => (string?)c["kind"] == "start"));
        Assert.True(HooksOff(fettleArgs), "Fettle's profile has Hooks: off: " + string.Join(' ', fettleArgs));
        Assert.True(fettle.Agent!.NoHooks);
        Assert.Contains("no hooks", string.Join('\n', fettle.Agent.Items.OfType<Darg.Desktop.ViewModels.NoteItem>().Select(n => n.Text)));

        await app.Api("work-on", new { project = "proj", card = card.Id });
        await app.Until(() => app.ClaudeCalls().Count(c => (string?)c["kind"] == "start") == 2, "the card agent's process to start");
        var cardArgs = Args(app.ClaudeCalls().Where(c => (string?)c["kind"] == "start").ElementAt(1));
        Assert.False(HooksOff(cardArgs), "the card agent's profile keeps hooks: " + string.Join(' ', cardArgs));
        Assert.DoesNotContain("--settings", cardArgs);
        Assert.Equal("", app.Crashes());
    }
}
