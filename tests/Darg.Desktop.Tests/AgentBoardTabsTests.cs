using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0150: the Agents list has the board tabs too; a plain click shows the agents on one board's cards,
/// Ctrl+click combines several.</summary>
public class AgentBoardTabsTests
{
    static string Listed(DargApp app) => string.Join(",", app.Agents.Listed.Select(a => a.CardId).Order());
    static string Lit(DargApp app) => string.Join(",", app.Agents.Tabs.Where(t => t.IsShown).Select(t => t.Name));
    static void Click(DargApp app, string name, bool ctrl = false)
    {
        app.Agents.TabCtrl = ctrl;
        app.Agents.Tabs.Single(t => t.Name == name).PickCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task Tabs_filter_the_agents_by_their_cards_board_plain_click_one_ctrl_click_several()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
            BoardStore.CreateBoard(app.Boards, "Site", DargApp.Columns, "S");
            var main = app.Card("on main", "In Progress");
            var android = BoardStore.Create(Path.Combine(app.Boards, "Android"), "on android", "");
            var site = BoardStore.Create(Path.Combine(app.Boards, "Site"), "on site", "");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Agents" });
            foreach (var c in new[] { main.Id, android.Id, site.Id }) await app.Api("work-on", new { project = "proj", card = c });
            await DargApp.Settle(300);
            app.Agents.NewPrompt = "a chat with no card";
            app.Agents.StartAgentCommand.Execute(null);
            await DargApp.Settle(300);
            Assert.Equal(4, app.Agents.Listed.Count);

            Assert.Equal(["All", "Android", "Main", "Site"], app.Agents.Tabs.Select(t => t.Name));
            Assert.Equal("All", Lit(app));

            Click(app, "Android");
            Assert.Equal(android.Id, Listed(app));
            Assert.Equal("Android", Lit(app));

            Click(app, "Site", ctrl: true);
            Assert.Equal(string.Join(",", new[] { android.Id, site.Id }.Order()), Listed(app));
            Assert.Equal("Android,Site", Lit(app));

            Click(app, "Android", ctrl: true); // takes it off
            Assert.Equal(site.Id, Listed(app));

            Click(app, "Main"); // plain: only that board
            Assert.Equal(main.Id, Listed(app));

            Click(app, "Main", ctrl: true); // the last one off: all again
            Assert.Equal(4, app.Agents.Listed.Count);
            Assert.Equal("All", Lit(app));

            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); } // the Sandbox default: unset, fake claude turns run 60 s
    }

    [AvaloniaFact]
    public async Task The_tabs_are_drawn_above_the_agents_list()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            BoardStore.CreateBoard(app.Boards, "Android", DargApp.Columns, "A");
            var main = app.Card("on main", "In Progress");
            var android = BoardStore.Create(Path.Combine(app.Boards, "Android"), "on android", "");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Agents" });
            await app.Api("work-on", new { project = "proj", card = main.Id });
            await app.Api("work-on", new { project = "proj", card = android.Id });
            await DargApp.Settle(300);
            var tabs = app.Window.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is AgentBoardTab).ToList();
            Assert.Equal(3, tabs.Count);
            app.Shot("agent-board-tabs");
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); } // the Sandbox default: unset, fake claude turns run 60 s
    }
}
