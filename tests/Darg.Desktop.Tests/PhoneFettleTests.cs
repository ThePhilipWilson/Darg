using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>AND-0007: Fettle from the phone, through the fettle-* API commands the phone app calls. Driven with the
/// phone's own client (<see cref="FettleRemote"/>) against the real app; fake claude plays Fettle.</summary>
public class PhoneFettleTests
{
    static FettleRemote Phone(DargApp app) => new((name, args) => app.Api(name, args));

    /// <summary>Read the chat as the phone polls it until it shows what's wanted (never .Result on the UI thread: the API runs there).</summary>
    static async Task<FettleChat> ReadUntil(DargApp app, FettleRemote phone, string chat, Func<FettleChat, bool> done, string what)
    {
        var until = DateTime.Now.AddSeconds(30);
        while (true)
        {
            var now = await phone.Read(chat);
            if (done(now)) return now;
            if (DateTime.Now > until)
                throw new TimeoutException($"Timed out waiting for {what}. Chat: {string.Join(" | ", now.Items.Select(i => i.Kind + ": " + i.Text))}{Environment.NewLine}{app.Diagnose()}");
            await DargApp.Settle(100);
        }
    }

    [AvaloniaFact] // the card's done-when: a card made by dictating to Fettle
    public async Task A_dictated_message_reaches_Fettle_with_the_phone_context_and_makes_a_card()
    {
        using var app = DargApp.Make();
        await app.Start();
        var phone = Phone(app);

        var chat = await phone.Say("proj", "FAKE_CARD proj: Export the board as CSV", section: "Board", voice: true);

        Assert.Equal("user", chat.Items[0].Kind); // the phone shows his words at once
        Assert.Equal("FAKE_CARD proj: Export the board as CSV", chat.Items[0].Text);
        Assert.Equal("proj", chat.Project);
        await app.Until(() => app.Prompts().Any(p => p.Contains("Export the board as CSV")), "the message to reach Fettle");
        var sent = app.Prompts().Single(p => p.Contains("Export the board as CSV"));
        Assert.StartsWith("[Philip is looking at: Proj › Board, on his phone]", sent);
        Assert.Contains(PhoneFettle.Dictated, sent);

        await app.Until(() => BoardStore.ReadCards(app.BoardFolder).Any(c => c.Title == "Export the board as CSV"), "Fettle to make the card");
        var card = BoardStore.ReadCards(app.BoardFolder).Single(c => c.Title == "Export the board as CSV");
        await ReadUntil(app, phone, chat.Id, c => c.Items.Any(i => i.Kind == "fettle" && i.Text.Contains($"(card:{card.Id})")), "the reply linking the card");
        Assert.Contains(app.Main.Mascot.Chats, c => c.Id == chat.Id); // listed on the desktop too, to carry on there
    }

    [AvaloniaFact]
    public async Task Fettles_question_reaches_the_phone_as_options_and_the_tapped_one_goes_back()
    {
        using var app = DargApp.Make();
        await app.Start();
        var phone = Phone(app);

        var chat = await phone.Say("proj", "FAKE_ASK where should this go?");
        var asked = await ReadUntil(app, phone, chat.Id, c => c.NeedsYou, "Fettle to ask");
        var waiting = asked.Waiting!;
        Assert.Equal("question", waiting.Kind);
        var q = waiting.Questions.Single();
        Assert.Equal("Which board?", q.Question);
        Assert.Equal(["Main", "Elsewhere"], q.Options.Select(o => o.Label));

        var after = await phone.Answer(chat.Id, new Dictionary<string, string> { [q.Question] = "Elsewhere" });

        Assert.Equal("Elsewhere", after.Items.First(i => i.Kind == "question").Questions.Single().Chosen);
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "answered"), "the answer to reach Fettle");
        var answered = app.ClaudeCalls().First(c => (string?)c["kind"] == "answered");
        Assert.Equal("Elsewhere", (string?)answered["response"]!["updatedInput"]!["answers"]!["Which board?"]);
        await ReadUntil(app, phone, chat.Id, c => c.Items.Any(i => i.Text == "Picked: Elsewhere"), "Fettle to carry on with it");
    }

    [AvaloniaFact]
    public async Task The_phone_chat_runs_on_while_the_desktop_starts_another_and_carries_on_by_id()
    {
        using var app = DargApp.Make();
        await app.Start();
        var phone = Phone(app);
        var chat = await phone.Say("proj", "first from the phone");
        await ReadUntil(app, phone, chat.Id, c => !c.Busy, "the first turn to end");

        // on the desktop, Philip opens the phone's chat from the list, then starts one of his own: leaving a chat on the
        // desktop ends its session, but not the phone's, which is still in it
        var fettle = app.Main.Mascot;
        fettle.SelectedChat = fettle.Chats.Single(c => c.Id == chat.Id);
        Assert.Equal(chat.Id, fettle.Agent?.Id);
        Assert.Contains("first from the phone", fettle.Items.OfType<Darg.Desktop.ViewModels.UserItem>().Select(u => u.Text));
        fettle.FreshCommand.Execute(null);
        fettle.Compose = "typed at the desk";
        fettle.SendCommand.Execute(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("typed at the desk")), "the desktop's chat to start");

        var again = await phone.Say("proj", "second from the phone", chat: chat.Id);

        Assert.Equal(chat.Id, again.Id);
        Assert.Equal("second from the phone", again.Items[0].Text);
        await app.Until(() => app.Prompts().Any(p => p.Contains("second from the phone")), "the second message to reach Fettle");
        var pids = app.ClaudeCalls().Where(c => (string?)c["kind"] == "message" && ((string?)c["text"] ?? "").Contains("from the phone"))
            .Select(c => (int?)c["pid"]).Distinct().ToList();
        Assert.Single(pids); // the same session heard both: it wasn't closed and resumed
    }

    [AvaloniaFact]
    public async Task Answering_when_Fettle_waits_on_nothing_is_refused()
    {
        using var app = DargApp.Make();
        await app.Start();
        var phone = Phone(app);
        var chat = await phone.Say("proj", "hello");
        await ReadUntil(app, phone, chat.Id, c => !c.Busy, "the turn to end");

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => phone.Answer(chat.Id, new Dictionary<string, string> { ["x"] = "y" }));
        Assert.Contains("isn't waiting", e.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => phone.Say("proj", "hi", section: "Nowhere"));
    }
}
