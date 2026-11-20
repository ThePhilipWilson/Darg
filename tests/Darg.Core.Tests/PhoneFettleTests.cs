using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>AND-0007: Fettle from the phone. What the phone sends (its context line, the dictation note) and the chat it reads back.</summary>
public class PhoneFettleTests
{
    [Fact]
    public void The_context_line_says_what_the_phone_shows_and_that_it_is_the_phone()
    {
        Assert.Equal("Darg, on his phone", PhoneFettle.Context("Darg"));
        Assert.Equal("Darg › Board, card AND-0007 open, on his phone", PhoneFettle.Context("Darg", "Board", card: "AND-0007"));
        Assert.Equal("Darg › Docs, doc ANDROID open, on his phone", PhoneFettle.Context("Darg", "Docs", doc: "ANDROID"));
    }

    [Fact]
    public void A_dictated_message_carries_the_note_on_slips_and_a_typed_one_does_not()
    {
        var spoken = PhoneFettle.Message("Darg, on his phone", "make a card for the export button", voice: true);
        Assert.StartsWith("[Philip is looking at: Darg, on his phone]", spoken);
        Assert.Contains(PhoneFettle.Dictated, spoken);
        Assert.EndsWith("make a card for the export button", spoken);
        Assert.Contains("AskUserQuestion", PhoneFettle.Dictated);

        var typed = PhoneFettle.Message("Darg, on his phone", "hello", voice: false);
        Assert.DoesNotContain(PhoneFettle.Dictated, typed);
        Assert.EndsWith("hello", typed);
    }

    [Fact]
    public void A_chat_round_trips_and_says_where_to_read_from_next()
    {
        var question = new FettleEntry(3, "question", "Which board?", null, true,
            [new FettleQuestion("Board", "Which board?", false, [new FettleOption("Main", "the inbox"), new FettleOption("Elsewhere", "")], null)]);
        var json = new JsonObject
        {
            ["id"] = "c1", ["name"] = "Export card", ["project"] = "darg", ["state"] = "Needs you", ["busy"] = false, ["needsYou"] = true,
            ["count"] = 4, ["from"] = 2,
            ["items"] = new JsonArray(new FettleEntry(2, "fettle", "On it.", null, false, []).ToJson(), question.ToJson()),
        }.ToJsonString();

        var chat = FettleChat.Parse(json);
        Assert.Equal("c1", chat.Id);
        Assert.True(chat.NeedsYou);
        Assert.Equal(2, chat.Items.Count);
        var q = chat.Waiting!;
        Assert.Equal("question", q.Kind);
        Assert.Equal(["Main", "Elsewhere"], q.Questions.Single().Options.Select(o => o.Label));
        Assert.Equal("the inbox", q.Questions.Single().Options[0].Description);
        Assert.Equal(3, chat.NextFrom); // the question may still change: read again from it

        var settled = chat with { Items = [chat.Items[0], q with { Pending = false }] };
        Assert.Null(settled.Waiting);
        Assert.Equal(4, settled.NextFrom); // nothing open: from the end
    }

    [Fact]
    public async Task The_remote_sends_each_command_with_its_arguments()
    {
        var sent = new List<(string Name, JsonObject Args)>();
        const string empty = """{"id":"c1","items":[]}""";
        var remote = new FettleRemote((name, args) => { sent.Add((name, args)); return Task.FromResult(empty); });

        await remote.Say("darg", "make a card", section: "Board", card: "AND-0007", voice: true);
        await remote.Read("c1", 5);
        await remote.Answer("c1", new Dictionary<string, string> { ["Which board?"] = "Main" });
        await remote.Allow("c1", false);

        Assert.Equal(["fettle-say", "fettle", "fettle-answer", "fettle-answer"], sent.Select(s => s.Name));
        var say = sent[0].Args;
        Assert.Equal("darg", (string?)say["project"]);
        Assert.Equal("Board", (string?)say["section"]);
        Assert.Equal("AND-0007", (string?)say["card"]);
        Assert.True((bool?)say["voice"]);
        Assert.Null(say["chat"]); // no chat: a new one
        Assert.Equal(5, (int?)sent[1].Args["from"]);
        Assert.Equal("Main", (string?)sent[2].Args["answers"]!["Which board?"]);
        Assert.False((bool?)sent[3].Args["allow"]);
    }
}
