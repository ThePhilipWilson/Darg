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
}
