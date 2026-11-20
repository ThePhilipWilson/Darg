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

    static int Count(List<PlanStore.Node> nodes) => nodes.Sum(n => 1 + Count(n.Children));
}
