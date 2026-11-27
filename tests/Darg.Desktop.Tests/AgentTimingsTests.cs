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
        // claude starts while Haiku picks its model, and its message goes once the pick is in (WOR-0233); fake claude's
        // initialize may land either side of the pick's end, so it is checked apart
        string[] order =
        [
            "model pick start", "process start", "model pick end", "message sent", "hook start SessionStart",
            "hook end SessionStart", "init · 2 mcp · failed: down-mcp", "first reply", "first tool call Read", "first tool result", "turn end",
        ];
        Assert.True(lines.FindIndex(l => l.Contains(" · initialized (control)")) > lines.FindIndex(l => l.Contains(" · process start")),
            string.Join("\n", lines));
        var at = -1;
        foreach (var step in order)
        {
            var i = lines.FindIndex(at + 1, l => l.Contains(" · " + step));
            Assert.True(i > at, $"'{step}' after line {at} in:\n{string.Join("\n", lines)}");
            at = i;
        }
        Assert.Contains(lines, l => l.Contains(" · turn end (") && l.Contains(", first tool at ") && l.Contains("Claude Code says"));
        // WOR-0224: what it is, and how long the transcript took to show what was read
        Assert.Contains(lines, l => l.EndsWith(" · agent chat", StringComparison.Ordinal));
        Assert.Contains(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @" · first text shown \(\d+ ms after it was read\)$"));
        Assert.Contains(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @" · turn 1 shown \(\d+ ms after it was read, the turn's longest wait \d+ ms\)$"));

        // the transcript says it too, without opening the log
        var done = agent.Items.OfType<NoteItem>().Last(n => n.Text.StartsWith("Turn done", StringComparison.Ordinal));
        Assert.Matches(@"^Turn done · \d+\.\d\d s \(model pick \d+\.\d\d s · init at \d+\.\d\d s · first tool at \d+\.\d\d s\)$", done.Text);
        Assert.True(agent.FinishedTurn, "a timed Turn done line still reads as a finished turn");
    }

    /// <summary>WOR-0224: tools/perf/agents.ps1 tells Fettle's replies from card agents' by this line.</summary>
    [AvaloniaFact]
    public async Task A_fettle_chat_names_itself_in_agents_log()
    {
        using var app = DargApp.Make();
        await app.Start();

        Assert.True(app.Main.Mascot.Ask("look at the board", "look at the board"));
        var chat = app.Main.Mascot.Agent!;
        await app.Until(() => chat.State == AgentState.Idle, "Fettle's turn to end");
        await AgentTimings.Writer.Flush();

        var lines = File.ReadAllLines(Path.Combine(app.Home, "agents.log")).Where(l => l.Contains($" {chat.Id} +")).ToList();
        Assert.Contains(lines, l => l.EndsWith(" · agent fettle", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.EndsWith(" · agent chat", StringComparison.Ordinal));
    }
}
