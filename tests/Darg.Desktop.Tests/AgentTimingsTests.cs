using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0206: every agent's start and turns are timed into agents.log beside perf.log, and its "Turn done"
/// line says how long it took, so Darg's speed can be set beside a plain claude -p.</summary>
public class AgentTimingsTests
{
    [AvaloniaFact]
    public async Task An_agents_start_and_turn_go_to_agents_log_in_order_and_its_turn_line_says_how_long()
    {
        using var app = DargApp.Make();
        await app.Start();

        await app.Api("start-agent", new { project = "proj", prompt = "look at the code" });
        var agent = Assert.Single(app.Agents.Agents);
        await app.Until(() => agent.State == AgentState.Idle, "the agent's turn to end");
        await AgentTimings.Writer.Flush();

        var log = Path.Combine(app.Home, "agents.log");
        Assert.True(File.Exists(log), "agents.log beside perf.log");
        var lines = File.ReadAllLines(log).Where(l => l.Contains($" {agent.Id} +")).ToList();
        string[] order =
        [
            "model pick start", "model pick end", "process start", "message sent", "initialized (control)", "hook start SessionStart",
            "hook end SessionStart", "init · 2 mcp · failed: down-mcp", "first reply", "first tool call Read", "first tool result", "turn end",
        ];
        var at = -1;
        foreach (var step in order)
        {
            var i = lines.FindIndex(at + 1, l => l.Contains(" · " + step));
            Assert.True(i > at, $"'{step}' after line {at} in:\n{string.Join("\n", lines)}");
            at = i;
        }
        Assert.Contains(lines, l => l.Contains(" · turn end (") && l.Contains(", first tool at ") && l.Contains("Claude Code says"));

        // the transcript says it too, without opening the log
        var done = agent.Items.OfType<NoteItem>().Last(n => n.Text.StartsWith("Turn done", StringComparison.Ordinal));
        Assert.Matches(@"^Turn done · \d+\.\d\d s \(model pick \d+\.\d\d s · init at \d+\.\d\d s · first tool at \d+\.\d\d s\)$", done.Text);
        Assert.True(agent.FinishedTurn, "a timed Turn done line still reads as a finished turn");
    }
}
