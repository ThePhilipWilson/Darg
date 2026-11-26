using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0234: replies shown as they are written, from Claude Code's --include-partial-messages stream.</summary>
public sealed class StreamingTests
{
    // recorded from Claude Code 2.1.326 (claude -p --output-format stream-json --verbose --include-partial-messages, Haiku),
    // 2026-11-26; the usage objects cut short, and a few deltas left out of the middle
    static readonly string[] Recorded =
    [
        """{"type":"stream_event","event":{"type":"message_start","message":{"model":"claude-haiku-5-5","id":"msg_011ChRymQMqnJPkGg6iwt4rK","type":"message","role":"assistant","content":[],"stop_reason":null}},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"0a1b","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"stream_event","event":{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"d3b81c6c-172b-453f-8c4a-a0e273acf168","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Rivers car"}},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"ba76f04d-a636-4950-bf8b-d803e0017572","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ry"}},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"6725248c-aaa1-4329-899d-c61208a3fd88","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" water"}},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"a38af144-827a-4ed8-b059-445999b4abf1","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" to the sea."}},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"2dbad18e-7ce6-49f7-a649-4fa2109ed73d","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"assistant","message":{"model":"claude-haiku-5-5","id":"msg_011ChRymQMqnJPkGg6iwt4rK","type":"message","role":"assistant","content":[{"type":"text","text":"Rivers carry water to the sea."}],"stop_reason":null},"parent_tool_use_id":null,"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","uuid":"5e2f"}""",
        """{"type":"stream_event","event":{"type":"content_block_stop","index":0},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"631e18ec-a6cf-4953-bf4f-8f9c9538ae44","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"stream_event","event":{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":49}},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"77aa","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
        """{"type":"stream_event","event":{"type":"message_stop"},"session_id":"844cf62a-a8ca-4199-af1c-d45701bc5572","parent_tool_use_id":null,"uuid":"9b8b1806-1543-45b1-9a76-a56c98636ef1","api_message_id":"msg_011ChRymQMqnJPkGg6iwt4rK"}""",
    ];

    const string Id = "msg_011ChRymQMqnJPkGg6iwt4rK";

    [Fact]
    public void Recorded_stream_events_give_each_piece_of_text_then_the_whole_message_with_its_id()
    {
        var events = Recorded.SelectMany(StreamParser.Parse).ToList();

        Assert.Equal(
            [new AssistantDelta(Id, 0, "Rivers car"), new AssistantDelta(Id, 0, "ry"), new AssistantDelta(Id, 0, " water"),
             new AssistantDelta(Id, 0, " to the sea."), new AssistantText("Rivers carry water to the sea.", Id)],
            events);
        Assert.Equal("Rivers carry water to the sea.", string.Concat(events.OfType<AssistantDelta>().Select(d => d.Text)));
    }

    [Theory] // not text the person reads, or not this session's own reply
    [InlineData("""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"hmm"}},"parent_tool_use_id":null,"api_message_id":"m"}""")]
    [InlineData("""{"type":"stream_event","event":{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"file"}},"parent_tool_use_id":null,"api_message_id":"m"}""")]
    [InlineData("""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"a subagent's"}},"parent_tool_use_id":"toolu_01","api_message_id":"m"}""")]
    [InlineData("""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":""}},"parent_tool_use_id":null,"api_message_id":"m"}""")]
    [InlineData("""{"type":"stream_event","event":{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}},"parent_tool_use_id":null}""")]
    [InlineData("""{"type":"stream_event"}""")]
    public void Other_stream_events_give_nothing(string line) => Assert.Empty(StreamParser.Parse(line));

    [Fact]
    public void A_whole_message_without_an_id_still_parses()
    {
        Assert.Equal(new AssistantText("hi"), Assert.Single(StreamParser.Parse("""{"type":"assistant","message":{"content":[{"type":"text","text":" hi "}]}}""")));
    }

    /// <summary>An inbox whose posts wait until the test runs them, as the UI thread would get to them.</summary>
    sealed class Ui
    {
        readonly Queue<Action> _posted = new();
        public readonly List<AgentEvent> Seen = [];
        public int Deliveries;
        public StreamInbox Inbox { get; }
        public Ui(TimeSpan every) =>
            Inbox = new StreamInbox(a => { lock (_posted) _posted.Enqueue(a); }, events => { Deliveries++; Seen.AddRange(events.Select(e => e.Event)); }, every);
        public int Pending { get { lock (_posted) return _posted.Count; } }
        public void Run()
        {
            while (true)
            {
                Action a;
                lock (_posted) { if (_posted.Count == 0) return; a = _posted.Dequeue(); }
                a();
            }
        }
    }

    [Fact]
    public async Task Streamed_text_is_posted_once_per_batch_merged_and_never_behind_the_whole_message()
    {
        var ui = new Ui(TimeSpan.FromMilliseconds(30));
        for (var i = 0; i < 50; i++) ui.Inbox.Add(new AssistantDelta("m1", 0, "ab"));
        await Task.Delay(300);
        Assert.Equal(1, ui.Pending); // fifty pieces, one post
        ui.Run();
        Assert.Equal(new AssistantDelta("m1", 0, string.Concat(Enumerable.Repeat("ab", 50))), Assert.Single(ui.Seen));

        // the whole message is posted at once and takes the pieces before it along, in order; the next message's pieces
        // queued after it come after it, as their own run
        ui.Seen.Clear();
        ui.Inbox.Add(new AssistantDelta("m1", 0, "c"));
        ui.Inbox.Add(new AssistantText("abc", "m1"));
        ui.Inbox.Add(new AssistantDelta("m2", 0, "x"));
        ui.Inbox.Add(new AssistantDelta("m2", 0, "y"));
        ui.Inbox.Add(new AssistantDelta("m2", 1, "z"));
        ui.Run();
        await Task.Delay(300);
        ui.Run();
        Assert.Equal([new AssistantDelta("m1", 0, "c"), new AssistantText("abc", "m1"), new AssistantDelta("m2", 0, "xy"), new AssistantDelta("m2", 1, "z")], ui.Seen);
    }

    [Fact]
    public void A_long_reply_streams_in_fewer_bigger_batches()
    {
        Assert.Equal(StreamInbox.DefaultEvery, StreamInbox.PaceFor(0));
        Assert.Equal(TimeSpan.FromMilliseconds(90), StreamInbox.PaceFor(5000));
        Assert.Equal(TimeSpan.FromMilliseconds(250), StreamInbox.PaceFor(100_000));
    }

    [Fact]
    public void Every_other_event_is_posted_at_once_as_before()
    {
        var ui = new Ui(TimeSpan.FromSeconds(10));
        ui.Inbox.Add(new SessionStarted("s", "m", "c"));
        ui.Inbox.Add(new TurnFinished(false, "done", null));
        Assert.Equal(2, ui.Pending);
        ui.Run();
        Assert.Equal([new SessionStarted("s", "m", "c"), new TurnFinished(false, "done", null)], ui.Seen);
        Assert.Equal(1, ui.Deliveries); // the second post found nothing left to hand over
    }

    [Fact]
    public void Timings_mark_the_first_text_streamed()
    {
        var lines = new List<string>();
        var clock = new AgentTimings("abc", lines.Add, toFile: false);
        clock.Sent();
        foreach (var line in Recorded) clock.Saw(StreamParser.Read(line)!);
        clock.Shown(new AssistantDelta(Id, 0, "Rivers"), TimeSpan.FromMilliseconds(5));
        Assert.Single(lines, l => l.Contains("first text streamed"));
        Assert.True(lines.FindIndex(l => l.Contains("first text streamed")) < lines.FindIndex(l => l.Contains("first reply")));
        Assert.Contains(lines, l => l.Contains("first text shown"));
    }
}
