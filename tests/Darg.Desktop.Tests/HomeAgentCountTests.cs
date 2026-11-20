using Avalonia.Headless.XUnit;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0165: Home's Agents heading carries the number of agents running, as the rail's Agents icon does.</summary>
public class HomeAgentCountTests
{
    [AvaloniaFact]
    public async Task Home_agents_heading_counts_the_agents_running()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var card = app.Card("being worked on", "In Progress");
            await app.Start();
            Assert.Equal("Agents", app.Project.Home.AgentsTitle);

            await app.Api("work-on", new { project = "proj", card = card.Id });
            await app.Until(() => app.Agents.ForCard(card.Id)?.Working == true, "the agent at work");
            var running = app.Agents.Agents.Count(a => a.Working);
            await app.Until(() => app.Project.Home.AgentsTitle.StartsWith($"Agents · {running} running"), "Home's heading counting it");
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", null); }
    }
}
