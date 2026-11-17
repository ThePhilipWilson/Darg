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
        Assert.Contains("## How to check", rules);
        Assert.DoesNotContain("write no report", rules);
    }

    [Fact]
    public void Reports_divide_into_finished_investigation_and_qa()
    {
        Assert.Equal(ReportKind.Finished, ReportStore.Parse(Report, "x.md").Kind);
        Assert.Equal(ReportKind.Investigation, ReportStore.Parse(Report.Replace("card: WOR-0061", "status: Investigation\ncard: WOR-0061"), "x.md").Kind);
        Assert.Equal(ReportKind.Qa, ReportStore.Parse(Report.Replace("card: WOR-0061", "status: qa\ncard: WOR-0061"), "x.md").Kind);
        var card = new Card("WOR-0091", "Split", "In Progress", "", "Task", [], "x.md", "");
        Assert.Contains("status: investigation", ReportStore.Rules("r", card, "QA", "a1"));
    }

    [Fact]
    public void A_qa_handoff_writes_a_qa_report_of_its_own_and_the_agent_stays()
    {
        var card = new Card("WOR-0088", "QA reports", "In Progress", "", "Task", [], "x.md", "");
        var rules = ReportStore.Rules("C:\\inst\\reports", card, "QA Ready", "abc1234567");
        // a file of its own beside the done report's, so one agent can write both
        Assert.Matches(@"C:/inst/reports/\d{8}-\d{4}-WOR-0088-qa\.md", rules);
        Assert.Matches(@"C:/inst/reports/\d{8}-\d{4}-WOR-0088\.md", rules);
        Assert.Contains("status: qa", rules);
        Assert.Contains("keeps this agent", rules);
        Assert.Contains("## Look at", rules);
        Assert.Contains("## Checked", rules);
        // the QA report comes after the card moves, so its commit ids are final
        Assert.True(rules.IndexOf("set its Status to QA Ready") < rules.IndexOf("status: qa"));
        // with no QA column there's still a QA report
        Assert.Contains("status: qa", ReportStore.Rules("r", card, null, null));

        var r = ReportStore.Parse(Report.Replace("card: WOR-0061", "status: qa\ncard: WOR-0061"), "x/20261117-0100-WOR-0061-qa.md");
        Assert.True(r.Qa);
        Assert.Equal("WOR-0061", r.Card);
        Assert.True(ReportStore.Parse(Report.Replace("read: false", "read: false\nstatus: \"QA\""), "x.md").Qa);
        Assert.False(ReportStore.Parse(Report, "x.md").Qa);
    }

    [Fact]
    public void Qa_notes_go_in_a_section_of_their_own_one_dated_entry_per_send_back()
    {
        var dir = Path.Combine(Path.GetTempPath(), "darg-qanotes-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "items"));
        try
        {
            var file = Path.Combine(dir, "items", "WOR-0001 - X.md");
            File.WriteAllText(file, "# WOR-0001: X\r\n\r\n- **Status:** QA Ready\r\n\r\n## Description\r\n\r\nDo it.\r\n\r\n## How to check\r\n\r\nRun it.\r\n");
            QaNotes.Add(file, "The button is grey.\n", new DateTime(2026, 11, 17, 9, 5, 0));
            QaNotes.Add(file, "Still grey.", new DateTime(2026, 11, 17, 10, 0, 0));
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("\n\n\n", text.Replace("\r\n", "\n"));
            Assert.Contains("\r\n", text); // the file's line endings are kept
            var t = text.Replace("\r\n", "\n");
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(t, "## QA notes"));
            Assert.True(t.IndexOf("## How to check") < t.IndexOf("## QA notes"));
            Assert.True(t.IndexOf("### 2026-11-17 09:05\n\nThe button is grey.") < t.IndexOf("### 2026-11-17 10:00\n\nStill grey."));
            // the board still reads the description alone
            var card = BoardStore.Parse(t, "WOR-0001 - X.md")!;
            Assert.Equal("Do it.", card.Description);

            // a section after the notes stays after them
            File.WriteAllText(file, "# WOR-0001: X\n\n## Description\n\nDo it.\n\n## QA notes\n\n### old\n\nA.\n\n## How to check\n\nRun it.\n");
            QaNotes.Add(file, "B.", new DateTime(2026, 11, 17, 11, 0, 0));
            t = File.ReadAllText(file);
            Assert.Contains("### old\n\nA.\n\n### 2026-11-17 11:00\n\nB.\n\n## How to check\n\nRun it.\n", t);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Rework_preface_carries_the_notes_and_points_at_how_to_check()
    {
        var p = ReportStore.ReworkPreface("  The button is grey.  ");
        Assert.Contains("The button is grey.", p);
        Assert.Contains("How to check", p);
        Assert.Contains("## QA notes", p);
        Assert.Contains("fresh QA report", p);
        var own = p.Replace("The button is grey.", "");
        Assert.DoesNotContain("\"", own);
        foreach (var c in "<>&|") Assert.DoesNotContain(c, own);
    }

    [Fact]
    public void Check_prompt_points_at_the_card_and_goes_on_a_command_line()
    {
        var card = new Card("WOR-0075", "QA", "QA Ready", "", "Task", [], "x.md", "");
        var p = ReportStore.CheckPrompt("Darg", card, "C:\\b\\x.md");
        Assert.Contains("How to check", p);
        Assert.Contains("WOR-0075", p);
        Assert.DoesNotContain("\"", p);
        foreach (var c in "<>&|") Assert.DoesNotContain(c, p);
    }
}
