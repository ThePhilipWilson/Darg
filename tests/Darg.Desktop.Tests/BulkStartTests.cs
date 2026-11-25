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
    public async Task Work_All_starts_every_free_card_once_and_moves_waiting_ones_to_In_Progress()
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
            Assert.Equal("▶ Work All (4)", inProgress.BulkText); // WOR-0218: In Progress's takes the backlog's too
            Assert.Equal("▶ Work All (2)", Column(app, app.ColumnId("To Do")).BulkText);
            Assert.Equal("✦ Improve All (2)", Column(app, Board.Backlog).BulkText); // WOR-0215: the backlog improves, it doesn't work
            Assert.False(Column(app, app.ColumnId("Done")).HasBulk);
            Assert.False(Column(app, app.ColumnId("For Human Attention")).HasBulk);
            Assert.Equal("🧪 QA Run All (0)", Column(app, app.ColumnId("QA Ready")).BulkText);

            inProgress.BulkCommand.Execute(null);
            Assert.True(inProgress.ConfirmingBulk); // 4 is more than a few: it asks first
            Assert.Single(app.Agents.Agents); // busy's, nothing more yet
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

    /// <summary>WOR-0218: with nothing in In Progress, its ▶ Work All counted 0 however many backlog cards could start
    /// (WOR-0215 had taken the backlog out of Work All's lists). It counts the workable backlog cards now, leaves out one
    /// blocked or with a live agent, and starts and moves the rest; the To Do's button and the backlog's Improve All keep
    /// their own counts.</summary>
    [AvaloniaFact]
    public async Task In_Progress_Work_All_counts_and_starts_the_workable_backlog_cards()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var a = app.Card("first to work");
            var b = app.Card("second to work");
            var c = app.Card("third to work");
            var blocker = app.Card("still open", "needs more work");
            var blocked = app.Card("waits on the open one");
            var improving = app.Card("being improved");
            var todo = app.Card("waiting in To Do", "To Do");
            var file = Path.Combine(app.BoardFolder, "items", blocked.File);
            var text = File.ReadAllText(file).Replace("\r\n", "\n");
            var end = text.IndexOf('\n', text.IndexOf("- **Labels:**", StringComparison.Ordinal));
            File.WriteAllText(file, text.Insert(end, $"\n- **Blocked by:** {blocker.Id}"));
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });
            await app.Api("improve-card", new { project = "proj", card = improving.Id });
            await DargApp.Settle(300);

            // the To Do's button keeps to the To Do (and In Progress): the backlog isn't its
            var toDo = Column(app, app.ColumnId("To Do"));
            Assert.Equal("▶ Work All (1)", toDo.BulkText);
            Assert.Equal("✦ Improve All (4)", Column(app, Board.Backlog).BulkText); // a, b, c, blocked: improving has its agent
            toDo.BulkCommand.Execute(null);
            await DargApp.Settle(300);
            Assert.Equal("In Progress", app.Read(todo.Id).Status);
            Assert.All(new[] { a, b, c }, x => Assert.DoesNotContain(app.Agents.Agents, g => g.CardId == x.Id));

            var inProgress = Column(app, app.ColumnId("In Progress"));
            Assert.Equal("▶ Work All (3, 1 blocked)", inProgress.BulkText);
            inProgress.BulkCommand.Execute(null);
            Assert.False(inProgress.ConfirmingBulk); // three: no asking
            await DargApp.Settle(300);

            foreach (var x in new[] { a, b, c })
            {
                Assert.Single(app.Agents.Agents, g => g.CardId == x.Id);
                Assert.Equal("In Progress", app.Read(x.Id).Status);
            }
            Assert.DoesNotContain(app.Agents.Agents, g => g.CardId == blocked.Id);
            Assert.Equal("Backlog", app.Read(blocked.Id).Status);
            Assert.Single(app.Agents.Agents, g => g.CardId == improving.Id); // its improve agent, no second one
            Assert.Equal("Backlog", app.Read(improving.Id).Status);
            Assert.Equal("▶ Work All (0, 1 blocked)", Column(app, app.ColumnId("In Progress")).BulkText);
            Assert.Equal("✦ Improve All (1)", Column(app, Board.Backlog).BulkText);

            // the API's work-all is the same button
            Assert.Contains($"Nothing to start: left out 1 blocked: {blocked.Id}", await app.Api("work-all", new { project = "proj" }));
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    /// <summary>WOR-0218: the API's work-all, with no button pressed, starts the backlog's workable cards as In Progress's does.</summary>
    [AvaloniaFact]
    public async Task Work_all_through_the_API_starts_the_backlog_too()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var a = app.Card("first to work");
            var b = app.Card("second to work");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });
            Assert.Contains("Started work on 2 cards", await app.Api("work-all", new { project = "proj" }));
            await DargApp.Settle(300);
            foreach (var x in new[] { a, b })
            {
                Assert.Single(app.Agents.Agents, g => g.CardId == x.Id);
                Assert.Equal("In Progress", app.Read(x.Id).Status);
            }
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task Improve_All_on_the_backlog_starts_an_improve_agent_on_each_thin_card_and_moves_nothing()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var a = app.Card("thin one");
            var b = app.Card("thin two");
            var c = app.Card("thin three");
            var d = app.Card("thin four");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });

            var backlog = Column(app, Board.Backlog);
            Assert.Equal("✦ Improve All (4)", backlog.BulkText);
            backlog.BulkCommand.Execute(null);
            Assert.True(backlog.ConfirmingBulk); // 4 is more than a few: it asks first
            Assert.Empty(app.Agents.Agents);

            backlog.ConfirmBulkCommand.Execute(null);
            await DargApp.Settle(300);
            foreach (var x in new[] { a, b, c, d })
                Assert.Single(app.Agents.Agents, g => g.CardId == x.Id);
            Assert.Equal("✦ Improve All (0)", Column(app, Board.Backlog).BulkText);
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
