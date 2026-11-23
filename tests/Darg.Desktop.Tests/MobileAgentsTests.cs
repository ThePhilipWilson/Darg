using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;
using Darg.Mobile;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Desktop.Tests;

/// <summary>AND-0005: the phone's Agents section against the real desktop app over HTTP: the list shows an agent at work
/// with what it's doing, its transcript opens with long text folded, and a message sent mid-turn reaches the agent.</summary>
public class MobileAgentsTests
{
    [AvaloniaFact]
    public async Task An_agent_is_watched_and_nudged_from_the_phone()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "6");
        var (listEvery, pageEvery) = (AgentsListViewModel.PollEvery, AgentPageViewModel.PollEvery);
        AgentsListViewModel.PollEvery = AgentPageViewModel.PollEvery = TimeSpan.FromMilliseconds(300);
        try
        {
            using var app = DargApp.Make();
            await app.Start();
            var prompt = "tidy the header\n" + string.Join("\n", Enumerable.Range(1, 29).Select(i => $"step {i} of the brief"));
            await app.Api("start-agent", new { project = "proj", prompt, name = "header" });
            var agent = app.Agents.Agents.Single();

            var parsed = Connection.ParseAddress(ArtifactHost.Server!.ApiUrl)!.Value;
            var store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));
            store.Save(new Connection(parsed.Address, parsed.Token!));
            var shell = new ShellViewModel(store);
            var window = Phone(shell);
            var projects = shell.OpenProjects();
            await DargApp.Wait(() => projects.Projects.Count == 1, "the projects");

            var page = new ProjectPageViewModel(projects.Projects[0], shell);
            shell.Show(page);
            page.PickCommand.Execute(page.Tabs.First(t => t.Section.Name == "Agents"));
            var list = Assert.IsType<AgentsListViewModel>(page.Content);
            await app.Until(() => list.Agents.Count == 1 && list.Agents[0].Working && list.Agents[0].Doing.Length > 0, "the list to show the agent at work");
            Assert.Equal("header", list.Agents[0].Name);
            Shot(window, app, "1-agents");
            // the row spans the phone's width (a thumb hits it anywhere), not docked to the left at its text's width
            var row = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("row"));
            Assert.True(row.Bounds.Width > 360, $"the agent's row is {row.Bounds.Width} wide in a 412 phone");
            // the list scrolls past its last row far enough that the page's ✦ Fettle button can't sit over it: the list's
            // own height (margin and all, what the scroller scrolls) runs on below the last row by the button's height.
            // (The scroller's Extent is no use here: with one row it is the whole viewport, room or no room.)
            var fab = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("fab") && b.IsEffectivelyVisible);
            var items = row.FindAncestorOfType<ItemsControl>()!;
            var rowBottom = row.TranslatePoint(new Point(0, row.Bounds.Height), items)!.Value.Y;
            var room = items.DesiredSize.Height - rowBottom;
            Assert.True(room >= fab.Bounds.Height + fab.Margin.Bottom,
                $"the list ends {room} below its last row; the Fettle button takes {fab.Bounds.Height + fab.Margin.Bottom}");

            list.OpenCommand.Execute(list.Agents[0]);
            var open = Assert.IsType<AgentPageViewModel>(shell.Page);
            Assert.Equal("header", shell.Title);
            await app.Until(() => open.Entries.Count >= 4 && open.Working, "the transcript to fill while it works");
            var asked = open.Entries[0];
            Assert.True(asked.IsUser);
            Assert.Equal(30 - TranscriptEntry.FoldLines, asked.Hidden);
            Assert.Equal(TranscriptEntry.FoldLines, asked.Text.Split('\n').Length);
            window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Scroll").ScrollToHome();
            Shot(window, app, "2-folded");
            asked.ToggleCommand.Execute(null);
            Assert.Equal(30, asked.Text.Split('\n').Length);
            Assert.Equal("Show less", asked.MoreLabel);
            var step = open.Entries.First(e => e.IsStep);
            Assert.StartsWith("✓ Read: ", step.Line);
            Assert.Contains("mid-turn", open.SendHint);
            await DargApp.Settle(200);
            Shot(window, app, "3-transcript");

            open.Compose = "use the teal logo";
            await open.SendCommand.ExecuteAsync(null);
            Assert.Equal("", open.Compose);
            Assert.False(open.Bad, open.Status);
            await app.Until(() => app.Prompts().Contains("use the teal logo"), "the nudge to reach the agent");
            Assert.Contains(open.Entries, e => e.IsUser && e.Text == "use the teal logo");

            await app.Until(() => agent.State == AgentState.Idle, "the turn to end", 60);
            await app.Until(() => !open.Working && open.Entries.Count == agent.Items.Count, "the phone to catch up with the finished turn");
            Assert.Equal(Enumerable.Range(0, open.Entries.Count), open.Entries.Select(e => e.Entry.Index));
            await DargApp.Settle(200);
            Shot(window, app, "4-done");

            Assert.True(shell.Back());
            Assert.Same(page, shell.Page);
            window.Close();
            Assert.Equal("", app.Crashes());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_SECONDS", "1");
            (AgentsListViewModel.PollEvery, AgentPageViewModel.PollEvery) = (listEvery, pageEvery);
        }
    }

    static Window Phone(ShellViewModel shell)
    {
        var window = new Window { Width = 412, Height = 860, Content = new ShellView { DataContext = shell } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static void Shot(Window window, DargApp app, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
#pragma warning disable CS0618
        frame.Save(Path.Combine(app.Folder, "phone-" + name + ".png"));
#pragma warning restore CS0618
    }
}
