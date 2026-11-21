using System.Text.RegularExpressions;
using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0174: plan files in the project's docs/design folder.</summary>
public class PlansTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "darg-plans-" + Guid.NewGuid().ToString("N")[..8], "design");

    public PlansTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, true);

    /// <summary>The worked Android plan, taken from docs/FEATURE-PLANNING.md itself, so the doc and the parser can't drift.</summary>
    static string WorkedPlan()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "docs", "FEATURE-PLANNING.md");
            if (!File.Exists(f)) continue;
            var m = Regex.Match(File.ReadAllText(f).Replace("\r\n", "\n"), @"```markdown\n(---\nkind: feature.*?)\n```", RegexOptions.Singleline);
            Assert.True(m.Success, "the worked plan block is in the doc");
            return m.Groups[1].Value + "\n";
        }
        throw new FileNotFoundException("docs/FEATURE-PLANNING.md");
    }

    string Save(string text, string name = "android.md")
    {
        var f = Path.Combine(_dir, name);
        File.WriteAllText(f, text);
        return f;
    }

    const string Q = "How does the phone reach the PC off the home network?";
    const string AnswerLine = "**Answer:** Tailscale (Philip, 2026-11-20)";

    [Fact]
    public void The_worked_Android_plan_parses_into_goals_questions_cards_and_blockers()
    {
        var p = PlanDoc.Load(Save(WorkedPlan()));
        Assert.Equal(("feature", "agreed", "Android", "darg", "Android"), (p.Kind, p.Status, p.Board, p.Parent, p.Title));
        Assert.Equal(["Answer agent questions and card decisions away from the PC."], p.Goals);
        Assert.Single(p.NotDoing);
        var q = Assert.Single(p.Questions);
        Assert.Equal(Q, q.Question);
        Assert.Equal(["Tailscale", "A relay", "Home network only"], q.Options.Select(o => o.Label));
        Assert.Equal("Tailscale", q.Options.Single(o => o.Recommended).Label);
        Assert.Equal("Tailscale (Philip, 2026-11-20)", q.Answer);
        Assert.Empty(p.OpenQuestions);
        Assert.StartsWith("A thin remote client", p.Design);

        Assert.Equal(2, p.Cards.Count);
        var remote = p.Cards[0];
        Assert.Equal(("AND-0002", "Remote access to the desktop API"), (remote.Id, remote.Title));
        Assert.Equal("TLS on its own port, pairing by QR, a token per phone.", remote.Description);
        Assert.StartsWith("`src/Darg.Core/Remote.cs`", remote.Where);
        Assert.StartsWith("a phone calls projects", remote.Done);
        Assert.Empty(remote.BlockedBy);
        var pairing = p.Cards[1];
        Assert.False(pairing.Made);
        Assert.Equal(["AND-0002", "AND-0003"], pairing.BlockedBy);
        Assert.Same(remote, p.Find("AND-0002"));
        Assert.Same(remote, p.Find("remote access to the desktop api"));
        Assert.Empty(p.UnknownBlockers());   // AND-0003 is shaped like a card id, so it may be one on the board
    }

    [Fact]
    public void Each_proposed_card_has_its_ticket_score_and_a_thin_one_is_not_ready()
    {
        var p = PlanDoc.Parse(
            "# P\n\n## Cards\n\n### Thin\nDo it.\n\n### Full\n" +
            string.Join(" ", Enumerable.Repeat("The phone app lists what waits on you and lets you answer it with one tap each.", 3)) + "\n" +
            "**Where:** `src/Darg.Mobile/Needs.cs`\n**Done when:** a question can be answered from the phone.\n**Blocked by:** none\n");
        Assert.Equal(["Thin", "Full"], p.Cards.Select(c => c.Title));
        Assert.Equal(0, p.Cards[0].Score.Stars);
        Assert.Contains(TicketQuality.Where, p.Cards[0].Score.Missing);
        Assert.Equal(3, p.Cards[1].Score.Stars);
        Assert.True(p.Cards[1].Ready);
        Assert.Equal(["Thin"], p.NotReady.Select(c => c.Title));
    }

    [Fact]
    public void It_is_forgiving_of_the_shapes_agents_drift_into()
    {
        var p = PlanDoc.Parse(
            "---\r\nkind: Feature\r\nstatus: Agreed\r\n---\r\n# P\r\n\r\n## goals\r\n- one\r\n* two\r\nthree\r\n\r\n## NOT DOING\r\n- x\r\n\r\n" +
            "## Questions\r\n\r\n### Which?\r\n1. **A** (Recommended): first\r\n2. B - second\r\n\r\nAnswer: B\r\n\r\n" +
            "## Cards\r\n\r\n### AND-0007 - Made already\r\ntext\r\n- **Where:** `a.cs`\r\n- Done when: it works\r\n- Blocked by: AND-0002 and Pairing, AND-0003.\r\n");
        Assert.Equal(("feature", "agreed"), (p.Kind, p.Status));
        Assert.Equal(["one", "two", "three"], p.Goals);
        Assert.Equal(["x"], p.NotDoing);
        Assert.Equal("B", p.Questions[0].Answer);
        Assert.Equal(["A", "B"], p.Questions[0].Options.Select(o => o.Label));
        var c = p.Cards[0];
        Assert.Equal(("AND-0007", "Made already", "`a.cs`", "it works"), (c.Id, c.Title, c.Where, c.Done));
        Assert.Equal(["AND-0002", "Pairing", "AND-0003"], c.BlockedBy);
        Assert.Equal(["Pairing"], p.UnknownBlockers());
    }

    [Fact]
    public void A_project_plan_lists_features_with_their_links()
    {
        var p = PlanDoc.Parse("---\nkind: project\n---\n# Darg\n\n## Features\n- [Android](android.md): the phone\n- Planning - the design page\n");
        Assert.True(p.IsProject);
        Assert.Equal([("Android", "the phone", "android.md"), ("Planning", "the design page", "")], p.Features.Select(f => (f.Title, f.Description, f.Link)));
    }

    [Fact]
    public void Writing_an_answer_changes_only_that_question_and_replaces_an_earlier_answer()
    {
        var unanswered = WorkedPlan().Replace("\n\n" + AnswerLine + "\n", "\n");
        Assert.DoesNotContain("Answer:", unanswered);
        var f = Save(unanswered);
        Assert.True(PlanStore.Answer(f, Q, "A relay", "Philip", new DateTime(2026, 11, 21)));
        var after = File.ReadAllText(f);
        Assert.Equal("A relay (Philip, 2026-11-21)", PlanDoc.Load(f).Questions[0].Answer);
        // exactly one line added, in the question's own section
        Assert.Equal(unanswered.Replace("\n\n## Design", "\n\n**Answer:** A relay (Philip, 2026-11-21)\n\n## Design"), after);

        Assert.True(PlanStore.Answer(f, Q, "Tailscale", "Philip", new DateTime(2026, 11, 22)));
        Assert.Equal(after.Replace("A relay (Philip, 2026-11-21)", "Tailscale (Philip, 2026-11-22)"), File.ReadAllText(f));
        Assert.False(PlanStore.Answer(f, "No such question", "x", "Philip", DateTime.Now));
    }

    [Fact]
    public void Writing_a_card_id_changes_only_its_heading_and_keeps_crlf()
    {
        var text = WorkedPlan().Replace("\n", "\r\n");
        var f = Save(text);
        Assert.True(PlanStore.SetCardId(f, "Pairing in the phone app", "AND-0010"));
        Assert.Equal(text.Replace("### Pairing in the phone app", "### AND-0010: Pairing in the phone app"), File.ReadAllText(f));
        Assert.Equal("AND-0010", PlanDoc.Load(f).Cards[1].Id);
        // again, by its new heading: the id is replaced, not stacked
        Assert.True(PlanStore.SetCardId(f, "AND-0010: Pairing in the phone app", "AND-0011"));
        Assert.Equal("AND-0011", PlanDoc.Load(f).Cards[1].Id);
        Assert.Equal("Pairing in the phone app", PlanDoc.Load(f).Cards[1].Title);
        Assert.False(PlanStore.SetCardId(f, "Nothing like it", "AND-0012"));
    }

    [Fact]
    public void Setting_a_property_changes_one_line_or_adds_the_block()
    {
        var text = WorkedPlan();
        var f = Save(text);
        Assert.True(PlanStore.SetProperty(f, "status", "dropped"));
        Assert.Equal(text.Replace("status: agreed", "status: dropped"), File.ReadAllText(f));
        var bare = Save("# P\n\n## Goals\n- a\n", "bare.md");
        PlanStore.SetProperty(bare, "board", "Android");
        Assert.Equal("---\nboard: Android\n---\n# P\n\n## Goals\n- a\n", File.ReadAllText(bare));
    }

    [Fact]
    public void Writing_a_part_replaces_only_its_body_and_adds_a_missing_one_in_its_place()
    {
        // WOR-0175: Goals, Not doing and Design are edited as text on the Design page
        var text = WorkedPlan();
        var f = Save(text);
        Assert.True(PlanStore.SetSection(f, "Goals", "- Answer agents from the sofa.\r\n- Never miss a decision.\n\n"));
        Assert.Equal(text.Replace("## Goals\n- Answer agent questions and card decisions away from the PC.\n",
            "## Goals\n- Answer agents from the sofa.\n- Never miss a decision.\n"), File.ReadAllText(f));
        Assert.Equal(["Answer agents from the sofa.", "Never miss a decision."], PlanDoc.Load(f).Goals);
        Assert.Equal(PlanDoc.Parse(text).Cards.Count, PlanDoc.Load(f).Cards.Count);

        // the last part, in a CRLF file: the final newline and the line endings stay
        var crlf = Save("# P\r\n\r\n## Goals\r\n- a\r\n\r\n## Design\r\nold\r\n", "crlf.md");
        PlanStore.SetSection(crlf, "Design", "new\nlines");
        Assert.Equal("# P\r\n\r\n## Goals\r\n- a\r\n\r\n## Design\r\nnew\r\nlines\r\n", File.ReadAllText(crlf));

        // missing: before the part that follows it, or at the end; emptied: the heading stays
        var bare = Save("# P\n\n## Goals\n- a\n\n## Design\nd\n", "bare.md");
        PlanStore.SetSection(bare, "Not doing", "- b");
        Assert.Equal("# P\n\n## Goals\n- a\n\n## Not doing\n- b\n\n## Design\nd\n", File.ReadAllText(bare));
        PlanStore.SetSection(bare, "Features", "- c");
        Assert.Equal("# P\n\n## Goals\n- a\n\n## Not doing\n- b\n\n## Design\nd\n\n## Features\n- c\n", File.ReadAllText(bare));
        PlanStore.SetSection(bare, "Goals", "  ");
        Assert.Equal("# P\n\n## Goals\n\n## Not doing\n- b\n\n## Design\nd\n\n## Features\n- c\n", File.ReadAllText(bare));
        Assert.Empty(PlanDoc.Load(bare).Goals);
    }

    [Fact]
    public void The_store_lists_a_projects_plans_and_builds_the_tree_by_parent()
    {
        Save("---\nkind: project\n---\n# Darg\n\n## Features\n- Android\n", "darg.md");
        Save("---\nparent: darg\n---\n# Android\n", "android.md");
        Save("---\nparent: Android\n---\n# Pairing\n", "pairing.md");
        Save("---\nparent: nowhere\n---\n# Lost\n", "lost.md");
        Save("---\nparent: loop-b\n---\n# A\n", "loop-a.md");
        Save("---\nparent: loop-a\n---\n# B\n", "loop-b.md");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "not a plan");
        var plans = PlanStore.List(_dir);
        Assert.Equal(6, plans.Count);
        var roots = PlanStore.Tree(plans);
        var darg = roots.Single(n => n.Plan.Title == "Darg");
        Assert.Equal(["Android"], darg.Children.Select(c => c.Plan.Title));
        Assert.Equal(["Pairing"], darg.Children[0].Children.Select(c => c.Plan.Title));
        Assert.Equal(["A", "B", "Darg", "Lost"], roots.Select(n => n.Plan.Title).Order());   // a loop shows up as roots, not nowhere
        Assert.Equal(6, Count(roots));
        Assert.Empty(PlanStore.List(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void A_question_an_agent_answered_is_marked_with_its_card_and_pick()
    {
        var doc = PlanDoc.Parse("# P\n\n## Questions\n\n### How?\n- **Tailscale**: a\n- **Relay**: b\n\n**Answer:** Tailscale (decided by agent, and-0002, 2026-11-20)\n\n### Which id?\n- **x**: a\n\n**Answer:** x (Philip, 2026-11-20)\n\n### Open?\n- **y**: a\n");
        var q = doc.Questions[0];
        Assert.True(q.ByAgent);
        Assert.Equal("AND-0002", q.DecidedCard);
        Assert.Equal("Tailscale", q.Pick);
        Assert.False(doc.Questions[1].ByAgent);
        Assert.False(doc.Questions[2].ByAgent);
        Assert.True(PlanRules.DecidedBy("Relay (decided by an agent)") is { Card: null });
    }

    [Fact]
    public void A_card_agent_is_given_its_plan_and_the_rule_for_a_choice_the_plan_left_open()
    {
        var doc = PlanDoc.Parse("# P\n\n## Cards\n\n### AND-0002: Remote\nd\n**Where:** w\n**Done when:** x\n");
        Assert.Same(doc, PlanRules.PlanOf([doc], "and-0002"));
        Assert.Null(PlanRules.PlanOf([doc], "AND-0009"));
        var lines = string.Join("\n", PlanRules.Prompt("D:/docs/design/phone.md", "AND-0002"));
        Assert.Contains("D:/docs/design/phone.md", lines);
        Assert.Contains("## Questions", lines);
        Assert.Contains("(decided by agent, AND-0002,", lines);
        Assert.Contains("same commit", lines);
    }

    static int Count(List<PlanStore.Node> nodes) => nodes.Sum(n => 1 + Count(n.Children));
}
