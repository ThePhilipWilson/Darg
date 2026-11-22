using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0176: the planner's prompt, the file it's given, and holding it to one part when asked to change one.</summary>
public class PlannerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "darg-planner-" + Guid.NewGuid().ToString("N")[..8], "design");

    public PlannerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, true);

    [Fact]
    public void A_plan_is_named_for_the_words_before_a_colon_and_numbered_past_any_taken()
    {
        Assert.Equal(Path.Combine(_dir, "android-app.md"), Planner.FileFor(_dir, "Android app: answer agents from the phone"));
        Assert.Equal(Path.Combine(_dir, "answer-agents-from-the-sofa-on.md"), Planner.FileFor(_dir, "Answer agents from the sofa, on my phone please"));
        Assert.Equal(Path.Combine(_dir, "plan.md"), Planner.FileFor(_dir, "!!!"));
        File.WriteAllText(Path.Combine(_dir, "android-app.md"), "# Android");
        Assert.Equal(Path.Combine(_dir, "android-app-2.md"), Planner.FileFor(_dir, "Android app: again"));
        // a planner still drafting android-app-2.md holds the name though the file isn't there yet
        Assert.Equal(Path.Combine(_dir, "android-app-3.md"), Planner.FileFor(_dir, "Android app", [Path.Combine(_dir, "android-app-2.md")]));
    }

    [Fact]
    public void The_prompt_drafts_every_part_without_an_interview_into_the_named_file()
    {
        var file = Path.Combine(_dir, "phone.md");
        var p = Planner.Prompt("Proj", "Phone app: answer agents from the sofa", PlanKind.Feature, file, "D:/docs", "D:/tasks",
            [Path.Combine(_dir, "proj.md")], "In D:/docs, commit your work without asking, but never push.");
        Assert.Contains("> Phone app: answer agents from the sofa", p);
        Assert.Contains("Plan file: " + file, p);
        Assert.Contains("Don't interview me", p);
        Assert.Contains("Every call you aren't sure of goes into the plan's Questions", p);
        Assert.Contains("D:/docs", p);
        Assert.Contains("D:/tasks", p);
        Assert.Contains(Path.Combine(_dir, "proj.md"), p);
        Assert.Contains("3 of 3", p);
        Assert.Contains("**Blocked by:**", p);
        Assert.Contains("Look for gaps", p);
        Assert.Contains("commit your work without asking, but never push. Commit only the plan file", p);
        Assert.Contains("change only that part's lines", p);
        Assert.Contains("## Cards", p);
        Assert.DoesNotContain("## Features", p);
        Assert.DoesNotContain("parent:", p);

        var project = Planner.Prompt("Proj", "Proj itself", PlanKind.Project, file, null, null, [], null, parent: "darg");
        Assert.Contains("## Features", project);
        Assert.DoesNotContain("## Cards", project);
        Assert.Contains("parent: darg", project);
        Assert.Contains("Don't commit the plan.", project);
        Assert.Contains("Darg starts a planner on each and links its plan here", project);
        Assert.DoesNotContain("one of the Features of the project plan", project);

        // WOR-0179: a feature of a project plan reads that plan first and keeps to its frame
        var feature = Planner.Prompt("Proj", "Search: find cards", PlanKind.Feature, file, null, null, [], null, parent: "darg");
        Assert.Contains("parent: darg", feature);
        Assert.Contains("one of the Features of the project plan " + Path.Combine(Path.GetDirectoryName(file)!, "darg.md") + ": read it first", feature);
    }

    /// <summary>The shape the prompt shows is what the Design page reads: a question with a recommended option, and a
    /// card that scores 3 of 3, so a planner copying it can't drift from the parser.</summary>
    [Fact]
    public void The_shape_in_the_prompt_parses_as_a_plan_whose_card_scores_three()
    {
        var p = Planner.Prompt("Proj", "Phone", PlanKind.Feature, Path.Combine(_dir, "phone.md"), null, null, [], null);
        var lines = p.Replace("\r\n", "\n").Split('\n');
        var start = Array.IndexOf(lines, "   ---");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("   Questions:"));
        var shape = string.Join("\n", lines[start..end].Select(l => l.Length >= 3 ? l[3..] : l));
        var plan = PlanDoc.Parse(shape);
        Assert.Equal("feature", plan.Kind);
        Assert.Equal("draft", plan.Status);
        Assert.Equal("Name", plan.Title);
        Assert.Single(plan.Goals);
        Assert.Single(plan.NotDoing);
        var q = Assert.Single(plan.Questions);
        Assert.Equal(2, q.Options.Count);
        Assert.Single(q.Options, o => o.Recommended);
        Assert.True(q.Open);
        Assert.Contains("approach", plan.Design);
        var card = Assert.Single(plan.Cards);
        Assert.Equal(3, card.Score.Stars);
        Assert.Empty(card.BlockedBy);
    }

    [Fact]
    public void A_change_message_names_the_file_and_holds_the_planner_to_the_part()
    {
        var m = Planner.ChangeMessage("D:/design/phone.md", "Goals", "make the goals sharper");
        Assert.StartsWith("make the goals sharper", m);
        Assert.Contains("Plan file: D:/design/phone.md", m);
        Assert.Contains("Change only its Goals part", m);
        Assert.DoesNotContain("Change only", Planner.ChangeMessage("D:/design/phone.md", null, "anything"));
    }

    const string Before = """
        ---
        kind: feature
        status: draft
        ---
        # Phone

        On the phone.

        ## Goals
        - Answer agents.

        ## Questions

        ### Which way?
        - **This** (recommended): quick.
        - **That**: slow.

        **Answer:** This (Philip, 2026-11-21)

        ## Design
        A thin client.

        ## Cards

        ### First
        Do it.
        """;

    [Fact]
    public void What_a_planner_changes_outside_the_part_it_was_asked_about_is_put_back()
    {
        var file = Path.Combine(_dir, "phone.md");
        var before = Before.ReplaceLineEndings("\r\n");
        // it changed Goals as asked, but also tidied the Design, dropped the answer and renamed the plan
        var after = before.Replace("- Answer agents.", "- Answer agents from the sofa.\r\n- Never miss a decision.")
            .Replace("A thin client.", "A thick client.").Replace("**Answer:** This (Philip, 2026-11-21)\r\n", "").Replace("# Phone", "# Phone app");
        File.WriteAllText(file, after);

        var put = Planner.KeepOnly(file, before, "Goals");

        Assert.Equal(["the title and properties", "Questions", "Design"], put);
        var text = File.ReadAllText(file);
        Assert.Equal(before.Replace("- Answer agents.", "- Answer agents from the sofa.\r\n- Never miss a decision."), text);
    }

    [Fact]
    public void A_planner_that_kept_to_the_part_is_left_alone()
    {
        var file = Path.Combine(_dir, "phone.md");
        var after = Before.Replace("A thin client.", "A thin client over the API.");
        File.WriteAllText(file, after);
        Assert.Empty(Planner.KeepOnly(file, Before, "Design"));
        Assert.Equal(after, File.ReadAllText(file));
        Assert.Equal("A thin client over the API.", Planner.Part(after, "design"));
    }

    [Fact]
    public void What_darg_writes_to_the_plan_during_a_held_turn_stands_and_only_the_planners_other_changes_go_back()
    {
        var file = Path.Combine(_dir, "phone.md");
        var other = Path.Combine(_dir, "other.md");
        File.WriteAllText(file, Before.ReplaceLineEndings("\r\n"));
        File.WriteAllText(other, Before);
        using var hold = PlanHold.Start(file, "Goals")!;

        // the planner, asked about Goals, changes Goals and tidies the Design too
        File.WriteAllText(file, File.ReadAllText(file).Replace("- Answer agents.", "- Answer agents from the sofa.").Replace("A thin client.", "A thick client."));
        // meanwhile, on the page and through the API: an answer changed, the plan agreed, a card's id written, a part added
        Assert.True(PlanStore.Answer(file, "Which way?", "That", "Philip", new DateTime(2026, 11, 22)));
        Assert.True(PlanStore.SetProperty(file, "status", "agreed"));
        Assert.True(PlanStore.SetCardId(file, "First", "AND-0009"));
        Assert.True(PlanStore.SetSection(file, "Not doing", "- Tablets."));
        // a write to another plan is nothing to this one
        Assert.True(PlanStore.SetSection(other, "Design", "Elsewhere."));

        var put = hold.Finish();

        Assert.Equal(["Design"], put);
        var text = File.ReadAllText(file);
        Assert.Contains("- Answer agents from the sofa.", text);
        Assert.Contains("A thin client.", text);
        Assert.DoesNotContain("A thick client.", text);
        Assert.Contains("**Answer:** That (Philip", text);
        Assert.DoesNotContain("**Answer:** This", text);
        Assert.Contains("status: agreed", text);
        Assert.Contains("### AND-0009", text);
        Assert.Equal("- Tablets.", Planner.Part(text, "Not doing"));
        Assert.DoesNotContain("Elsewhere", text);
        Assert.Contains("\r\n", text);
        Assert.DoesNotMatch("[^\r]\n", text);
    }

    [Fact]
    public void A_held_turn_with_nothing_else_written_still_puts_back_what_the_planner_changed()
    {
        var file = Path.Combine(_dir, "phone.md");
        File.WriteAllText(file, Before);
        var hold = PlanHold.Start(file, "Design")!;
        File.WriteAllText(file, Before.Replace("A thin client.", "A thin client over the API.").Replace("# Phone", "# Phone app"));
        Assert.Equal(["the title and properties"], hold.Finish());
        Assert.Equal(Before.Replace("A thin client.", "A thin client over the API."), File.ReadAllText(file));
        Assert.Null(PlanHold.Start(Path.Combine(_dir, "none.md"), "Goals"));
    }

    [Fact]
    public void Rebase_takes_the_parts_a_write_changed_and_keeps_the_rest()
    {
        var before = Before.ReplaceLineEndings("\n");
        var pre = before.Replace("A thin client.", "A thick client.");   // the planner's change was already on disk
        var post = pre.Replace("## Questions", "## Not doing\n- Tablets.\n\n## Questions").Replace("### First", "### AND-0001: First");
        var rebased = Planner.Rebase(before, pre, post);
        Assert.Equal(before.Replace("## Questions", "## Not doing\n- Tablets.\n\n## Questions").Replace("### First", "### AND-0001: First"), rebased);
        Assert.Equal(before, Planner.Rebase(before, pre, pre));
        // a part the write dropped goes
        Assert.DoesNotContain("## Design", Planner.Rebase(before, before, before.Replace("## Design\nA thin client.\n\n", "")));
    }

    [Fact]
    public void A_planners_change_in_a_part_the_page_also_writes_still_goes_back()
    {
        // QA round 2: the planner, held to Goals, rewords one question; the person answers another meanwhile. The part
        // was taken whole from the page's write, so the rewording stood and the status didn't name Questions.
        var file = Path.Combine(_dir, "phone.md");
        var two = Before.Replace("## Design", "### Second?\n- **Yes** (recommended): y.\n- **No**: n.\n\n## Design");
        File.WriteAllText(file, two);
        using var hold = PlanHold.Start(file, "Goals")!;
        File.WriteAllText(file, File.ReadAllText(file).Replace("- Answer agents.", "- Answer agents fast.").Replace("### Second?", "### Second, reworded?"));
        Assert.True(PlanStore.Answer(file, "Which way?", "That", "Philip", new DateTime(2026, 11, 22)));

        var put = hold.Finish();

        Assert.Equal(["Questions"], put);
        var text = File.ReadAllText(file);
        Assert.Contains("- Answer agents fast.", text);
        Assert.Contains("**Answer:** That (Philip", text);
        Assert.Contains("### Second?", text);
        Assert.DoesNotContain("reworded", text);
    }

    [Fact]
    public void Merge3_applies_the_writes_lines_to_mine_and_the_writes_win_where_both_changed()
    {
        const string pre = "a\nB\nc\nd\ne\nF\ng";    // the planner had changed b and f
        const string mine = "a\nb\nc\nd\ne\nf\ng";   // as it was before the planner
        Assert.Equal(mine + "\nh", Planner.Merge3(pre, mine, pre + "\nh"));                                    // a line added at the end
        Assert.Equal("x\n" + mine, Planner.Merge3(pre, mine, "x\n" + pre));                                    // and at the start
        Assert.Equal("a\nb\nc\nD\ne\nf\ng", Planner.Merge3(pre, mine, "a\nB\nc\nD\ne\nF\ng"));                 // a line between the planner's
        Assert.Equal("a\nb\nc\nd\nd2\ne\nf\ng", Planner.Merge3(pre, mine, "a\nB\nc\nd\nd2\ne\nF\ng"));         // a line put in between
        Assert.Equal("a\nb\nc\ne\nf\ng", Planner.Merge3(pre, mine, "a\nB\nc\ne\nF\ng"));                       // a line the write took out
        Assert.Equal("a\nB!\nc\nd\ne\nf\ng", Planner.Merge3(pre, mine, "a\nB!\nc\nd\ne\nF\ng"));               // the same line: the write's stands
        // right beside a line the planner changed, with no line between that both kept: the write's stretch stands whole
        Assert.Equal("a\nB\nC\nd\ne\nf\ng", Planner.Merge3(pre, mine, "a\nB\nC\nd\ne\nF\ng"));
        Assert.Equal(mine, Planner.Merge3(pre, mine, pre));
        Assert.Equal("z", Planner.Merge3(mine, mine, "z"));
        // the planner rewrote the file with LF where the plan had CRLF: lines still match, and the result keeps CRLF
        Assert.Equal("a\r\nb\r\nc\r\nD\r\ne\r\nf\r\ng", Planner.Merge3(pre, mine.Replace("\n", "\r\n"), "a\nB\nc\nD\ne\nF\ng"));
    }

    [Fact]
    public void Kinds_read_as_typed()
    {
        Assert.Equal(PlanKind.Feature, Planner.ParseKind(null));
        Assert.Equal(PlanKind.Feature, Planner.ParseKind(" Feature "));
        Assert.Equal(PlanKind.Project, Planner.ParseKind("project"));
        Assert.Null(Planner.ParseKind("story"));
    }
}
