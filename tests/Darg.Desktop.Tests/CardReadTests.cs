using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0140: the API's cards and card commands answer board questions in one call each, instead of Fettle
/// grepping card files and joining them against the agents list.</summary>
public class CardReadTests
{
    const string HowToCheck = "\n\n## How to check\n\nOpen the board and look at the logo.\n";

    static List<string> Ids(string json) => JsonNode.Parse(json)!.AsArray().Select(c => (string)c!["id"]!).ToList();

    [AvaloniaFact]
    public async Task Which_QA_cards_have_no_agent_and_which_agents_are_on_done_cards_are_one_call_each()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var qaFree = app.Card("check the logo", "QA Ready", "The logo." + HowToCheck);
            var qaBusy = app.Card("check the header", "QA Ready", "The header." + HowToCheck);
            var doneWith = app.Card("finished with an agent on it", "To Do");
            var doneAlone = app.Card("finished alone", "Done");
            var todo = app.Card("not started", "To Do");
            await app.Start();

            await app.Api("work-on", new { project = "proj", card = qaBusy.Id });
            await app.Api("work-on", new { project = "proj", card = doneWith.Id });
            BoardStore.Move(app.BoardFolder, doneWith.Id, app.ColumnId("Done"));
            await DargApp.Settle(300);
            var onDone = app.Agents.ForCard(doneWith.Id)!;

            Assert.Equal([qaFree.Id], Ids(await app.Api("cards", new { project = "proj", column = "qa", agent = "none" })));
            Assert.Equal([qaFree.Id], Ids(await app.Api("cards", new { project = "proj", column = "QA Ready", agent = "none" })));

            var done = JsonNode.Parse(await app.Api("cards", new { project = "proj", column = "done", agent = "any" }))!.AsArray();
            var row = Assert.Single(done)!;
            Assert.Equal(doneWith.Id, (string)row["id"]!);
            Assert.Equal(onDone.Id, (string)row["agents"]![0]!["id"]!);
            Assert.Equal(onDone.Id, (string)row["agent"]!["id"]!);
            Assert.Equal("Done", (string)row["column"]!);
            Assert.Equal("Main", (string)row["board"]!);
            Assert.True(File.Exists((string)row["file"]!));

            // no filter: every card, each with its column; an unknown column says which there are
            var all = JsonNode.Parse(await app.Api("cards", new { project = "proj" }))!.AsArray();
            Assert.Equal(5, all.Count);
            Assert.Equal("To Do", (string)all.Single(c => (string)c!["id"]! == todo.Id)!["column"]!);
            Assert.Null(all.Single(c => (string)c!["id"]! == doneAlone.Id)!["agent"]);
            Assert.Equal([qaBusy.Id], Ids(await app.Api("cards", new { project = "proj", text = "header" })));
            var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("cards", new { project = "proj", column = "Nowhere" }));
            Assert.Contains("QA Ready", bad.Message);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task Card_gives_its_text_decision_QA_notes_reports_and_agents()
    {
        using var app = DargApp.Make();
        var card = app.Card("pick the logo", BoardStore.AttentionName, "Which logo?");
        await app.Start();
        var file = Path.Combine(app.BoardFolder, "items", card.File);
        QaNotes.Add(file, "the pink is too loud", new DateTime(2026, 11, 18, 9, 0, 0));
        new CardDecision("Which logo goes on the header?",
            [new DecisionOption("Pink", "the loud one", DecisionAction.Done, true), new DecisionOption("Teal", "the quiet one", DecisionAction.Back, false)],
            "both are in the docs", "agent abc1234567, 2026-11-18 09:00").WriteTo(file);
        var reports = Directory.CreateDirectory(ReportStore.FolderFor("proj")).FullName;
        File.WriteAllText(Path.Combine(reports, $"20261118-0900-{card.Id}-qa.md"),
            $"---\nstatus: qa\ncard: {card.Id}\ntitle: pick the logo\nagent: abc1234567\ncommits: 1234567\nrepo: {app.Repo}\ndate: 2026-11-18 09:00\nread: false\n---\n\n## Changed\n\nTwo logos.\n");
        File.WriteAllText(Path.Combine(reports, "20261118-0900-T-999.md"), "---\ncard: T-999\ntitle: someone else's\n---\n");
        await app.Api("work-on", new { project = "proj", card = card.Id });

        var c = JsonNode.Parse(await app.Api("card", new { project = "proj", id = card.Id.ToLowerInvariant() }))!;
        Assert.Equal(card.Id, (string)c["id"]!);
        Assert.Contains("Which logo?", (string)c["text"]!);
        Assert.Equal("Which logo goes on the header?", (string)c["decision"]!["question"]!);
        Assert.Equal("Pink", (string)c["decision"]!["options"]![0]!["label"]!);
        Assert.Equal("done", (string)c["decision"]!["options"]![0]!["action"]!);
        Assert.Contains("the pink is too loud", (string)c["qaNotes"]!);
        var report = Assert.Single(c["reports"]!.AsArray())!;
        Assert.Equal("qa", (string)report["kind"]!);
        var agent = Assert.Single(c["agents"]!.AsArray())!;
        Assert.Equal(app.Agents.Agents.Single(a => a.CardId == card.Id).Id, (string)agent["id"]!);

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("card", new { project = "proj", id = "T-404" }));
    }
}
