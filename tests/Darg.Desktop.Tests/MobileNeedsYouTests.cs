using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;
using Darg.Mobile;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Desktop.Tests;

/// <summary>AND-0010: the phone's first screen, Needs you, run headless against the real desktop app's API. A card
/// waiting on a decision, an agent's question and an agent's permission ask all show there, and a tap answers each,
/// the PC seeing the answer (the card moved and answered, the fake agent told what was picked).</summary>
public class MobileNeedsYouTests
{
    const string Decision =
        "Dictate into the box.\n\n## Decision\n\n**Question:** Does dictation work for you?\n**Asked by:** agent x, 2026-11-23 00:00\n\n" +
        "- **Still fails** → back: say what the box showed.\n- **Works** (recommended) → done: words land in the box.\n\n" +
        "**Context:** a real microphone can't be tried from an agent.\n";

    [AvaloniaFact]
    public async Task A_decision_a_question_and_a_permission_show_first_and_a_tap_answers_each()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Decision);
        await app.Start();
        var asker = await StartAgent(app, "FAKE_ASK where should this go?");
        var permitter = await StartAgent(app, "FAKE_PERMIT run it");
        await app.Until(() => asker.Items.OfType<QuestionItem>().Any(q => q.Pending) && permitter.Items.OfType<PermissionItem>().Any(p => p.Pending),
            "both agents to ask");

        // the phone, with a saved connection, opens to Needs you
        var shell = new ShellViewModel(Saved(app));
        var window = Phone(shell);
        var needs = Assert.IsType<NeedsYouViewModel>(shell.Page);
        Assert.False(shell.CanGoBack);
        Assert.Equal("Needs you", shell.Title);
        await DargApp.Wait(() => needs.Items.Count == 3, "the three things waiting to show");
        Assert.Equal([NeedKind.Question, NeedKind.Permission, NeedKind.Decision], needs.Items.Select(x => x.Item.Kind).OrderBy(k => k));
        Assert.Equal(NeedKind.Decision, needs.Items[^1].Item.Kind); // asks first: an agent is held up mid-turn
        var decision = needs.Items.Single(x => x.IsDecision);
        Assert.Equal("Does dictation work for you?", decision.Heading);
        Assert.Equal(["Works", "Still fails"], decision.Questions.Single().Options.Select(o => o.Label));
        Assert.Equal(card.Id, decision.Item.Card);
        Assert.Equal("Proj", decision.Item.ProjectName);
        await DargApp.Settle(200);
        Shot(window, app, "1-needs-you");

        // the question: one tap on Main sends it, the agent hears Main
        var question = needs.Items.Single(x => x.IsQuestion);
        Assert.Equal(asker.Id, question.Item.Agent);
        Click(window, Button(window, "Main"));
        await app.Until(() => Logged(app, "answered") is { } r && r.ToJsonString().Contains("\"Main\""), "the agent to be told Main");
        Assert.DoesNotContain(question, needs.Items);
        Assert.False(asker.Items.OfType<QuestionItem>().Single().Pending);

        // the permission: Allow lets the tool run
        var permission = needs.Items.Single(x => x.IsPermission);
        Assert.Contains("echo fake", permission.Item.Detail);
        permission.AllowCommand.Execute(null);
        await app.Until(() => (string?)Logged(app, "permitted")?["behavior"] == "allow", "the agent to be allowed Bash");
        Assert.DoesNotContain(permission, needs.Items);

        // an ask answered is refused if tapped again (a stale screen can't answer a newer ask by mistake)
        var again = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.Api("answer", new { id = permitter.Id, at = permission.Item.At, allow = true }));
        Assert.Contains("already answered", again.Message);

        // the decision: a tap on Works writes the answer into the card and moves it to Done, as the panel's button does
        Click(window, Button(window, "Works"));
        await app.Until(() => app.Read(card.Id).Status == "Done", "the card to move to Done");
        var text = File.ReadAllText(Path.Combine(app.BoardFolder, "items", app.Read(card.Id).File));
        Assert.Contains("**Answer:** Works", text);
        Assert.Null(CardDecision.Read(text));

        await needs.Refresh();
        Assert.True(needs.Empty);
        await DargApp.Settle(200);
        Shot(window, app, "2-all-answered");

        // the projects are a step on, and back returns here
        needs.OpenProjectsCommand.Execute(null);
        var projects = Assert.IsType<ProjectsViewModel>(shell.Page);
        await DargApp.Wait(() => projects.Projects.Count == 1, "the projects to list");
        Assert.True(shell.Back());
        Assert.IsType<NeedsYouViewModel>(shell.Page);
        window.Close();
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_card_with_no_decision_offers_approve_and_send_back_which_needs_notes()
    {
        using var app = DargApp.Make();
        var card = app.Card("no decision", BoardStore.AttentionName, "Check it.\n");
        await app.Start();
        var client = new DargClient(Saved(app).Load()!);
        var items = await client.NeedsYou();
        var item = Assert.Single(items);
        Assert.True(item.NoDecision);
        Assert.Equal(["Approve", "Send back"], item.Questions.Single().Options.Select(o => o.Label));

        // tapped with no notes, Send back says why it can't, and the options can be tapped again
        var needs = new NeedsYouViewModel(new ShellViewModel(Saved(app)));
        await needs.Refresh();
        var row = Assert.Single(needs.Items);
        row.Questions.Single().Options.Single(o => o.Label == "Send back").TapCommand.Execute(null);
        await DargApp.Wait(() => row.Error.Length > 0, "the refusal");
        Assert.Contains("Say what's wrong", row.Error);
        Assert.Null(row.Questions.Single().Chosen);
        Assert.Equal(BoardStore.AttentionName, app.Read(card.Id).Status);

        // Approve moves it to Done
        row.Questions.Single().Options.Single(o => o.Label == "Approve").TapCommand.Execute(null);
        await app.Until(() => app.Read(card.Id).Status == "Done", "the card to be approved");
        Assert.Empty(needs.Items);
        Assert.Equal("", app.Crashes());
    }

    static async Task<AgentViewModel> StartAgent(DargApp app, string prompt)
    {
        var reply = await app.Api("start-agent", new { project = "proj", prompt });
        await app.Until(() => app.Agents.Agents.Any(a => reply.Contains(a.Id)), "the agent to be listed");
        return app.Agents.Agents.Single(a => reply.Contains(a.Id));
    }

    /// <summary>The phone's connection, saved as tools/android.ps1 writes it: the desktop's API address and token.</summary>
    static ConnectionStore Saved(DargApp app)
    {
        var parsed = Connection.ParseAddress(ArtifactHost.Server!.ApiUrl)!.Value;
        var store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));
        store.Save(new Connection(parsed.Address, parsed.Token!));
        return store;
    }

    static JsonObject? Logged(DargApp app, string kind) =>
        app.ClaudeCalls().LastOrDefault(c => (string?)c["kind"] == kind)?["response"] as JsonObject;

    static Button Button(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == name && b.IsEffectivelyVisible);
    }

    static void Click(Window window, Button b)
    {
        var at = b.TranslatePoint(new Avalonia.Point(b.Bounds.Width / 2, b.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
    }

    static Window Phone(ShellViewModel shell)
    {
        var window = new Window { Width = 412, Height = 1400, Content = new ShellView { DataContext = shell } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static void Shot(Window window, DargApp app, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
#pragma warning disable CS0618
        frame.Save(Path.Combine(app.Folder, "phone-needs-" + name + ".png"));
#pragma warning restore CS0618
    }
}

/// <summary>The phone's first screen after connecting is Needs you (AND-0010); tests of other screens step on from it.</summary>
static class PhoneSteps
{
    /// <summary>Checks the page is Needs you and opens the projects from it (listed at once after a connect, read fresh otherwise).</summary>
    public static ProjectsViewModel OpenProjects(this ShellViewModel shell)
    {
        var needs = Assert.IsType<NeedsYouViewModel>(shell.Page);
        needs.OpenProjectsCommand.Execute(null);
        return Assert.IsType<ProjectsViewModel>(shell.Page);
    }
}
