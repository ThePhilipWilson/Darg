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
    public void Kinds_read_as_typed()
    {
        Assert.Equal(PlanKind.Feature, Planner.ParseKind(null));
        Assert.Equal(PlanKind.Feature, Planner.ParseKind(" Feature "));
        Assert.Equal(PlanKind.Project, Planner.ParseKind("project"));
        Assert.Null(Planner.ParseKind("story"));
    }
}
