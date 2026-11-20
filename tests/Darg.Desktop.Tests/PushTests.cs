using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Core.Tests;

namespace Darg.Desktop.Tests;

/// <summary>AND-0006: a decision raised on the desktop goes to the phone with its options as buttons, and a tap on one
/// answers the card as picking it on the panel does. ntfy is played by <see cref="FakeNtfy"/>; the tap by a message on
/// the reply topic, which is what the phone's button posts.</summary>
public class PushTests
{
    static void PushTo(DargApp app, FakeNtfy ntfy)
    {
        var config = InstanceConfig.Load(app.Home);
        config.Push = new PushConfig { Server = ntfy.Url, Topic = "darg-test" };
        config.Save(app.Home);
    }

    static Task<string> RaiseDecision(DargApp app, string card) =>
        app.Api("to-attention", new { project = "proj", card, question = "Ship it?", options = new[] { "Not yet → back: more work", "Yes (recommended) → done: it works" }, context = "tests pass" });

    [AvaloniaFact]
    public async Task A_decision_raised_on_the_desktop_is_answered_by_a_tap_on_the_phone()
    {
        using var ntfy = new FakeNtfy();
        using var app = DargApp.Make();
        PushTo(app, ntfy);
        var card = app.Card("ship the thing", "QA Ready", "Ship the thing.");
        await app.Start();
        await app.Until(() => ntfy.StreamCount > 0, "Darg listening to the reply topic");

        await RaiseDecision(app, card.Id);
        await app.Until(() => ntfy.Snapshot().Any(m => m["actions"] is not null), "the decision sent to ntfy with buttons");
        var sent = ntfy.Snapshot().Single(m => m["actions"] is not null);
        Assert.Equal("darg-test", (string?)sent["topic"]);
        Assert.Contains("Ship it?", (string?)sent["message"]);
        var buttons = sent["actions"]!.AsArray();
        Assert.Equal(["Yes ★", "Not yet"], buttons.Select(b => (string?)b!["label"]));
        Assert.Equal($"{ntfy.Url}/darg-test-reply", (string?)buttons[0]!["url"]);

        ntfy.Reply((string)buttons[0]!["body"]!); // tapping Yes on the lock screen
        await app.Until(() => app.Read(card.Id).Status == "Done", "the card answered Yes and moved to Done");
        Assert.Contains("**Answer:** Yes", File.ReadAllText(Path.Combine(app.BoardFolder, "items", card.File)));
        Assert.Contains("you picked Yes on the phone", app.Main.PushLast);
        // a second tap of the same button (or ntfy replaying it) does nothing more
        ntfy.Reply((string)buttons[0]!["body"]!);
        await DargApp.Settle(300);
        Assert.Equal("Done", app.Read(card.Id).Status);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Answering_on_the_desktop_takes_the_decision_off_the_phone_and_kills_its_buttons()
    {
        using var ntfy = new FakeNtfy();
        using var app = DargApp.Make();
        PushTo(app, ntfy);
        var card = app.Card("ship the thing", "QA Ready", "Ship the thing.");
        await app.Start();
        await app.Until(() => ntfy.StreamCount > 0, "Darg listening to the reply topic");

        await RaiseDecision(app, card.Id);
        await app.Until(() => ntfy.Snapshot().Any(m => m["actions"] is not null), "the decision sent to ntfy");
        var notYet = (string)ntfy.Snapshot().Single(m => m["actions"] is not null)["actions"]![1]!["body"]!;

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Yes" });
        await app.Until(() => ntfy.DeletedSnapshot().Contains($"darg-test/proj-{card.Id.ToLowerInvariant()}"), "the phone's notification deleted");
        ntfy.Reply(notYet); // a stale tap after it was answered here
        await DargApp.Settle(300);
        Assert.Equal("Done", app.Read(card.Id).Status);
    }

    [AvaloniaFact]
    public async Task Push_is_off_until_a_topic_is_set_and_the_push_command_turns_it_on()
    {
        using var ntfy = new FakeNtfy();
        using var app = DargApp.Make();
        await app.Start();
        Assert.Contains("off", await app.Api("push"));

        var status = await app.Api("push", new { topic = "new", server = ntfy.Url });
        Assert.Contains("subscribe to topic darg-", status);
        Assert.StartsWith("darg-", InstanceConfig.Load(app.Home).Push?.Topic);
        await app.Until(() => ntfy.StreamCount > 0, "Darg listening once turned on");
        Assert.Contains("Sent a test", await app.Api("push-test"));
        Assert.Contains(ntfy.Snapshot(), m => (string?)m["title"] == "Darg: test");
        app.Main.OpenSettingsCommand.Execute(null);
        await DargApp.Settle(300);
        Assert.StartsWith("darg-", app.Main.SettingsPage!.PushTopic);
        Assert.Contains("subscribe to topic", app.Main.SettingsPage.PushState);
        app.Shot("settings-push");
        app.Main.SettingsPage.CloseCommand.Execute(null);

        Assert.Contains("off", await app.Api("push", new { off = "true" }));
        Assert.Null(InstanceConfig.Load(app.Home).Push);
    }
}
