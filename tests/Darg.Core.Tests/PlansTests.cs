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

    static string ThreeCards => PlanSource.ReplaceLineEndings("\n");

    const string PlanSource = """
        ---
        kind: feature
        status: draft
        ---
        # Android app

        ## Cards

        ### Pairing in the app
        Scan the code and keep the token.
        **Where:** `src/Pair.cs`
        **Done when:** a paired phone keeps its token across restarts.
        **Blocked by:** Remote access

        ### Remote access
        TLS on its own port and a token per phone.
        **Where:** `src/Remote.cs`
        **Done when:** a paired phone calls the API and one without a token is refused.

        ### Notifications
        Push what waits on me to the phone: a question from an agent, a card needing a decision, or a turn that ran long, each as a notification with its options as buttons.
        **Where:** `src/Push.cs`
        **Done when:** a decision shows on the phone within a minute.
        **Blocked by:** none
        """;

    string Boards(string name = "boards")
    {
        var boards = Path.Combine(Path.GetDirectoryName(_dir)!, name);
        BoardStore.CreateBoard(boards, "Main", ["Ideas", "Doing", "Shipped"], "WOR");
        return boards;
    }

    [Fact]
    public void Making_cards_builds_the_story_board_in_blocked_by_order_and_writes_the_ids_back()
    {
        // WOR-0177
        var f = Save(ThreeCards.ReplaceLineEndings("\r\n"));
        var boards = Boards();
        var r = PlanStore.MakeCards(f, boards);
        Assert.True(r.Ok, r.Problem);
        Assert.True(r.CreatedBoard);
        Assert.Equal("Android app", r.Board);
        // Remote access is made before Pairing, which waits on it, though Pairing comes first in the file
        Assert.Equal(["Remote access", "Pairing in the app", "Notifications"], r.Made.Select(m => m.Title));
        var folder = Path.Combine(boards, "Android app");
        var board = BoardStore.Load(folder);
        Assert.Equal(["Ideas", "Doing", "Shipped"], board.Columns.Select(c => c.Name));   // the project's columns
        var cards = BoardStore.ReadCards(folder).ToDictionary(c => c.Title);
        Assert.All(cards.Values, c => Assert.StartsWith("AND-", c.Id));
        Assert.Equal([cards["Remote access"].Id], cards["Pairing in the app"].BlockedBy);
        Assert.Empty(cards["Remote access"].BlockedBy);
        Assert.Empty(cards["Notifications"].BlockedBy);
        Assert.All(cards.Values, c => Assert.True(TicketQuality.Score(c).Green, c.Title));

        // the plan holds the ids, the board and the agreed state; its CRLF endings stay
        var text = File.ReadAllText(f);
        Assert.Contains($"### {cards["Pairing in the app"].Id}: Pairing in the app", text);
        Assert.Contains("board: Android app", text);
        Assert.Contains("status: agreed", text);
        Assert.DoesNotContain('\n', text.Replace("\r\n", ""));
        Assert.All(PlanDoc.Load(f).Cards, c => Assert.True(c.Made));

        // pressing again makes nothing, and nothing is deleted
        var again = PlanStore.MakeCards(f, boards);
        Assert.False(again.Ok);
        Assert.Empty(again.Made);
        Assert.Equal(3, BoardStore.ReadCards(folder).Count);
    }

    [Fact]
    public void Making_again_makes_only_the_rows_with_no_id_and_names_made_blockers_by_id()
    {
        var f = Save(ThreeCards);
        var boards = Boards();
        var folder = Path.Combine(boards, "Android app");
        Assert.True(PlanStore.MakeCards(f, boards, ["Remote access"]).Ok);   // only the ticked one
        Assert.Equal(["Remote access"], BoardStore.ReadCards(folder).Select(c => c.Title));
        // Pairing alone is fine now that its blocker is made; the rest is left for later
        var r = PlanStore.MakeCards(f, boards, ["Pairing in the app"]);
        Assert.True(r.Ok, r.Problem);
        var cards = BoardStore.ReadCards(folder).ToDictionary(c => c.Title);
        Assert.Equal([cards["Remote access"].Id], cards["Pairing in the app"].BlockedBy);
        Assert.Single(PlanDoc.Load(f).Cards, c => !c.Made);
        Assert.Single(PlanStore.MakeCards(f, boards).Made);
        Assert.Equal(3, BoardStore.ReadCards(folder).Count);
    }

    [Fact]
    public void A_card_under_three_stars_stops_everything_and_its_missing_parts_are_named()
    {
        var f = Save(ThreeCards.Replace("**Done when:** a decision shows on the phone within a minute.\n", ""));
        var boards = Boards();
        var before = File.ReadAllText(f);
        var r = PlanStore.MakeCards(f, boards);
        Assert.False(r.Ok);
        Assert.Contains("\"Notifications\" is 2/3", r.Problem);
        Assert.Contains("when it's done", r.Problem);
        Assert.Empty(r.Made);
        Assert.False(Directory.Exists(Path.Combine(boards, "Android app")));   // not even the board
        Assert.Equal(before, File.ReadAllText(f));
    }

    [Fact]
    public void Open_questions_need_a_second_press_and_a_loop_or_unknown_blocker_is_refused()
    {
        var open = ThreeCards.Replace("## Cards", "## Questions\n\n### Which port?\n- **A** (recommended): one\n- **B**: two\n\n## Cards");
        var f = Save(open);
        var boards = Boards();
        var first = PlanStore.MakeCards(f, boards);
        Assert.True(first.NeedsConfirm);
        Assert.Equal(["Which port?"], first.OpenQuestions);
        Assert.Empty(BoardStore.List(boards).Where(b => b != "Main"));
        Assert.True(PlanStore.MakeCards(f, boards, confirmOpen: true).Ok);

        var loop = Save(ThreeCards.Replace("**Where:** `src/Remote.cs`", "**Blocked by:** Pairing in the app\n**Where:** `src/Remote.cs`"), "loop.md");
        Assert.Contains("loop", PlanStore.MakeCards(loop, Boards("boards2")).Problem);
        var typo = Save(ThreeCards.Replace("**Blocked by:** none", "**Blocked by:** Nothing like it"), "typo.md");
        Assert.Contains("no card in the plan", PlanStore.MakeCards(typo, Boards("boards3")).Problem);
    }

    /// <summary>WOR-0179: a feature's title is linked to the file named for its plan, however the line is written, and
    /// the line reads back with the link and the rest unchanged.</summary>
    [Fact]
    public void A_features_title_is_linked_to_its_plan_whatever_shape_its_line_has()
    {
        var f = Save("---\nkind: project\n---\n# Darg\n\nIntro - with a dash.\n\n## Goals\n- Android: not a feature\n\n## Features\n" +
                     "- **Android**: the phone, see [ANDROID.md](../ANDROID.md)\n" +
                     "- Planning - the design page\n" +
                     "* [ ] Themes\n" +
                     "- [Search](old.md): find cards\n" +
                     "\n## Design\n- Android: still not a feature\n", "darg.md");
        Assert.True(PlanStore.SetFeatureLink(f, "android", "android.md"));
        Assert.True(PlanStore.SetFeatureLink(f, "Planning", "planning.md"));
        Assert.True(PlanStore.SetFeatureLink(f, "Themes", "themes.md"));
        Assert.True(PlanStore.SetFeatureLink(f, "Search", "search.md"));
        Assert.False(PlanStore.SetFeatureLink(f, "Nothing", "x.md"));
        var text = File.ReadAllText(f);
        Assert.Contains("- **[Android](android.md)**: the phone, see [ANDROID.md](../ANDROID.md)\n", text);
        Assert.Contains("- [Planning](planning.md) - the design page\n", text);
        Assert.Contains("* [ ] [Themes](themes.md)\n", text);
        Assert.Contains("- [Search](search.md): find cards\n", text);
        Assert.Contains("## Goals\n- Android: not a feature\n", text);
        Assert.Contains("## Design\n- Android: still not a feature\n", text);
        var plan = PlanDoc.Load(f);
        Assert.Equal(["Android", "Planning", "Themes", "Search"], plan.Features.Select(x => x.Title));
        Assert.Equal(["android", "planning", "themes", "search"], plan.Features.Select(x => x.LinkSlug));

        // features written as ### headings instead
        var headed = Save("# D\n\n## Features\n\n### Voice\nTalk to Fettle.\n\n### **Search**\nFind.\n", "headed.md");
        Assert.True(PlanStore.SetFeatureLink(headed, "Voice", "voice.md"));
        Assert.True(PlanStore.SetFeatureLink(headed, "Search", "search.md"));
        Assert.Equal("# D\n\n## Features\n\n### [Voice](voice.md)\nTalk to Fettle.\n\n### **[Search](search.md)**\nFind.\n", File.ReadAllText(headed));
        Assert.Equal([("Voice", "voice"), ("Search", "search")], PlanDoc.Load(headed).Features.Select(x => (x.Title, x.LinkSlug)));
        Assert.Equal("the phone, see [ANDROID.md](../ANDROID.md)", plan.Features[0].Description);
        Assert.Equal("Android: the phone, see [ANDROID.md](../ANDROID.md)", plan.Features[0].Idea);
        // before it was linked, the doc in its line wasn't taken for its plan
        Assert.Equal("", PlanDoc.Parse("## Features\n- **Android**: see [ANDROID.md](../ANDROID.md)\n").Features[0].Link);
        Assert.Equal("Themes", plan.Features[2].Idea);
    }

    /// <summary>WOR-0179: a feature's plan is the one its link names, else one under the project with its title; the
    /// tree puts a linked plan with no parent under its project; only features with neither, and no planner drafting
    /// their linked file, are left to plan.</summary>
    [Fact]
    public void A_feature_finds_its_plan_by_link_or_parent_and_the_rest_are_left_to_plan()
    {
        Save("---\nkind: project\n---\n# Darg\n\n## Features\n- [Android](android.md): the phone\n- Themes: colours\n- [Search](search.md): find\n- [Voice](voice.md): talk\n- Notes\n", "darg.md");
        Save("---\nkind: feature\n---\n# The phone app\n", "android.md");      // linked, no parent, its own title
        Save("---\nkind: feature\nparent: Darg\n---\n# Themes\n", "themes-plan.md");   // not linked: under the project with the title
        Save("---\nkind: feature\nparent: elsewhere\n---\n# Notes\n", "notes.md");    // a Notes plan, but under another project
        var plans = PlanStore.List(_dir);
        var darg = plans.Single(p => p.Slug == "darg");
        Assert.Equal("android", PlanStore.FeaturePlan(darg, darg.Features[0], plans)?.Slug);
        Assert.Equal("themes-plan", PlanStore.FeaturePlan(darg, darg.Features[1], plans)?.Slug);
        Assert.Null(PlanStore.FeaturePlan(darg, darg.Features[2], plans));
        Assert.Null(PlanStore.FeaturePlan(darg, darg.Features[4], plans));

        // search.md is being drafted; voice.md was linked but never written and nobody is at it
        var todo = PlanStore.FeaturesToPlan(darg, plans, slug => slug == "search");
        Assert.Equal(["Voice", "Notes"], todo.Select(f => f.Title));

        var root = PlanStore.Tree(plans).Single(n => n.Plan.Slug == "darg");
        Assert.Equal(["android", "themes-plan"], root.Children.Select(c => c.Plan.Slug));
    }

    [Fact]
    public void A_prefix_comes_from_the_plans_name()
    {
        Assert.Equal("AND", PlanStore.PrefixFor("Android app"));
        Assert.Equal("PH", PlanStore.PrefixFor("Ph"));
        Assert.Equal("T", PlanStore.PrefixFor("42"));
    }

    static int Count(List<PlanStore.Node> nodes) => nodes.Sum(n => 1 + Count(n.Children));
}
