using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>AND-0010: the needs-you list as the desktop writes it and the phone reads it.</summary>
public class NeedsYouTests
{
    static Card Card(string id, string title) => new(id, title, "For Human Attention", "", "Task", [], id + ".md", "");

    [Fact]
    public void An_ask_and_a_decision_go_through_json_and_back_whole()
    {
        var ask = new NeedsYouItem(NeedKind.Question, "proj", "Proj", "Which board?", "", "a1", "Fix the thing", 7, "WOR-1", "The thing",
            Questions: [new NeedQuestion("Board", "Which board?", true, [new NeedOption("Main", "the inbox"), new NeedOption("Elsewhere", "another")])]);
        var decision = NeedsYouItem.ForDecision("proj", "Proj", Card("WOR-2", "Voice"), new CardDecision("Does it work?",
            [new DecisionOption("Fails", "say how", DecisionAction.Back, false), new DecisionOption("Works", "words land", DecisionAction.Done, true)],
            "a mic can't be tried", "agent abc, 2026-11-23"), false);

        var read = NeedsYouItem.List(JsonNode.Parse(new JsonArray(ask.ToJson(), decision.ToJson()).ToJsonString()));

        Assert.Equal(2, read.Count);
        var a = read[0];
        Assert.Equal((NeedKind.Question, "a1", 7, "WOR-1", "The thing", "Proj"), (a.Kind, a.Agent, a.At, a.Card, a.CardTitle, a.ProjectName));
        Assert.Equal("question:a1:7", a.Key);
        var q = Assert.Single(a.Questions);
        Assert.True(q.MultiSelect);
        Assert.Equal(["Main", "Elsewhere"], q.Options.Select(o => o.Label));
        Assert.Equal("the inbox", q.Options[0].Description);

        var d = read[1];
        Assert.Equal((NeedKind.Decision, "WOR-2", "Voice", "Does it work?", "a mic can't be tried"), (d.Kind, d.Card, d.CardTitle, d.Title, d.Detail));
        Assert.Equal("decision:proj:WOR-2", d.Key);
        Assert.Equal(-1, d.At);
        // recommended first, as the card panel shows them, each with what picking it does
        Assert.Equal(["Works", "Fails"], d.Questions.Single().Options.Select(o => o.Label));
        Assert.True(d.Questions.Single().Options[0].Recommended);
        Assert.Equal(["done", "back"], d.Questions.Single().Options.Select(o => o.Action));
    }

    [Fact]
    public void Entries_that_are_not_ones_are_left_out()
    {
        var json = JsonNode.Parse("""
            [ {"kind":"question","project":"p","title":"ok","agent":"a","at":1},
              {"kind":"weather","project":"p"},
              {"kind":"decision"},
              "a string",
              {"kind":"permission","project":"p","title":"Use Bash?","detail":"ls","agent":"b","at":"three"} ]
            """);
        var read = NeedsYouItem.List(json);
        Assert.Equal([NeedKind.Question, NeedKind.Permission], read.Select(x => x.Kind));
        Assert.Equal(-1, read[1].At); // not a number: no place, so answer takes the agent's newest ask
        Assert.Empty(NeedsYouItem.List(JsonNode.Parse("\"Darg says no\"")));
        Assert.Empty(NeedsYouItem.List(null));
    }

    [Fact]
    public void Asks_come_before_decisions_keeping_their_order()
    {
        var d1 = new NeedsYouItem(NeedKind.Decision, "a", "A", "d1", Card: "X-1");
        var q1 = new NeedsYouItem(NeedKind.Question, "b", "B", "q1", Agent: "x", At: 1);
        var d2 = new NeedsYouItem(NeedKind.Decision, "b", "B", "d2", Card: "X-2");
        var p1 = new NeedsYouItem(NeedKind.Permission, "a", "A", "p1", Agent: "y", At: 4);
        Assert.Equal(["q1", "p1", "d1", "d2"], NeedsYouItem.Order([d1, q1, d2, p1]).Select(x => x.Title));
    }

    [Fact]
    public void The_phone_may_call_needs_you_and_answer()
    {
        Assert.Contains("needs-you", RemoteAccess.Commands);
        Assert.Contains("answer", RemoteAccess.Commands);
        Assert.Contains("decide", RemoteAccess.Commands);
    }
}
