using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0164: the rail's Agents icon counts the agents at work, in a pip unlike the notification badges.</summary>
public class RunningPipTests
{
    static Border Pip(DargApp app) => app.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RunningPip");
    static string PipText(DargApp app) => Pip(app).GetVisualDescendants().OfType<TextBlock>().Single().Text ?? "";

    [AvaloniaFact]
    public async Task The_Agents_icon_counts_agents_at_work_and_hides_the_count_when_none_are()
    {
        try
        {
            using var app = DargApp.Make();
            var first = app.Card("first piece of work", "In Progress");
            var second = app.Card("second piece of work", "In Progress");
            await app.Start();
            await DargApp.Settle(200);
            Assert.False(Pip(app).IsVisible);

            Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
            await app.Api("work-on", new { project = "proj", card = first.Id });
            await app.Api("work-on", new { project = "proj", card = second.Id });

            await app.Until(() => app.Agents.RunningCount == 2, "both agents at work");
            await DargApp.Settle(100);
            Assert.True(Pip(app).IsVisible);
            Assert.Equal("2", PipText(app));
            app.Shot("two-running");

            // closing one's session stops it mid-turn: it stays listed but is no longer at work
            var closed = app.Agents.Agents.Single(a => a.CardId == second.Id);
            await app.Until(() => closed.CloseSession("the test"), "its session to start, so it can be closed");
            await app.Until(() => app.Agents.RunningCount == 1, "one agent closed");
            await DargApp.Settle(100);
            Assert.True(Pip(app).IsVisible);
            Assert.Equal("1", PipText(app));
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); } // the Sandbox default
    }
}
