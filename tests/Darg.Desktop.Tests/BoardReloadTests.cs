using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0227: the board's reload ran the agents list's RefreshCards, and each agent that forgot reloaded every row,
/// which ran RefreshCards again inside it: stalls.log showed Board.Reload > Agents.RefreshCards > Board.Reload ten deep, one
/// level per agent forgotten, each reading every card file of every board twice on the UI thread. Now the boards are read
/// in one pass off the UI thread, RefreshCards answers from it, and a forgotten agent only redraws the tiles.</summary>
public class BoardReloadTests
{
    /// <summary>Every StallWatch section's path as it ends, while <paramref name="body"/> runs.</summary>
    static async Task<List<string>> Sections(Func<Task> body)
    {
        var seen = new List<string>();
        StallWatch.Shared.SectionEnded = p => { lock (seen) seen.Add(p); };
        try { await body(); }
        finally { StallWatch.Shared.SectionEnded = null; }
        lock (seen) return seen.ToList();
    }

    /// <summary>A section path where the board is reloaded inside RefreshCards: the re-entry.</summary>
    static bool ReEntered(string path) =>
        path.IndexOf("Agents.RefreshCards >", StringComparison.Ordinal) is var at and >= 0 && path[at..].Contains("Board.Reload", StringComparison.Ordinal);

    const string HowToCheck = "\n\n## How to check\n\nOpen the board and look at the logo.\n";

    /// <summary>The stalls.log case: every state change of a QA agent reloads the board (so its tile's Run QA comes and
    /// goes), and the reload's RefreshCards forgets a QA agent whose turn is over; that forget reloaded every row again,
    /// inside the first, once per agent. Four QA runs ending together now forget their agents with no reload inside
    /// RefreshCards, and the board is read in passes off the UI thread rather than once per row per change.</summary>
    [AvaloniaFact]
    public async Task Several_QA_agents_finishing_together_never_reload_the_board_inside_RefreshCards()
    {
        using var app = DargApp.Make();
        var cards = Enumerable.Range(1, 4).Select(i => app.Card($"check number {i}", "QA Ready", "Make the logo crisp." + HowToCheck)).ToList();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        var seen = await Sections(async () =>
        {
            await app.Api("qa-all", new { project = "proj" });
            await app.Until(() => app.Agents.Agents.Count(a => a.Purpose == AgentPurposes.Qa) == 4, "the four QA agents to start");
            await app.Until(() => app.Agents.Agents.Count == 0, "the four QA agents to end their turns and be forgotten");
            await DargApp.Settle(500);
        });

        Assert.Contains(seen, p => p.EndsWith("Agents.RefreshCards", StringComparison.Ordinal));
        Assert.DoesNotContain(seen, ReEntered);
        Assert.All(app.Board.Lists.SelectMany(l => l.Cards), t => Assert.Null(t.Agent)); // the tiles dropped their agents
        Assert.All(cards, c => Assert.Equal("QA Ready", app.Read(c.Id).Status));
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Agents_ending_their_turns_at_once_never_reload_the_board_inside_RefreshCards()
    {
        using var app = DargApp.Make();
        var cards = Enumerable.Range(1, 4).Select(i => app.Card($"work item {i}", "To Do")).ToList();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        var seen = await Sections(async () =>
        {
            await app.Api("work-all", new { project = "proj" });
            await app.Until(() => app.Agents.Agents.Count == 4, "the four agents to start");
            await app.Until(() => app.Agents.Agents.Count == 0, "the four agents to end their turns and be forgotten");
            await DargApp.Settle(500);
        });

        Assert.DoesNotContain(seen, ReEntered);
        Assert.All(app.Board.Lists.SelectMany(l => l.Cards), t => Assert.Null(t.Agent));
        Assert.Equal("", app.Crashes());
    }

    /// <summary>A board file changed by someone else (an agent, a text editor) is read off the UI thread: the board on screen
    /// shows it once the pass lands, and the card agents' lookups (each card's board, done cards) answer from that read.</summary>
    [AvaloniaFact]
    public async Task A_card_moved_on_disk_shows_after_the_pass_and_the_lookups_answer_from_it()
    {
        using var app = DargApp.Make();
        var card = app.Card("moved by someone else", "To Do");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var passes = app.Board.Passes;

        BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("Done"));

        await app.Until(() => app.Board.Lists.Single(l => l.Id == app.ColumnId("Done")).Cards.Any(c => c.Id == card.Id), "the move to show on the board");
        Assert.True(app.Board.Passes > passes);
        Assert.Contains(card.Id, app.Board.DoneCardIds());
        Assert.Equal("Main", app.Board.CardBoards()[card.Id]);
        Assert.Equal("", app.Crashes());
    }
}
