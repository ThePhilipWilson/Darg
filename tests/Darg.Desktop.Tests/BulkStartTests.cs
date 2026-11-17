using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0125: ▶ Work All on the backlog / To Do / In Progress columns and 🧪 QA Run All on the QA column, pressed
/// through the columns' own commands. Turns are long here (FAKE_SECONDS) so the agents are still at work when the
/// buttons are read and pressed again.</summary>
public class BulkStartTests
{
    const string HowToCheck = "\n\n## How to check\n\nOpen the board and look at the logo.\n";

    static ListViewModel Column(DargApp app, string id) => app.Board.Lists.Single(l => l.Id == id);

    [AvaloniaFact]
    public async Task Work_All_asks_above_three_then_starts_every_free_card_once_and_moves_waiting_ones_to_In_Progress()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var backlog1 = app.Card("first in the backlog");
            var backlog2 = app.Card("second in the backlog");
            var todo = app.Card("waiting in To Do", "To Do");
            var busy = app.Card("already being worked on", "In Progress");
            var idle = app.Card("in progress with nothing running", "In Progress");
            var done = app.Card("finished", "Done");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });
            await app.Api("work-on", new { project = "proj", card = busy.Id });
            await DargApp.Settle(300);

            var inProgress = Column(app, app.ColumnId("In Progress"));
            Assert.Equal("▶ Work All (4)", inProgress.BulkText);
            Assert.Equal("▶ Work All (4)", Column(app, Board.Backlog).BulkText);
            Assert.Equal("▶ Work All (4)", Column(app, app.ColumnId("To Do")).BulkText);
            Assert.False(Column(app, app.ColumnId("Done")).HasBulk);
            Assert.False(Column(app, app.ColumnId("For Human Attention")).HasBulk);
            Assert.Equal("🧪 QA Run All (0)", Column(app, app.ColumnId("QA Ready")).BulkText);

            inProgress.BulkCommand.Execute(null);
            Assert.True(inProgress.ConfirmingBulk); // 4 is more than a few: it asks first
            Assert.Single(app.Agents.Agents);

            inProgress.ConfirmBulkCommand.Execute(null);
            await DargApp.Settle(300);
            foreach (var c in new[] { backlog1, backlog2, todo, busy, idle })
            {
                Assert.Single(app.Agents.Agents, a => a.CardId == c.Id);
                Assert.Equal("In Progress", app.Read(c.Id).Status);
            }
            Assert.DoesNotContain(app.Agents.Agents, a => a.CardId == done.Id);
            Assert.Equal("▶ Work All (0)", Column(app, app.ColumnId("In Progress")).BulkText);
            Assert.False(Column(app, app.ColumnId("In Progress")).BulkCommand.CanExecute(null));

            Assert.Contains("Nothing to start", await app.Api("work-all", new { project = "proj" }));
            Assert.Equal(5, app.Agents.Agents.Count);
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task QA_Run_All_starts_a_QA_agent_on_each_card_in_QA_and_not_twice()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var a = app.Card("check the logo", "QA Ready", "The logo." + HowToCheck);
            var b = app.Card("check the header", "QA Ready", "The header." + HowToCheck);
            var waiting = app.Card("not ready yet", "To Do");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });

            var qa = Column(app, app.ColumnId("QA Ready"));
            Assert.Equal("🧪 QA Run All (2)", qa.BulkText);
            qa.BulkCommand.Execute(null); // two: no asking
            Assert.False(qa.ConfirmingBulk);
            await DargApp.Settle(300);

            Assert.Single(app.Agents.Agents, x => x.CardId == a.Id && x.Purpose == AgentPurposes.Qa);
            Assert.Single(app.Agents.Agents, x => x.CardId == b.Id && x.Purpose == AgentPurposes.Qa);
            Assert.DoesNotContain(app.Agents.Agents, x => x.CardId == waiting.Id);
            Assert.Equal("QA Ready", app.Read(a.Id).Status); // QA leaves the card where it is
            Assert.Equal("🧪 QA Run All (0)", Column(app, app.ColumnId("QA Ready")).BulkText);

            Assert.Contains("Nothing to QA", await app.Api("qa-all", new { project = "proj" }));
            Assert.Equal(2, app.Agents.Agents.Count);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }
}
