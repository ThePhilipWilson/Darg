using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>AND-0008: the phone's read-only notes, docs and reports, as JSON from Core.</summary>
public sealed class ReadViewsTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("wb-readviews-").FullName;
    string Docs => Path.Combine(_root, "docs");

    public ReadViewsTests()
    {
        Directory.CreateDirectory(Path.Combine(Docs, "sub"));
        Directory.CreateDirectory(Path.Combine(Docs, ".hidden"));
        File.WriteAllText(Path.Combine(Docs, "Index.md"),
            "# Home\n\nSee [[Plan]], [[Plan|the plan]], [setup](sub/Setup.md), [web](https://example.com), [[Nope]] and `[[InCode]]`.\n\n[up](#top)\n");
        File.WriteAllText(Path.Combine(Docs, "Plan.md"), "No heading here.\n");
        File.WriteAllText(Path.Combine(Docs, "sub", "Setup.md"), "# Setup\n\n[back](../Index.md)\n");
        File.WriteAllText(Path.Combine(Docs, ".hidden", "Secret.md"), "# Secret\n");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Doc_list_is_relative_sorted_and_skips_dot_folders()
        => Assert.Equal(["Index.md", "Plan.md", "sub/Setup.md"], ReadViews.DocList(Docs));

    [Fact]
    public void Doc_returns_text_title_and_every_link_resolved()
    {
        var d = ReadViews.Doc(Docs, "Index");
        Assert.Equal("Index.md", (string?)d["path"]);
        Assert.Equal("Home", (string?)d["title"]);
        var links = d["links"]!.AsObject();
        Assert.Equal("Plan.md", (string?)links["wiki:Plan"]!["path"]);
        Assert.Equal("doc", (string?)links["wiki:Plan"]!["kind"]);
        Assert.Equal("sub/Setup.md", (string?)links["sub/Setup.md"]!["path"]);
        Assert.Equal("external", (string?)links["https://example.com"]!["kind"]);
        Assert.Equal("missing", (string?)links["wiki:Nope"]!["kind"]);
        Assert.False(links.ContainsKey("wiki:InCode"));
        Assert.False(links.ContainsKey("#top"));
        Assert.Equal(4, links.Count);
    }

    [Fact]
    public void Doc_without_a_heading_is_titled_by_its_file_and_links_resolve_from_its_folder()
    {
        Assert.Equal("Plan", (string?)ReadViews.Doc(Docs, "Plan.md")["title"]);
        Assert.Equal("Index.md", (string?)ReadViews.Doc(Docs, "sub/Setup.md")["links"]!["../Index.md"]!["path"]);
    }

    [Theory]
    [InlineData("../Secret.md")]
    [InlineData(".hidden/Secret.md")]
    [InlineData("missing")]
    public void Doc_refuses_anything_that_is_not_a_listed_doc(string path)
        => Assert.Throws<ApiError>(() => ReadViews.Doc(Docs, path));

    [Fact]
    public void Notes_list_columns_and_every_note()
    {
        var notes = Path.Combine(_root, "notes");
        NoteStore.Save(notes, new Note("20261120-0101-aaaa", "Buy milk\nand eggs", "Loose", "yellow", ["home"], "2026-11-20 01:01", ""));
        var o = ReadViews.NotesList(notes);
        Assert.Contains("Loose", o["columns"]!.AsArray().Select(c => (string?)c));
        var n = o["notes"]!.AsArray().Single()!.AsObject();
        Assert.Equal("Buy milk\nand eggs", (string?)n["text"]);
        Assert.Equal("home", (string?)n["tags"]![0]);
        Assert.Empty(ReadViews.NotesList(null)["notes"]!.AsArray());
    }

    [Fact]
    public void Reports_list_newest_first_and_read_one_by_id_only_from_the_folder()
    {
        var reports = Directory.CreateDirectory(Path.Combine(_root, "reports")).FullName;
        string R(string card, bool read) => $"---\ncard: {card}\ntitle: T {card}\nagent: x\ncommits: abc123\nrepo: r\ndate: 2026-11-20 01:00\nread: {read.ToString().ToLowerInvariant()}\n---\n\n## Asked\n\nA.\n";
        File.WriteAllText(Path.Combine(reports, "20261120-0100-X-1.md"), R("X-1", true));
        File.WriteAllText(Path.Combine(reports, "20261120-0200-X-2.md"), R("X-2", false));
        Assert.Equal(["20261120-0200-X-2", "20261120-0100-X-1"], ReadViews.ReportList(reports).Select(r => (string?)r!["id"]));
        Assert.Equal(["20261120-0200-X-2"], ReadViews.ReportList(reports, unreadOnly: true).Select(r => (string?)r!["id"]));
        var one = ReadViews.Report(reports, "20261120-0200-X-2");
        Assert.Equal("X-2: T X-2", (string?)one["title"]);
        Assert.Contains("## Asked", (string?)one["body"]);
        Assert.Throws<ApiError>(() => ReadViews.Report(reports, "../docs/Plan"));
    }
}
