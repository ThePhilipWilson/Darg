using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0131: a card reaching done gets its done report, then its agents close and leave the list: settled ones at
/// once, one still at work once its turn ends (never killed mid-turn). The Reports section's Clean up does the same for
/// agents already left on done cards. Each case is one the report path (an agent named in a done report goes) misses.</summary>
public class DoneCardTests
{
    /// <summary>A QA report for the card by an agent that is long gone, as a QA pass leaves behind.</summary>
    static string QaReport(DargApp app, Card card)
    {
        var folder = app.Project.Reports!.Folder;
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{DateTime.Now.AddMinutes(-5):yyyyMMdd-HHmm}-{card.Id}-qa.md");
        File.WriteAllText(file, $"---\nstatus: qa\ncard: {card.Id}\ntitle: {card.Title}\nagent: 0000000000\ncommits: \nrepo: {app.Repo}\n" +
                                $"date: {DateTime.Now.AddMinutes(-5):yyyy-MM-dd HH:mm}\nread: false\n---\n\n## Changed\n\nThe logo.\n\n## Look at\n\nThe logo.\n\n## Checked\n\nBuilt it.\n");
        return file;
    }

    [AvaloniaFact]
    public async Task A_card_moved_to_done_gets_its_done_report_then_its_finished_agent_goes()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.ForCard(card.Id)!;
        await app.Until(() => agent.State == AgentState.Idle, "the agent's turn to end");
        var qa = QaReport(app, card);
        await DargApp.Settle(500);
        Assert.Contains(agent, app.Agents.Agents);

        BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("Done")); // a drag, an approval or an agent: all the same file change

        await app.Until(() => !app.Agents.Agents.Contains(agent), "the agent on the done card to be forgotten");
        Assert.False(agent.Open); // its session closed too
        Assert.False(File.Exists(qa)); // the report step ran first: the QA report archived...
        var reports = ReportStore.List(app.Project.Reports!.Folder);
        Assert.Contains(reports, r => r.Card == card.Id && !r.Qa); // ...and the done report written
        Assert.Null(app.Board.AgentFor?.Invoke(card.Id));
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task An_agent_at_work_when_its_card_reaches_done_is_left_to_finish_then_goes()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "4");
        try
        {
            using var app = DargApp.Make();
            var card = app.Card("fix the logo aliasing", "To Do");
            await app.Start();
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = app.Agents.ForCard(card.Id)!;
            await app.Until(() => agent.State == AgentState.Working, "the agent to start working");

            // the agent moves its own card to done mid-turn, as a QA agent passing it or a card agent finishing does
            BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("Done"));
            await DargApp.Settle(1000);
            Assert.Contains(agent, app.Agents.Agents); // not killed mid-turn
            Assert.Equal(AgentState.Working, agent.State);

            await app.Until(() => !app.Agents.Agents.Contains(agent), "the agent to go once its turn ended");
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task Clean_up_forgets_the_agents_left_on_done_cards()
    {
        using var app = DargApp.Make();
        var done = app.Card("finished long ago", "Done");
        var open = app.Card("still being worked on", "To Do");
        await app.Start();
        // its QA report is still in the list, as a card done before WOR-0092 left it: Clean up archives it and
        // writes the done report, file changes the reports folder's watcher reloads the list for
        File.WriteAllText(Path.Combine(app.Project.Reports!.Folder, $"20261101-0900-{done.Id}-qa.md"),
            $"---\nstatus: qa\ncard: {done.Id}\ntitle: finished long ago\ndate: 2026-11-01 09:00\nread: true\n---\n\n## Changed\n\nThe thing.\n");
        // agents left on a done card from before this change: work on the card where it is (work-on leaves Done alone)
        await app.Api("work-on", new { project = "proj", card = done.Id });
        await app.Api("work-on", new { project = "proj", card = open.Id });
        var stranded = app.Agents.ForCard(done.Id)!;
        var other = app.Agents.ForCard(open.Id)!;
        await app.Until(() => stranded.State == AgentState.Idle && other.State == AgentState.Idle, "both turns to end");
        Assert.Equal("Done", app.Read(done.Id).Status);
        await DargApp.Settle(500);
        Assert.Contains(stranded, app.Agents.Agents);

        app.Project.Reports!.CleanUpCommand.Execute(null);

        Assert.DoesNotContain(stranded, app.Agents.Agents);
        Assert.Contains(other, app.Agents.Agents); // a card that isn't done keeps its agent
        Assert.Contains("1 agent on done cards forgotten", app.Project.Reports.Status);
        // Clean up wrote the card's done report, so the folder watcher reloads the list 400 ms later: what Clean up
        // said must still be there after that, not the list's own "n reports" line (QA, WOR-0131)
        Assert.Contains("1 QA report archived, 1 done report written", app.Project.Reports.Status);
        await DargApp.Settle(1500);
        Assert.Contains("1 agent on done cards forgotten", app.Project.Reports.Status);
    }
}
