using Darg.Core;

namespace Darg.Core.Tests;

public class ReportStoreTests
{
    const string Report = """
        ---
        card: WOR-0061
        title: Finished agent reports
        agent: abc1234567
        commits: 1a2b3c4 5d6e7f8
        repo: D:/repo
        date: 2026-11-16 22:40
        read: false
        ---

        ## Asked

        Agents should file a report.

        ## Done

        Added the Reports section.
        """;

    [Fact]
    public void Parses_properties_and_body()
    {
        var r = ReportStore.Parse(Report, "x/20261116-2240-WOR-0061.md");
        Assert.Equal("WOR-0061", r.Card);
        Assert.Equal("Finished agent reports", r.Title);
        Assert.Equal("abc1234567", r.Agent);
        Assert.Equal(["1a2b3c4", "5d6e7f8"], r.Commits);
        Assert.Equal("D:/repo", r.Repo);
        Assert.Equal("2026-11-16 22:40", r.Date);
        Assert.False(r.Read);
        Assert.StartsWith("## Asked", r.Body);
        Assert.Equal("WOR-0061: Finished agent reports", r.Heading);
        Assert.Equal("20261116-2240-WOR-0061", r.Id);
    }

    [Theory]
    [InlineData("commits: [1a2b3c4, 5d6e7f8]")]
    [InlineData("commits:\n  - 1a2b3c4\n  - 5d6e7f8")]
    [InlineData("commits: 1a2b3c4, 5d6e7f8 (rebased)")]
    public void Commits_read_in_any_list_shape(string line)
    {
        var r = ReportStore.Parse($"---\ncard: WOR-1\n{line}\nread: true\n---\nbody", "r.md");
        Assert.Equal(["1a2b3c4", "5d6e7f8"], r.Commits);
        Assert.True(r.Read);
    }

    [Fact]
    public void No_properties_is_unread_and_titled_by_its_heading()
    {
        var r = ReportStore.Parse("# Fixed the thing\n\nIt works.", "r.md");
        Assert.False(r.Read);
        Assert.Equal("Fixed the thing", r.Heading);
    }

    [Fact]
    public void SetRead_rewrites_only_the_read_property()
    {
        var dir = Directory.CreateTempSubdirectory("darg-reports").FullName;
        try
        {
            var file = Path.Combine(dir, "a.md");
            File.WriteAllText(file, Report);
            ReportStore.SetRead(file, true);
            var r = ReportStore.Parse(File.ReadAllText(file), file);
            Assert.True(r.Read);
            Assert.Equal(["1a2b3c4", "5d6e7f8"], r.Commits);
            Assert.StartsWith("## Asked", r.Body);
            ReportStore.SetRead(file, false);
            Assert.False(ReportStore.Parse(File.ReadAllText(file), file).Read);

            // a report written without the property gets it
            var bare = Path.Combine(dir, "b.md");
            File.WriteAllText(bare, "---\ncard: WOR-2\n---\nbody");
            ReportStore.SetRead(bare, true);
            Assert.True(ReportStore.Parse(File.ReadAllText(bare), bare).Read);
            Assert.Equal("WOR-2", ReportStore.Parse(File.ReadAllText(bare), bare).Card);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void List_is_newest_first()
    {
        var dir = Directory.CreateTempSubdirectory("darg-reports").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "20261115-0900-WOR-1.md"), "---\ncard: WOR-1\ndate: 2026-11-15 09:00\n---\n");
            File.WriteAllText(Path.Combine(dir, "20261116-0900-WOR-2.md"), "---\ncard: WOR-2\ndate: 2026-11-16 09:00\n---\n");
            Assert.Equal(["WOR-2", "WOR-1"], ReportStore.List(dir).Select(r => r.Card));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Qa_column_is_found_by_name_but_never_the_done_column()
    {
        Column[] cols = [new("c1", "To Do"), new("c2", "In Progress"), new("c4", "QA Ready"), new("c3", "Done")];
        Assert.Equal("QA Ready", BoardColumns.Qa(cols));
        Assert.Equal("Review", BoardColumns.Qa([new("a", "To Do"), new("b", "Review"), new("c", "Done")]));
        Assert.Null(BoardColumns.Qa([new("a", "To Do"), new("b", "In Progress"), new("c", "Done")]));
        Assert.Null(BoardColumns.Qa([new("a", "To Do"), new("b", "QA")])); // the last column is done
        Assert.Null(BoardColumns.Qa([new("a", "Quality")]));
    }

    [Fact]
    public void Rules_name_the_report_file_and_the_qa_column_and_go_on_a_command_line()
    {
        var card = new Card("WOR-0061", "Reports", "In Progress", "", "Task", [], "x.md", "");
        var rules = ReportStore.Rules("C:\\inst\\reports", card, "QA Ready", "abc1234567");
        Assert.Contains("set its Status to QA Ready", rules);
        Assert.Contains("C:/inst/reports/", rules);
        Assert.Contains("-WOR-0061.md", rules);
        Assert.Contains("agent: abc1234567", rules);
        Assert.DoesNotContain("\"", rules);
        foreach (var c in "<>&|") Assert.DoesNotContain(c, rules);
        Assert.Contains("this board has no QA column", ReportStore.Rules("r", card, null, null));
    }
}
