using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0206: an agent session's clock, a line per step of its start and turns, from the stream-json it reads.</summary>
public sealed class AgentTimingsTests
{
    static JsonObject J(string json) => (JsonObject)JsonNode.Parse(json)!;

    static (AgentTimings Clock, List<string> Lines) Clock()
    {
        var lines = new List<string>();
        return (new AgentTimings("abc123", lines.Add, toFile: false), lines);
    }

    /// <summary>What each line says, without its time stamps.</summary>
    static List<string> Steps(List<string> lines) => lines.Select(l => l[(l.IndexOf(" · ", StringComparison.Ordinal) + 3)..]).ToList();

    [Fact]
    public void A_turn_logs_every_step_in_order_and_sums_it_up()
    {
        var (clock, lines) = Clock();
        {
            clock.PickStarted();
            clock.PickEnded("Quick", "rule kind: chat");
            clock.ProcessStarted(TimeSpan.FromMilliseconds(40), 9);
            clock.Sent();
            clock.Saw(J("""{"type":"control_response","response":{"subtype":"success","request_id":"init","response":{}}}"""));
            clock.Saw(J("""{"type":"system","subtype":"hook_started","hook_name":"SessionStart:startup"}"""));
            clock.Saw(J("""{"type":"system","subtype":"hook_response","hook_name":"SessionStart:startup","outcome":"error"}"""));
            clock.Saw(J("""{"type":"system","subtype":"init","mcp_servers":[{"name":"a","status":"connected"},{"name":"tabby","status":"failed"},{"name":"unity-mcp","status":"failed"},{"name":"g","status":"needs-auth"}]}"""));
            clock.Saw(J("""{"type":"assistant","message":{"content":[{"type":"text","text":"Looking"}]}}"""));
            clock.Saw(J("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Read","input":{}}]}}"""));
            clock.Saw(J("""{"type":"control_request","request_id":"p1","request":{"subtype":"can_use_tool","tool_name":"Bash"}}"""));
            clock.Answered("p1");
            clock.Saw(J("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"x"}]}}"""));
            clock.Saw(J("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t2","name":"Edit","input":{}}]}}"""));
            clock.Saw(J("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t2","content":"x"}]}}"""));
            clock.Saw(J("""{"type":"result","is_error":false,"result":"done","duration_ms":7298,"duration_api_ms":5220}"""));
            clock.Exited(0);

            var steps = Steps(lines);
            Assert.Equal("model pick start", steps[0]);
            Assert.StartsWith("model pick end (", steps[1]);
            Assert.EndsWith(" · Quick: rule kind: chat", steps[1]);
            Assert.Equal("process start (spawn 40 ms, 9 args)", steps[2]);
            Assert.Equal("message sent", steps[3]);
            Assert.Equal("initialized (control)", steps[4]);
            Assert.Equal("hook start SessionStart:startup", steps[5]);
            Assert.Equal("hook end SessionStart:startup (error)", steps[6]);
            Assert.Equal("init · 4 mcp · failed: tabby, unity-mcp · needs-auth: g", steps[7]);
            Assert.Equal("first reply", steps[8]);
            Assert.Equal("first tool call Read", steps[9]);
            Assert.Equal("permission asked Bash (p1)", steps[10]);
            Assert.Equal("permission answered (p1)", steps[11]);
            Assert.StartsWith("first tool result (", steps[12]);
            Assert.StartsWith("turn end (", steps[13]); // the second tool and its result aren't firsts
            Assert.Contains(", first tool at ", steps[13]);
            Assert.EndsWith(" · Claude Code says 7.30 s, API 5.22 s", steps[13]);
            Assert.Equal("process exit 0", steps[14]);
            Assert.Equal(15, steps.Count);

            // every line names the agent and its time from the start; those in the turn its time in the turn too
            Assert.All(lines, l => Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} abc123 \+\d+\.\d\d s ", l));
            Assert.DoesNotContain(" turn ", lines[2]);
            Assert.Matches(@" turn 1 \+\d+\.\d\d s · first tool call Read$", lines[9]);

            // the first turn's sum counts from the agent being made: pick, start-up and all
            Assert.Matches(@"^\d+\.\d\d s \(model pick \d+\.\d\d s · init at \d+\.\d\d s · first tool at \d+\.\d\d s\)$", clock.LastTurn);
        }
    }

    [Fact]
    public void Later_turns_count_from_their_message_and_a_message_sent_mid_turn_starts_the_next()
    {
        var (clock, lines) = Clock();
        {
            clock.ProcessStarted(TimeSpan.Zero, 1);
            clock.Sent();
            clock.Saw(J("""{"type":"result","is_error":false,"result":"done"}"""));
            Assert.Matches(@"^\d+\.\d\d s$", clock.LastTurn); // no pick, no init, no tools: just how long

            clock.Sent();
            clock.Sent(); // queued while turn 2 runs
            clock.Saw(J("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Grep","input":{}}]}}"""));
            clock.Saw(J("""{"type":"result","is_error":false,"result":"done"}"""));
            Assert.Matches(@"^\d+\.\d\d s \(first tool at \d+\.\d\d s\)$", clock.LastTurn);
            clock.Saw(J("""{"type":"result","is_error":false,"result":"done"}"""));

            var steps = Steps(lines);
            Assert.Equal(["process start (spawn 0 ms, 1 args)", "message sent"], steps[..2]);
            Assert.StartsWith("turn end (", steps[2]);
            Assert.EndsWith(", no tools)", steps[2]);
            Assert.Equal(["message sent", "message sent mid-turn", "first reply", "first tool call Grep"], steps[3..7]);
            Assert.StartsWith("turn end (", steps[7]);
            Assert.Equal("next message's turn", steps[8]);
            Assert.Contains(" turn 3 +", lines[8]);
            Assert.StartsWith("turn end (", steps[9]);
        }
    }

    /// <summary>WOR-0224: Claude Code folds a message sent mid-turn into the turn running (one result for both), so the
    /// queued message's "turn" ran from that result to whatever the person sent next: a Fettle reply of 8.7 s logged as
    /// 238 s, and the next message logged as sent mid-turn.</summary>
    [Fact]
    public void A_message_folded_into_the_turn_running_gets_no_turn_of_its_own()
    {
        var (clock, lines) = Clock();
        clock.Sent();
        clock.Sent(); // mid-turn: Claude Code reads it into this turn
        clock.Saw(J("""{"type":"result","is_error":false,"result":"both answered"}"""));
        clock.Saw(J("""{"type":"rate_limit_event","rate_limit_info":{}}""")); // between turns: not a turn starting
        Thread.Sleep(30);
        clock.Sent(); // the person's next message, a while later
        clock.Saw(J("""{"type":"system","subtype":"init"}"""));
        clock.Saw(J("""{"type":"result","is_error":false,"result":"x"}"""));

        var steps = Steps(lines);
        Assert.Equal(["message sent", "message sent mid-turn"], steps[..2]);
        Assert.StartsWith("turn end (", steps[2]);
        Assert.Equal(["message sent", "init"], steps[3..5]); // a turn of its own, not "sent mid-turn"
        Assert.DoesNotContain("next message's turn", steps);
        Assert.Contains(" turn 2 +0.00 s · message sent", lines[3]); // counted from its own message, not the last result
    }

    [Fact]
    public void Thinking_is_one_line_a_turn_and_the_kind_resume_and_claudes_own_split_are_logged()
    {
        var (clock, lines) = Clock();
        clock.ProcessStarted(TimeSpan.FromMilliseconds(5), 12, resume: true);
        clock.Sent();
        clock.Describe("fettle");
        clock.Describe("chat"); // once only
        for (var i = 0; i < 5; i++) clock.Saw(J("""{"type":"system","subtype":"thinking_tokens","tokens":10}"""));
        clock.Saw(J("""{"type":"system","subtype":"task_started"}""")); // other system lines stay
        clock.Saw(J("""{"type":"result","is_error":false,"result":"x","duration_ms":4000,"duration_api_ms":3000,"time_to_request_ms":350,"ttft_ms":1840}"""));
        clock.Sent();
        clock.Saw(J("""{"type":"system","subtype":"thinking_tokens","tokens":10}"""));
        clock.Saw(J("""{"type":"system","subtype":"thinking_tokens","tokens":20}"""));
        clock.Saw(J("""{"type":"result","is_error":false,"result":"x"}"""));

        var steps = Steps(lines);
        Assert.Equal("process start (spawn 5 ms, 12 args, resume)", steps[0]);
        Assert.Equal(["message sent", "agent fettle", "thinking", "system task_started"], steps[1..5]);
        Assert.StartsWith("turn end (", steps[5]);
        Assert.EndsWith(" · Claude Code says 4.00 s, API 3.00 s, request at 0.35 s, first token at 1.84 s", steps[5]);
        Assert.Equal(["message sent", "thinking"], steps[6..8]); // a new turn's thinking is logged again
        Assert.StartsWith("turn end (", steps[8]);
        Assert.Equal(9, steps.Count);
    }

    [Fact]
    public void The_transcript_showing_what_was_read_logs_its_first_text_and_the_turns_longest_wait()
    {
        var (clock, lines) = Clock();
        clock.Sent();
        clock.Shown(new SessionStarted("s", "m", "c"), TimeSpan.FromMilliseconds(30));
        clock.Shown(new AssistantText("one"), TimeSpan.FromMilliseconds(12));
        clock.Shown(new AssistantText("two"), TimeSpan.FromMilliseconds(400)); // not a first
        clock.Saw(J("""{"type":"result","is_error":false,"result":"x"}"""));
        clock.Shown(new TurnFinished(false, "done", null), TimeSpan.FromMilliseconds(8));
        clock.Sent();
        clock.Shown(new AssistantText("three"), TimeSpan.FromMilliseconds(3));
        clock.Saw(J("""{"type":"result","is_error":false,"result":"x"}"""));
        clock.Shown(new TurnFinished(false, "done", null), TimeSpan.FromMilliseconds(2));

        var steps = Steps(lines).Where(s => !s.StartsWith("turn end (", StringComparison.Ordinal)).ToList();
        Assert.Equal(
        [
            "message sent", "first text shown (12 ms after it was read)", "turn 1 shown (8 ms after it was read, the turn's longest wait 400 ms)",
            "message sent", "first text shown (3 ms after it was read)", "turn 2 shown (2 ms after it was read, the turn's longest wait 3 ms)",
        ], steps);
    }

    [Theory]
    [InlineData("""{"type":"result","subtype":"success","is_error":false,"result":"Deep | open questions","duration_ms":1352}""", true, "Deep | open questions")]
    [InlineData("""{"type":"result","subtype":"error_during_execution","is_error":true,"result":"API Error"}""", false, "API Error")]
    public void Haikus_json_answer_is_read_from_its_result_line(string line, bool ok, string text) =>
        Assert.Equal((ok, text), Haiku.Result(line));

    [Theory]
    [InlineData("")]
    [InlineData("Deep | open questions")]
    [InlineData("""{"type":"system","subtype":"init"}""")]
    [InlineData("{not json")]
    public void Any_other_line_is_not_haikus_answer(string line) => Assert.Null(Haiku.Result(line));

    [Fact]
    public void Haiku_reads_none_of_the_persons_settings_and_answers_in_json()
    {
        // WOR-0224: the person's hooks, plugins and CLAUDE.md files doubled every model pick for a self-contained prompt
        var args = Haiku.Args.ToList();
        Assert.Equal("", args[args.IndexOf("--setting-sources") + 1]);
        Assert.Equal("json", args[args.IndexOf("--output-format") + 1]);
        Assert.Contains("--strict-mcp-config", args);
    }
}
