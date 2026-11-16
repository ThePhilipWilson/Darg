using System.Text.Json.Nodes;
using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class StreamParserTests
{
    [Fact]
    public void Init_gives_the_session()
    {
        var e = Assert.Single(StreamParser.Parse("""{"type":"system","subtype":"init","session_id":"abc","model":"claude-haiku-5-5","cwd":"D:\\x","apiKeySource":"none"}"""));
        Assert.Equal(new SessionStarted("abc", "claude-haiku-5-5", "D:\\x"), e);
        Assert.True(((SessionStarted)e).OnSubscription);
    }

    [Fact]
    public void An_api_key_session_says_so() =>
        Assert.False(Assert.IsType<SessionStarted>(Assert.Single(StreamParser.Parse(
            """{"type":"system","subtype":"init","session_id":"a","model":"m","cwd":"c","apiKeySource":"ANTHROPIC_API_KEY"}"""))).OnSubscription);

    [Fact]
    public void Assistant_text_and_tool_use_come_out_in_order_and_thinking_is_skipped()
    {
        var events = StreamParser.Parse("""
            {"type":"assistant","message":{"content":[{"type":"thinking","thinking":"hm"},{"type":"text","text":"Writing it."},{"type":"tool_use","id":"t1","name":"Write","input":{"file_path":"a.txt","content":"hi"}}]}}
            """).ToList();
        Assert.IsType<AssistantText>(events[0]);
        var call = Assert.IsType<ToolCall>(events[1]);
        Assert.Equal("Write", call.Name);
        Assert.Equal("a.txt", (string?)call.Input["file_path"]);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Tool_results_read_as_text_from_a_string_or_blocks()
    {
        var a = Assert.IsType<ToolResult>(Assert.Single(StreamParser.Parse("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"done"}]}}""")));
        Assert.Equal("done", a.Text);
        var b = Assert.IsType<ToolResult>(Assert.Single(StreamParser.Parse("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t2","is_error":true,"content":[{"type":"text","text":"no"}]}]}}""")));
        Assert.True(b.IsError);
        Assert.Equal("no", b.Text);
    }

    [Fact]
    public void A_question_arrives_as_a_permission_request_needing_the_person()
    {
        // As captured from Claude Code 2.1.325 on 2026-11-16.
        var e = Assert.IsType<PermissionAsked>(Assert.Single(StreamParser.Parse("""
            {"type":"control_request","request_id":"761626ff","request":{"subtype":"can_use_tool","tool_name":"AskUserQuestion","input":{"questions":[{"question":"Hello or goodbye?","header":"Note","options":[{"label":"Hello","description":"h"},{"label":"Goodbye","description":"g"}],"multiSelect":false}]},"tool_use_id":"toolu_01","requires_user_interaction":true}}
            """)));
        Assert.Equal("AskUserQuestion", e.ToolName);
        Assert.True(e.RequiresUser);
        Assert.Equal("761626ff", e.RequestId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"type":"rate_limit_event"}""")]
    [InlineData("[1,2]")]
    public void Lines_the_host_doesnt_show_give_nothing(string line) => Assert.Empty(StreamParser.Parse(line));
}

/// <summary>Runs the real claude CLI. Opt in with WORKBENCH_LIVE=1 (it spends a little on a Haiku turn).</summary>
public sealed class LiveAgentTests
{
    [Fact]
    public async Task A_question_is_answered_from_the_host_and_the_answer_is_used()
    {
        if (Environment.GetEnvironmentVariable("WORKBENCH_LIVE") != "1") return;
        var dir = Directory.CreateTempSubdirectory("wb-live-").FullName;
        await using var s = AgentSession.Start(dir, "haiku");
        var done = new TaskCompletionSource<TurnFinished>();
        var asked = false;
        s.Event += e =>
        {
            switch (e)
            {
                case PermissionAsked { ToolName: "AskUserQuestion" } q:
                    asked = true;
                    var question = (string)q.Input["questions"]![0]!["question"]!;
                    s.Answer(q.RequestId, q.Input, new Dictionary<string, string> { [question] = "goodbye" });
                    break;
                case PermissionAsked p: s.Allow(p.RequestId, p.Input); break;
                case TurnFinished f: done.TrySetResult(f); break;
                case SessionExited x: done.TrySetException(new Exception("exited: " + x.Stderr)); break;
            }
        };
        s.Send("Use AskUserQuestion to ask whether note.txt should say hello or goodbye (two options), then write the answer into note.txt with the Write tool. Nothing else.");
        var result = await done.Task.WaitAsync(TimeSpan.FromMinutes(3));
        Assert.False(result.IsError, result.Text);
        Assert.True(asked, "the question never reached the host");
        Assert.Equal("goodbye", File.ReadAllText(Path.Combine(dir, "note.txt")).Trim().ToLowerInvariant());
    }
}
