using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0141: the API's overview command, one call for "what's going on in this project?": Home's tiles
/// as JSON plus the board's column counts, unread reports and each repo's uncommitted files.</summary>
public class OverviewTests
{
    static string Report(string card, string title, bool read, bool qa = false) =>
        $"---\n{(qa ? "status: qa\n" : "")}card: {card}\ntitle: {title}\nagent: x\ncommits: abc123\nrepo: repo\ndate: 2026-11-18 10:00\nread: {(read ? "true" : "false")}\n---\n\n## Asked\n\nSomething.\n";

    [AvaloniaFact]
    public async Task Overview_returns_the_Home_tiles_column_counts_unread_reports_and_uncommitted_files()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            app.Card("still in the backlog");
            var busy = app.Card("being worked on", "In Progress");
            var idle = app.Card("in progress with nothing running", "In Progress");
            var qa = app.Card("waiting to be checked", "QA Ready");
            app.Card("finished", "Done");

            var reports = Directory.CreateDirectory(ReportStore.FolderFor("proj", app.Home)).FullName;
            File.WriteAllText(Path.Combine(reports, "20261118-1000-T-0001.md"), Report(qa.Id, "unread one", read: false, qa: true));
            File.WriteAllText(Path.Combine(reports, "20261118-0900-T-0002.md"), Report("T-0099", "already read", read: true));
            File.WriteAllText(Path.Combine(ArtifactStore.AgentFolder("proj", "a1", app.Home), "picks.html"),
                "<html><head><title>Logo picks</title></head><body></body></html>");
            File.WriteAllText(Path.Combine(app.Repo, "new.txt"), "not committed");
            File.AppendAllText(Path.Combine(app.Repo, "README.md"), "\nAn edit.\n");

            await app.Start();
            await app.Api("work-on", new { project = "proj", card = busy.Id });
            await app.Until(() => app.Agents.ForCard(busy.Id)?.Working == true, "the agent on the card at work");

            var o = JsonNode.Parse(await app.Api("overview", new { project = "proj" }))!.AsObject();

            Assert.Equal("proj", (string?)o["project"]);
            var columns = o["columns"]!.AsObject();
            Assert.Equal(1, (int)columns["Backlog"]!);
            Assert.Equal(2, (int)columns["In Progress"]!);
            Assert.Equal(1, (int)columns["QA Ready"]!);
            Assert.Equal(1, (int)columns["Done"]!);

            var cards = o["inProgress"]!.AsArray().Select(c => c!.AsObject()).ToList();
            Assert.Equal([busy.Id, idle.Id, qa.Id], cards.Select(c => (string?)c["id"]));
            var agent = app.Agents.ForCard(busy.Id)!;
            Assert.Equal(agent.Id, (string?)cards[0]["agent"]!["id"]);
            Assert.Null(cards[1]["agent"]);
            Assert.Equal("QA Ready", (string?)cards[2]["column"]);

            var agents = o["agents"]!.AsArray();
            Assert.Equal(agent.Id, (string?)Assert.Single(agents)!["id"]);
            Assert.Equal(busy.Id, (string?)agents[0]!["card"]);

            Assert.Equal("Logo picks", (string?)Assert.Single(o["artifacts"]!.AsArray())!["title"]);

            var unread = Assert.Single(o["unreadReports"]!.AsArray())!;
            Assert.Equal(qa.Id, (string?)unread["card"]);
            Assert.Equal("qa", (string?)unread["kind"]);

            var repo = Assert.Single(o["repos"]!.AsArray())!;
            Assert.Equal(2, (int)repo["uncommitted"]!);
            Assert.Equal("main", (string?)repo["branch"]);
            Assert.Equal("First", (string?)Assert.Single(repo["commits"]!.AsArray())!["subject"]);

            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("overview", new { project = "nope" }));
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }
}
