using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Darg.Desktop.ViewModels;
using Darg.Core;
using Darg.Mobile;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Desktop.Tests;

/// <summary>AND-0007's phone half: the phone app's Fettle chat, run headless against the real desktop over HTTP as the
/// phone reaches it, fake claude playing Fettle. The card's done-when: a card made by dictating to Fettle.</summary>
public class MobileFettleTests
{
    sealed class FakeVoice(string said) : IVoiceInput
    {
        public bool Available => true;
        public int Heard { get; private set; }
        public Task<string?> Listen() { Heard++; return Task.FromResult<string?>(said); }
    }

    /// <summary>The phone connected to the desktop, on the project's page.</summary>
    static async Task<(ShellViewModel shell, ProjectPageViewModel page, Window window)> OnProject(DargApp app)
    {
        var shell = new ShellViewModel(new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json")));
        var window = new Window { Width = 412, Height = 860, Content = new ShellView { DataContext = shell } };
        window.Show();
        var connect = (ConnectViewModel)shell.Page!;
        connect.Address = ArtifactHost.Server!.ApiUrl;
        var connecting = connect.Connect();
        await DargApp.Wait(() => connecting.IsCompleted, "the phone to connect");
        await connecting;
        var projects = shell.OpenProjects();
        projects.OpenCommand.Execute(projects.Projects.Single());
        var page = Assert.IsType<ProjectPageViewModel>(shell.Page);
        // a project opens to Home (AND-0008); these tests talk to Fettle from the Board
        page.PickCommand.Execute(page.Tabs.Single(t => t.Section.Name == "Board"));
        return (shell, page, window);
    }

    [AvaloniaFact]
    public async Task A_card_is_made_by_dictating_to_Fettle_on_the_phone()
    {
        using var app = DargApp.Make();
        await app.Start();
        var voice = new FakeVoice("FAKE_CARD proj: Export the board as CSV");
        Voice.Input = voice;
        try
        {
            var (shell, page, window) = await OnProject(app);
            Assert.True(page.CanTalkToFettle);
            Shot(window, app, "1-project");

            page.FettleCommand.Execute(null);
            var chat = Assert.IsType<FettleChatViewModel>(shell.Page);
            Assert.True(chat.CanDictate);
            Assert.Equal("Fettle sees: Proj › Board", chat.Sees);
            Shot(window, app, "2-empty");

            await chat.DictateCommand.ExecuteAsync(null);
            Assert.Equal(1, voice.Heard);
            Assert.Equal("FAKE_CARD proj: Export the board as CSV", chat.Compose); // in the box for a glance first
            await chat.SendCommand.ExecuteAsync(null);
            Assert.Equal("", chat.Compose);
            Assert.False(chat.Bad, chat.Status);
            Assert.True(chat.Rows[0].IsUser);

            await app.Until(() => app.Prompts().Any(p => p.Contains("Export the board as CSV")), "the message to reach Fettle");
            var sent = app.Prompts().Single(p => p.Contains("Export the board as CSV"));
            Assert.StartsWith("[Philip is looking at: Proj › Board, on his phone]", sent);
            Assert.Contains(PhoneFettle.Dictated, sent); // dictated, so Fettle reads past slips

            await app.Until(() => BoardStore.ReadCards(app.BoardFolder).Any(c => c.Title == "Export the board as CSV"), "Fettle to make the card");
            var card = BoardStore.ReadCards(app.BoardFolder).Single(c => c.Title == "Export the board as CSV");
            // the reply arrives by polling, its card link shown as the card's id
            await DargApp.Wait(() => chat.Rows.Any(r => r.IsFettle && r.Text == $"Made {card.Id}."), "the reply naming the card on the phone");
            await DargApp.Wait(() => !chat.Busy, "the turn to end on the phone");
            Shot(window, app, "3-made");

            // back and in again: the same chat, carrying on
            Assert.True(shell.Back());
            page.FettleCommand.Execute(null);
            Assert.Same(chat, shell.Page);
            window.Close();
            Assert.Equal("", app.Crashes());
        }
        finally { Voice.Input = null; }
    }

    [AvaloniaFact]
    public async Task Fettles_question_shows_as_options_on_the_phone_and_a_tap_answers_it()
    {
        using var app = DargApp.Make();
        await app.Start();
        var (shell, page, window) = await OnProject(app);
        page.OpenCard = "PRJ-0001"; // as the board section sets it with a card open
        page.FettleCommand.Execute(null);
        var chat = (FettleChatViewModel)shell.Page!;
        Assert.False(chat.CanDictate); // no recogniser here: no mic
        Assert.Equal("Fettle sees: Proj › Board, card PRJ-0001", chat.Sees);

        chat.Compose = "FAKE_ASK where should this go?";
        await chat.SendCommand.ExecuteAsync(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("where should this go")), "the message to reach Fettle");
        var sent = app.Prompts().Single(p => p.Contains("where should this go"));
        Assert.StartsWith("[Philip is looking at: Proj › Board, card PRJ-0001 open, on his phone]", sent);
        Assert.DoesNotContain(PhoneFettle.Dictated, sent); // typed

        await DargApp.Wait(() => chat.Rows.Any(r => r.IsQuestion && r.Pending), "Fettle's question on the phone");
        var asked = chat.Rows.Single(r => r.IsQuestion);
        var q = asked.Questions.Single();
        Assert.Equal("Which board?", q.Question);
        Assert.Equal(["Main", "Elsewhere"], q.Options.Select(o => o.Label));
        Assert.False(asked.NeedsSendButton); // one question, one pick: a tap sends it
        Shot(window, app, "4-question");

        q.Options.Single(o => o.Label == "Elsewhere").TapCommand.Execute(null);

        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "answered"), "the answer to reach Fettle");
        var answered = app.ClaudeCalls().First(c => (string?)c["kind"] == "answered");
        Assert.Equal("Elsewhere", (string?)answered["response"]!["updatedInput"]!["answers"]!["Which board?"]);
        await DargApp.Wait(() => chat.Rows.Any(r => r.IsFettle && r.Text == "Picked: Elsewhere"), "Fettle to carry on with it on the phone");
        var done = chat.Rows.Single(r => r.IsQuestion);
        Assert.False(done.Pending);
        Assert.True(done.Questions.Single().Answered);
        Assert.Equal("Elsewhere", done.Questions.Single().Chosen);
        Shot(window, app, "5-answered");
        window.Close();
        Assert.Equal("", app.Crashes());
    }

    [Theory]
    [InlineData("Made [WOR-0001](card:WOR-0001).", "Made WOR-0001.")]
    [InlineData("**Done**: see `tools/x.ps1`", "Done: see tools/x.ps1")]
    [InlineData("## Plan\nStep one", "Plan\nStep one")]
    public void Fettles_markdown_shows_as_plain_text(string md, string plain) => Assert.Equal(plain, FettleRow.Plain(md));

    static void Shot(Window window, DargApp app, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
#pragma warning disable CS0618
        frame.Save(Path.Combine(app.Folder, "phone-fettle-" + name + ".png"));
#pragma warning restore CS0618
    }
}
