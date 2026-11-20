using System.Text.Json;
using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>AND-0005: the phone reads an agent's transcript through the API a few entries at a time.</summary>
public class RemoteAgentsTests
{
    /// <summary>The desktop's agent command over a list of entries, as MainViewModel answers it, logging each since asked for.</summary>
    sealed class FakeDesktop
    {
        public readonly List<JsonObject> Items = [];
        public readonly List<int> Asked = [];
        public readonly List<JsonObject> Said = [];
        public int Page = 200;
        public string Status = "working";

        public Task<JsonNode?> Call(string command, object args)
        {
            var a = JsonNode.Parse(JsonSerializer.Serialize(args))!.AsObject();
            if (command == "say") { Said.Add(a); return Task.FromResult<JsonNode?>(JsonValue.Create("Sent.")); }
            var since = (int)a["since"]!;
            Asked.Add(since);
            var from = Math.Min(since, Items.Count);
            var count = Math.Min(Page, Items.Count - from);
            var items = new JsonArray();
            for (var i = from; i < from + count; i++) items.Add(Items[i].DeepClone());
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["id"] = "a1", ["name"] = "fix the header", ["project"] = "proj", ["status"] = Status, ["state"] = "Working · 3 steps",
                ["activity"] = "Running tests", ["card"] = "WOR-1", ["total"] = Items.Count, ["next"] = from + count, ["items"] = items,
            });
        }

        public void Add(string kind, string text, bool done = true, bool pending = false) =>
            Items.Add(new JsonObject { ["i"] = Items.Count, ["kind"] = kind, ["text"] = text, ["tool"] = kind == "tool" ? "Bash" : null, ["done"] = done, ["pending"] = pending });
    }

    [Fact]
    public async Task A_refresh_fetches_only_new_entries_and_rereads_a_step_still_running()
    {
        var desk = new FakeDesktop();
        desk.Add("user", "fix the header");
        desk.Add("agent", "Looking.");
        desk.Add("tool", "dotnet test", done: false);
        var feed = new AgentFeed(desk.Call, "a1");

        Assert.Equal(0, await feed.Refresh());
        Assert.Equal(3, feed.Entries.Count);
        Assert.Equal("fix the header", feed.Name);
        Assert.True(feed.Working);
        Assert.Equal("Running tests", feed.Activity);
        Assert.Equal("… Bash: dotnet test", feed.Entries[2].StepLine);

        // the running step finishes and a reply follows: the feed asks from the step, not from the start
        desk.Items[2]["done"] = true;
        desk.Add("agent", "All green.");
        Assert.Equal(2, await feed.Refresh());
        Assert.Equal([0, 2], desk.Asked);
        Assert.Equal(4, feed.Entries.Count);
        Assert.Equal("✓ Bash: dotnet test", feed.Entries[2].StepLine);

        // nothing new: one call from the end, no change
        Assert.Equal(4, await feed.Refresh());
        Assert.Equal(4, desk.Asked[^1]);
        Assert.Equal(4, feed.Entries.Count);
    }

    [Fact]
    public async Task A_long_transcript_comes_in_pages_and_a_shorter_one_is_read_afresh()
    {
        var desk = new FakeDesktop { Page = 2 };
        for (var i = 0; i < 5; i++) desk.Add("agent", $"line {i}");
        var feed = new AgentFeed(desk.Call, "a1");
        await feed.Refresh();
        Assert.Equal(["line 0", "line 1", "line 2", "line 3", "line 4"], feed.Entries.Select(e => e.Text));
        Assert.Equal([0, 2, 4], desk.Asked);

        desk.Items.RemoveRange(2, 3); // the desktop restarted on another transcript
        await feed.Refresh();
        Assert.Equal(["line 0", "line 1"], feed.Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task A_waiting_question_is_read_again_until_answered_and_send_queues_mid_turn()
    {
        var desk = new FakeDesktop { Status = "needsyou" };
        desk.Add("question", "Which logo?", pending: true);
        var feed = new AgentFeed(desk.Call, "a1");
        await feed.Refresh();
        Assert.True(feed.NeedsYou);
        Assert.False(feed.Entries[0].Settled);
        desk.Items[0]["pending"] = false;
        desk.Items[0]["text"] = "Which logo? → Pink";
        await feed.Refresh();
        Assert.Equal("Which logo? → Pink", feed.Entries.Single().Text);

        Assert.Equal("", await feed.Send("   "));
        Assert.Empty(desk.Said);
        await feed.Send(" stop, use the teal one ");
        var said = Assert.Single(desk.Said);
        Assert.Equal("stop, use the teal one", (string)said["text"]!);
        Assert.True((bool)said["queue"]!);
        Assert.Equal("a1", (string)said["id"]!);
    }

    [Fact]
    public void Long_output_folds_to_a_screenful_and_the_list_puts_needs_you_first()
    {
        var text = string.Join("\r\n", Enumerable.Range(1, 30).Select(i => $"line {i}")) + "\n";
        var (shown, hidden) = TranscriptEntry.FoldText(text);
        Assert.Equal(TranscriptEntry.FoldLines, shown.Split('\n').Length);
        Assert.Equal(30 - TranscriptEntry.FoldLines, hidden);
        Assert.Equal(("short", 0), TranscriptEntry.FoldText("short"));
        // one long paragraph: cut at a word near the character budget, its rest counted as a line to show
        var paragraph = string.Join(" ", Enumerable.Repeat("word", 600));
        var (cut, rest) = TranscriptEntry.FoldText(paragraph);
        Assert.EndsWith("word …", cut);
        Assert.InRange(cut.Length, TranscriptEntry.FoldChars / 2, TranscriptEntry.FoldChars + 2);
        Assert.Equal(1, rest);
        // short lines but many characters: stops at a line's end before the budget
        var wide = string.Join("\n", Enumerable.Repeat(new string('y', 500), 5));
        Assert.Equal((string.Join("\n", Enumerable.Repeat(new string('y', 500), 2)), 3), TranscriptEntry.FoldText(wide));

        var list = RemoteAgent.List(JsonNode.Parse("""
            [{"id":"a","name":"done one","state":"idle","project":"P","projectId":"p"},
             {"id":"b","name":"busy","state":"working","doing":"Editing a file"},
             {"id":"c","name":"asks","state":"needsyou","card":"WOR-2","open":true}]
            """));
        Assert.Equal(["c", "b", "a"], list.Select(a => a.Id));
        Assert.Equal("Needs you", list[0].StateText);
        Assert.True(list[0].Open);
        Assert.Equal("WOR-2", list[0].Card);
        Assert.Equal("Editing a file", list[1].Doing);
        Assert.Empty(RemoteAgent.List(JsonValue.Create("nope")));
    }
}
