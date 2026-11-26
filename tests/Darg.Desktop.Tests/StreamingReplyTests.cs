using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0234: a reply shows in the transcript as it is written, in the agents view and in Fettle, and the whole
/// message replaces the partial one when it lands. Fake claude, started with --include-partial-messages, streams
/// "Streaming reply, part one." and holds the rest until the test makes its file (FAKE_STREAM).</summary>
public class StreamingReplyTests
{
    const string Half = "Streaming reply, part one.", Whole = "Streaming reply, part one. Part two.";

    static List<TextItem> Replies(AgentViewModel agent) => agent.Items.OfType<TextItem>().Where(t => t.Text.StartsWith("Streaming reply")).ToList();

    static List<ItemRecord> SavedTexts(DargApp app, string projectId, string agentId)
    {
        AgentStore.Flush();
        var record = AgentStore.List(AgentStore.Folder(projectId, app.Home)).Single(r => r.Id == agentId);
        return record.Items.Where(i => i.Kind == "text").ToList();
    }

    [AvaloniaFact]
    public async Task An_agents_reply_shows_as_it_is_written_and_the_whole_message_replaces_it()
    {
        using var app = DargApp.Make();
        await app.Start();
        var hold = Path.Combine(app.Home, "go-on");
        await app.Api("show", new { project = "proj", section = "Agents" });
        app.Agents.NewPrompt = "FAKE_STREAM " + hold.Replace(Path.DirectorySeparatorChar, '/');
        app.Agents.StartAgentCommand.Execute(null);
        var agent = app.Agents.Selected!;

        await app.Until(() => Replies(agent).Count == 1, "the reply's first part to show while it is written");
        var partial = Replies(agent).Single();
        Assert.True(partial.Partial);
        Assert.Equal(Half, partial.Text);
        Assert.Contains(app.ClaudeCalls(), c => (string?)c["kind"] == "start" && c["args"]!.ToJsonString().Contains("--include-partial-messages"));
        await DargApp.Settle(200);
        app.Shot("half-written");

        agent.Save(); // saved mid-reply: only whole messages are kept
        Assert.DoesNotContain(SavedTexts(app, "proj", agent.Id), t => t.Text.StartsWith("Streaming reply"));

        File.WriteAllText(hold, "");
        await app.Until(() => agent.Items.OfType<NoteItem>().Any(n => n.Text.StartsWith("Turn done")), "the turn to end");

        var reply = Assert.Single(Replies(agent)); // replaced, not added beside
        Assert.Same(partial, reply);
        Assert.Equal(Whole, reply.Text);
        Assert.False(reply.Partial);
        // fake claude's other replies streamed too: each shows once, none left partial
        var texts = agent.Items.OfType<TextItem>().ToList();
        Assert.DoesNotContain(texts, t => t.Partial);
        Assert.Equal(texts.Count, texts.Select(t => t.Text).Distinct().Count());
        Assert.All(texts, t => Assert.Matches(@"^(Step \d+: |Streaming reply)", t.Text)); // whole replies, no fragments
        Assert.Contains(SavedTexts(app, "proj", agent.Id), t => t.Text == Whole);
        app.Shot("whole");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Fettles_reply_shows_as_it_is_written_and_the_phone_reads_it_again_until_it_is_whole()
    {
        using var app = DargApp.Make();
        await app.Start();
        var hold = Path.Combine(app.Home, "go-on");
        var phone = new FettleRemote((name, args) => app.Api(name, args));

        var said = await phone.Say("proj", "FAKE_STREAM " + hold.Replace(Path.DirectorySeparatorChar, '/'));
        var chat = app.Main.Mascot.Find(said.Id)!;

        await app.Until(() => Replies(chat).Count == 1, "Fettle's reply to show while it is written");
        Assert.Equal(Half, Replies(chat).Single().Text);
        var read = await phone.Read(said.Id);
        var writing = Assert.Single(read.Items, i => i.Kind == "fettle" && i.Text == Half);
        Assert.True(writing.Writing);
        Assert.Equal(writing.Index, read.NextFrom); // the phone asks again from the reply being written

        File.WriteAllText(hold, "");
        await app.Until(() => Replies(chat).Count == 1 && !Replies(chat)[0].Partial, "Fettle's whole reply");
        Assert.Equal(Whole, Assert.Single(Replies(chat)).Text);
        read = await phone.Read(said.Id, writing.Index);
        var whole = Assert.Single(read.Items, i => i.Index == writing.Index);
        Assert.Equal(Whole, whole.Text);
        Assert.False(whole.Writing);
        Assert.Equal("", app.Crashes());
    }
}
