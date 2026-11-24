using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0179: whole-project plans. A project plan lists features; each feature with a plan shows under it in the
/// tree (by its parent, or by the project's link to it), with its cards' progress rolled up into the project's. Ticking
/// features and pressing Draft starts a planner on each, the new plan's parent set to the project plan and the feature
/// linked to it.</summary>
public class ProjectPlanTests
{
    static string ProjectPlan() => """
        ---
        kind: project
        status: draft
        ---
        # Proj

        Everything Proj is meant to become.

        ## Goals
        - One place for the work.

        ## Questions

        ### Desktop first or phone first?
        - **Desktop** (recommended): it's where the agents run.
        - **Phone**: where I am.

        ## Features
        - [Phone](phone.md): Proj on the phone.
        - Themes: colours to pick from.
        - Search: find any card FAKE_PLAN
        - Voice: talk to Fettle.
        """;

    static string Feature(string title, string parent, string cards) => $"""
        ---
        kind: feature
        status: agreed
        {parent}
        ---
        # {title}

        ## Cards

        {cards}
        """;

    [AvaloniaFact]
    public async Task A_project_plan_shows_its_features_under_it_rolled_up_and_drafts_plans_for_the_ticked_ones()
    {
        using var app = DargApp.Make();
        var remote = app.Card("Phone remote", "Done");
        var pairing = app.Card("Phone pairing", "In Progress");
        var picker = app.Card("Theme picker", "To Do");
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        var projFile = Path.Combine(design, "proj.md");
        File.WriteAllText(projFile, ProjectPlan().ReplaceLineEndings("\n"));
        // Phone has no parent: the project's link puts it under; Themes isn't linked: its parent does
        File.WriteAllText(Path.Combine(design, "phone.md"), Feature("Phone", "", $"### {remote.Id}: Remote\nr\n\n### {pairing.Id}: Pairing\np\n"));
        File.WriteAllText(Path.Combine(design, "themes.md"), Feature("Themes", "parent: proj",
            $"### {picker.Id}: Picker\np\n\n### Dark mode\nA proposed card.\n"));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;

        // the tree: the project with both features under it, its progress theirs added up
        await app.Until(() => d.Tree.Count == 1 && d.Tree[0].Children.Count == 2, "the project plan with its two features under it");
        var proj = d.Tree[0];
        Assert.Equal("Proj", proj.Title);
        Assert.Equal(["Phone", "Themes"], proj.Children.Select(c => c.Title));
        Assert.Equal("1/2", proj.Children[0].Progress);
        Assert.Equal("0/2", proj.Children[1].Progress);
        Assert.Equal("1/4", proj.Progress);

        // the page: each feature with its plan's state and progress, the ones without a plan ticked to draft
        var page = d.Page!;
        Assert.Equal(projFile, page.File);
        Assert.Contains("2 of 4 features planned", page.Summary);
        Assert.Contains("2 proposed", page.Summary);
        Assert.Contains("3 of 4 cards made, 1 done", page.Summary);
        Assert.Equal(["Phone", "Themes", "Search", "Voice"], page.Features.Select(f => f.Title));
        Assert.Equal("agreed · 1/2 cards done", page.Features[0].State);
        Assert.Equal("agreed · 0/2 cards done", page.Features[1].State);
        Assert.Equal([false, false, true, true], page.Features.Select(f => f.Proposed));
        Assert.Equal("Draft 2 feature plans", page.MakeFeaturesText);
        Assert.Contains("Desktop first or phone first?", page.FeaturesOpenText);
        page.Features[1].OpenCommand.Execute(null);
        Assert.Equal("Themes", d.Page!.Title);
        d.SelectedNode = proj;
        page = d.Page!;
        await DargApp.Settle(150);
        app.Shot("project-plan");

        // Voice unticked; with a question open the first press only says so
        page.Features[3].Ticked = false;
        Assert.Equal("Draft 1 feature plan", page.MakeFeaturesText);
        page.MakeFeaturesCommand.Execute(null);
        Assert.True(page.ConfirmingFeatures);
        Assert.Contains("still open", d.Status);
        Assert.DoesNotContain(app.Prompts(), p => p.Contains("Search: find any card"));

        // the second press starts the planner on Search, under Proj, and links the feature to the file named for it
        page.MakeFeaturesCommand.Execute(null);
        var file = Path.Combine(design, "search.md");
        var planner = app.Agents.PlannerFor(file);
        Assert.NotNull(planner);
        Assert.Equal(AgentPurposes.Plan, planner.Purpose);
        Assert.False(d.IsDrafting);   // the page stays on the project
        var text = File.ReadAllText(projFile);
        Assert.Contains("- [Search](search.md): find any card FAKE_PLAN", text);
        Assert.Contains("- Voice: talk to Fettle.", text);
        Assert.Contains("status: agreed", text);
        await app.Until(() => app.Prompts().Any(p => p.Contains("Search: find any card")), "the planner's prompt");
        var prompt = app.Prompts().Single(p => p.Contains("Search: find any card"));
        Assert.Contains("parent: proj", prompt);
        Assert.Contains("kind: feature", prompt);
        Assert.Contains("one of the Features of the project plan " + projFile, prompt);
        Assert.DoesNotContain(app.Prompts(), p => p.Contains("Voice: talk"));

        // when its turn ends: the new plan's parent is the project plan, and it sits under it in the tree
        await app.Until(() => File.Exists(file) && d.Tree.Count == 1 && d.Tree[0].Children.Count == 3, "the feature plan written and under the project");
        Assert.Equal("proj", PlanDoc.Load(file).Parent);
        Assert.Equal(file, d.Tree[0].Children[1].Plan.File);   // phone, search, themes: by file name
        await app.Until(() => d.Page is { } p && p.File == projFile && p.Features[2].HasPlan, "the project page reading Search's plan");
        Assert.Equal("draft · 0/1 cards done", d.Page!.Features[2].State);
        Assert.True(d.Page.Features[3].Proposed);
        Assert.False(d.Page.Features[3].Ticked);   // the untick is kept across the reload
        Assert.False(d.Page.CanMakeFeatures);
        await DargApp.Settle(150);
        app.Shot("project-plan-drafted");

        // Fettle and the phone do the same through the API
        var reply = JsonNode.Parse(await app.Api("plan-features", new { project = "proj", plan = "proj", only = "Voice", confirm = "true" }))!;
        var started = Assert.Single(reply["started"]!.AsArray());
        Assert.Equal("Voice", (string?)started!["feature"]);
        Assert.Equal(Path.Combine(design, "voice.md"), (string?)started["file"]);
        Assert.Contains("- [Voice](voice.md): talk to Fettle.", File.ReadAllText(projFile));
        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("plan-features", new { project = "proj", plan = "proj", only = "Search", confirm = "true" }));
        Assert.Contains("without a plan", again.Message);
        Assert.Equal("", app.Crashes());
    }
}
