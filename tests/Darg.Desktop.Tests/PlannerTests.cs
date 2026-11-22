using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0176: the planner. A line typed in the Design section's + Plan box starts an agent that drafts the
/// plan file; the page opens it when the turn ends, with the planner's chat beside it. Asked to change one part,
/// only that part changes. The API's plan command starts one too, for Fettle and the phone.</summary>
public class PlannerTests
{
    [AvaloniaFact]
    public async Task A_line_typed_in_plus_plan_drafts_a_plan_the_page_opens_with_its_planner_beside_it()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Loaded, "the plans read");
        Assert.True(d.CanPlan);
        Assert.False(d.ShowChat);

        d.Idea = "Phone app: answer agents from the sofa FAKE_PLAN";
        d.StartPlanCommand.Execute(null);
        var file = Path.Combine(app.Docs, "design", "phone-app.md");
        var planner = app.Agents.PlannerFor(file);
        Assert.NotNull(planner);
        Assert.Equal(AgentPurposes.Plan, planner.Purpose);
        // while it drafts: the page says so and the chat beside it is the planner's
        Assert.Same(planner, d.Planner);
        Assert.True(d.ShowChat);
        Assert.Equal("", d.Idea);

        await app.Until(() => d.Page?.File == file, "the drafted plan open on the page");
        Assert.False(d.IsDrafting);
        Assert.Same(planner, d.Planner);
        Assert.Equal("Fake plan", d.Page!.Title);
        Assert.Single(d.Page.Questions);
        Assert.Single(d.Page.Cards);
        Assert.Contains("Goals", d.ChangeParts);
        Assert.Contains("Cards", d.ChangeParts);

        // what the planner was asked: the line, the file Darg named, every part drafted with no interview first
        var prompt = app.Prompts().Single(p => p.Contains("FAKE_PLAN"));
        Assert.Contains("> Phone app: answer agents from the sofa FAKE_PLAN", prompt);
        Assert.Contains("Plan file: " + file, prompt);
        Assert.Contains("Don't interview me", prompt);
        Assert.Contains("## Questions", prompt);
        Assert.Contains("3 of 3", prompt);
        // it may read the plans folder and the boards
        var start = app.ClaudeCalls().First(c => (string?)c["kind"] == "start" && c["args"]!.AsArray().Any(a => (string?)a == Path.Combine(app.Docs, "design")));
        Assert.Contains(start["args"]!.AsArray(), a => (string?)a == app.Boards);
        await DargApp.Settle(150);
        app.Shot("planner");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Asking_the_planner_to_change_one_part_changes_that_part_only()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        d.Idea = "Phone app: FAKE_PLAN";
        d.StartPlanCommand.Execute(null);
        var file = Path.Combine(app.Docs, "design", "phone-app.md");
        await app.Until(() => d.Page?.File == file && d.Planner is { Working: false }, "the drafted plan open, its planner's turn over");
        Assert.Contains("FAKE_OLD goal", d.Page!.Goals.Markdown);

        // the fake planner, asked about Goals, rewrites FAKE_OLD everywhere: Design too
        d.ChangePart = "Goals";
        d.Ask = "Make the goal sharper FAKE_REWRITE";
        d.AskPlannerCommand.Execute(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("sharper")), "the message at the planner");
        var asked = app.Prompts().Single(p => p.Contains("sharper"));
        Assert.Contains("Change only its Goals part", asked);
        Assert.Contains("Plan file: " + file, asked);
        await app.Until(() => d.Status.Contains("too: put back"),"the planner's turn over and what it changed outside Goals put back");
        var text = File.ReadAllText(file);
        Assert.Contains("FAKE_NEW goal", text);
        Assert.Contains("FAKE_OLD design", text);
        Assert.DoesNotContain("FAKE_NEW design", text);
        Assert.Contains("Design", d.Status);
        await app.Until(() => d.Page!.Goals.Markdown.Contains("FAKE_NEW"), "the page read again");
        Assert.Contains("FAKE_OLD", d.Page!.Design.Markdown);

        // the whole plan picked: what it changes stands
        d.ChangePart = DesignViewModel.WholePlan;
        File.WriteAllText(file, File.ReadAllText(file).Replace("FAKE_NEW", "FAKE_OLD"));
        d.Ask = "Tidy it all FAKE_REWRITE";
        d.AskPlannerCommand.Execute(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("Tidy it all")), "the message at the planner");
        Assert.DoesNotContain("Change only", app.Prompts().Single(p => p.Contains("Tidy it all")));
        await app.Until(() => File.ReadAllText(file).Contains("FAKE_NEW design") && d.Planner is { Working: false }, "the whole plan rewritten");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task What_the_page_writes_while_the_planner_changes_one_part_stands()
    {
        // QA round 1: a question answered on the page during a one-part turn was wiped when the turn ended, and the
        // status blamed the planner for it
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "8");
        try
        {
            using var app = DargApp.Make();
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Design" });
            var d = app.Project.Design!;
            d.Idea = "Phone app: FAKE_PLAN";
            d.StartPlanCommand.Execute(null);
            var file = Path.Combine(app.Docs, "design", "phone-app.md");
            await app.Until(() => d.Page?.File == file && d.Planner is { Working: false }, "the drafted plan open, its planner's turn over", 60);

            d.ChangePart = "Goals";
            d.Ask = "Make the goal sharper FAKE_REWRITE";
            d.AskPlannerCommand.Execute(null);
            await app.Until(() => File.ReadAllText(file).Contains("FAKE_NEW goal"), "the planner at work on Goals");
            Assert.True(d.Planner!.Working);

            // on the page while it works: the question answered, the other choice than the recommended one
            var q = d.Page!.Questions.Single();
            q.Options.Single(o => o.Label == "That way").PickCommand.Execute(null);
            Assert.Contains("**Answer:** That way", File.ReadAllText(file));
            Assert.True(d.Planner.Working);

            await app.Until(() => d.Status.Contains("too: put back"), "the planner's turn over and its Design change put back", 60);
            var text = File.ReadAllText(file);
            Assert.Contains("FAKE_NEW goal", text);
            Assert.Contains("FAKE_OLD design", text);
            Assert.Contains("**Answer:** That way", text);
            Assert.Contains("Design", d.Status);
            Assert.DoesNotContain("Questions", d.Status);
            await app.Until(() => d.Page!.Questions.Single().Answered, "the page read again with the answer");
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task The_planners_change_in_the_part_the_page_answers_in_still_goes_back()
    {
        // QA round 2: held to Goals, the planner also reworded an option in Questions; the person answered the question
        // meanwhile, and the whole Questions part was taken from that write, so the rewording stood unreported
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "8");
        try
        {
            using var app = DargApp.Make();
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Design" });
            var d = app.Project.Design!;
            d.Idea = "Phone app: FAKE_PLAN";
            d.StartPlanCommand.Execute(null);
            var file = Path.Combine(app.Docs, "design", "phone-app.md");
            await app.Until(() => d.Page?.File == file && d.Planner is { Working: false }, "the drafted plan open, its planner's turn over", 60);

            d.ChangePart = "Goals";
            d.Ask = "Make the goal sharper FAKE_REWRITE FAKE_REWORD";
            d.AskPlannerCommand.Execute(null);
            await app.Until(() => File.ReadAllText(file).Contains("the slow one, reworded."), "the planner at work, Questions touched too");
            Assert.True(d.Planner!.Working);

            d.Page!.Questions.Single().Options.Single(o => o.Label == "That way").PickCommand.Execute(null);
            Assert.Contains("**Answer:** That way", File.ReadAllText(file));
            Assert.True(d.Planner.Working);

            await app.Until(() => d.Status.Contains("too: put back"), "the planner's turn over and its other changes put back", 60);
            var text = File.ReadAllText(file);
            Assert.Contains("FAKE_NEW goal", text);
            Assert.Contains("**Answer:** That way", text);
            Assert.Contains("- **That way**: the slow one.", text);
            Assert.DoesNotContain("reworded", text);
            Assert.Contains("Questions", d.Status);
            Assert.Contains("Design", d.Status);
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task Fettle_and_the_phone_start_a_plan_through_the_api()
    {
        Assert.Contains("plan", RemoteAccess.Commands);
        using var app = DargApp.Make();
        await app.Start();
        var reply = JsonNode.Parse(await app.Api("plan", new { project = "proj", idea = "Themes: colours to pick from FAKE_PLAN", kind = "project" }))!;
        var file = (string)reply["file"]!;
        Assert.Equal(Path.Combine(app.Docs, "design", "themes.md"), file);
        var agent = app.Agents.Find((string)reply["agent"]!);
        Assert.Equal(AgentPurposes.Plan, agent!.Purpose);
        Assert.Equal(file, agent.Plan);
        await app.Until(() => File.Exists(file), "the plan written");
        var prompt = app.Prompts().Single(p => p.Contains("Themes: colours"));
        Assert.Contains("kind: project", prompt);
        Assert.Contains("## Features", prompt);

        // a second plan on the same words while the first's name is held gets a name of its own
        var again = JsonNode.Parse(await app.Api("plan", new { project = "proj", idea = "Themes" }))!;
        Assert.Equal(Path.Combine(app.Docs, "design", "themes-2.md"), (string)again["file"]!);
        await Assert.ThrowsAnyAsync<Exception>(() => app.Api("plan", new { project = "proj", idea = "x", kind = "story" }));
        Assert.Equal("", app.Crashes());
    }
}
