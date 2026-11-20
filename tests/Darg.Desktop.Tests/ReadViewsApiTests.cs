using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>AND-0008: notes, docs and reports reach the phone through the Darg API, read only.</summary>
public class ReadViewsApiTests
{
    [AvaloniaFact]
    public async Task Notes_docs_and_reports_are_readable_through_the_api()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Docs, "Index.md"), "# Index\n\nSee [[PLAN]].\n");
        var notes = Path.Combine(Path.GetDirectoryName(app.Boards)!, "notes");
        NoteStore.Save(notes, new Note("20261120-0101-aaaa", "A thought", "Loose", "pink", [], "2026-11-20 01:01", ""));
        var reports = Directory.CreateDirectory(ReportStore.FolderFor("proj", app.Home)).FullName;
        File.WriteAllText(Path.Combine(reports, "20261120-0100-T-0001.md"),
            "---\ncard: T-0001\ntitle: Did it\nagent: x\ncommits: abc123\nrepo: r\ndate: 2026-11-20 01:00\nread: false\n---\n\n## Asked\n\nA.\n");
        await app.Start();

        var n = JsonNode.Parse(await app.Api("notes", new { project = "proj" }))!;
        Assert.Equal("A thought", (string?)n["notes"]![0]!["text"]);

        Assert.Contains("PLAN.md", JsonNode.Parse(await app.Api("docs", new { project = "proj" }))!.AsArray().Select(d => (string?)d));
        var d = JsonNode.Parse(await app.Api("doc", new { project = "proj", path = "Index.md" }))!;
        Assert.Equal("PLAN.md", (string?)d["links"]!["wiki:PLAN"]!["path"]);

        var list = JsonNode.Parse(await app.Api("reports", new { project = "proj" }))!.AsArray();
        Assert.Equal("20261120-0100-T-0001", (string?)list[0]!["id"]);
        var r = JsonNode.Parse(await app.Api("report", new { project = "proj", id = "20261120-0100-T-0001" }))!;
        Assert.Contains("## Asked", (string?)r["body"]);
        Assert.False((bool)r["read"]!); // reading over the API leaves it unread
    }
}
