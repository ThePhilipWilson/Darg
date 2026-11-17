using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0092: a card landing in Needs Attention gets a decision report.</summary>
public class DecisionReportTests
{
    static readonly Card C = new("WOR-0200", "Do the thing", "Needs Phil Attention", "", "Task", [], "WOR-0200 - Do the thing.md", "Make it do the thing.");
    const string CardText = "# WOR-0200: Do the thing\n\n- **Status:** Needs Phil Attention\n\n## Description\n\nMake it do the thing.\n\n## How to check\n\nRun it and look.\n";

    [Fact]
    public void The_column_is_found_by_its_name_and_never_the_last()
    {
        Assert.Equal("Needs Phil Attention", AttentionColumn.Find([new("a", "In Progress"), new("b", "Needs Phil Attention"), new("c", "Done")]));
        Assert.Equal("Attention", AttentionColumn.Find([new("a", "Attention"), new("c", "Done")]));
        Assert.Null(AttentionColumn.Find([new("a", "In Progress"), new("c", "Needs Attention")]));
        Assert.Null(AttentionColumn.Find([new("a", "In Progress"), new("c", "Done")]));
    }

    [Fact]
    public void A_report_ends_in_a_decision_section_and_is_an_unread_pending_qa_report()
    {
        var text = DecisionReport.Build(C, CardText, ["abc1234"], "D:/repo", null, new DateTime(2026, 11, 17, 11, 0, 0));
        var r = ReportStore.Parse(text, "x/20261117-1100-WOR-0200-qa.md");
        Assert.True(r.Qa);
        Assert.False(r.Read);
        Assert.Equal("pending", r.Decision);
        Assert.Equal("WOR-0200", r.Card);
        Assert.Equal(["abc1234"], r.Commits);
        Assert.Contains("Make it do the thing.", DecisionReport.Section(text, "Asked"));
        Assert.Contains("Run it and look.", DecisionReport.Section(text, "Look at"));
        Assert.Contains("unchecked", DecisionReport.Section(text, "Checked"));
        var decision = text.IndexOf("## Decision", StringComparison.Ordinal);
        Assert.True(decision > text.IndexOf("## Checked", StringComparison.Ordinal));
        Assert.DoesNotContain("\n## ", text[(decision + 5)..]); // Decision is the last section
        Assert.Contains("- [ ] Approve", text);
        Assert.Contains("- [ ] Send back", text);
    }

    [Fact]
    public void An_agents_report_supplies_what_changed_and_what_was_checked()
    {
        var agent = new AgentReport("a.md", "WOR-0200", "t", "ag1", ["fff0001"], "D:/repo", "2026-11-17 10:00", true,
            "## Changed\n\nEdited X.\n\n## Look at\n\nPress Y.\n\n## Checked\n\nTests pass.", Qa: true);
        var text = DecisionReport.Build(C, CardText, [], "", agent, DateTime.Now);
        Assert.Contains("Edited X.", DecisionReport.Section(text, "Changed"));
        Assert.Contains("Press Y.", DecisionReport.Section(text, "Look at"));
        Assert.Contains("Tests pass.", DecisionReport.Section(text, "Checked"));
        Assert.Equal(["fff0001"], ReportStore.Parse(text, "x.md").Commits);
        Assert.Equal("ag1", ReportStore.Parse(text, "x.md").Agent);
    }

    [Fact]
    public void Write_makes_the_file_once_and_settling_ticks_the_box_and_keeps_the_notes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "darg-decision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var card = Path.Combine(dir, "card.md");
            File.WriteAllText(card, CardText);
            var reports = Path.Combine(dir, "reports");
            var now = new DateTime(2026, 11, 17, 11, 0, 0);
            var file = DecisionReport.Write(reports, C, card, "", now, TimeSpan.FromMinutes(2));
            Assert.NotNull(file);
            Assert.EndsWith("20261117-1100-WOR-0200-qa.md", file);
            Assert.Single(ReportStore.List(reports));
            // landing again straight away makes no second one
            Assert.Null(DecisionReport.Write(reports, C, card, "", now.AddSeconds(30), TimeSpan.FromMinutes(2)));
            Assert.Single(ReportStore.List(reports));

            DecisionReport.Settle(file!, DecisionReport.SentBack, "Wrong colour");
            var r = ReportStore.List(reports).Single();
            Assert.Equal("sent back", r.Decision);
            Assert.Contains("- [x] Send back", r.Body);
            Assert.Contains("- [ ] Approve", r.Body);
            Assert.Contains("Notes: Wrong colour", r.Body);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Rules_tell_an_agent_the_report_for_that_column_is_darg_s()
    {
        var rules = ReportStore.Rules("r", C, "QA Ready", null, "Needs Phil Attention");
        Assert.Contains("move the card to Needs Phil Attention, don't write its decision report", rules);
        Assert.DoesNotContain("decision report", ReportStore.Rules("r", C, "QA Ready", null));
        foreach (var c in "<>&|\"") Assert.DoesNotContain(c, rules);
    }
}
