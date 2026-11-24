using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0199: every edit the Design page makes to a plan is committed at once, the plan file alone, and pushed
/// where the project's rule says push; the unticked rows and the plan's planner chat are kept in the plan file.</summary>
public class PlanCommitTests
{
    static string PhonePlan() => """
        ---
        kind: feature
        status: draft
        board: Main
        parent: proj
        ---
        # Phone

        Proj on the phone.

        ## Goals
        - Answer agents from the sofa.

        ## Not doing
        - Tablets.

        ## Questions

        ### How does the phone reach the PC?
        - **Tailscale** (recommended): nothing goes through anyone else's server.
        - **Home network only**: simplest, no use on the move.

        ### Which port?
        - **47818** (recommended): beside the artifact server's.
        - **443**: looks like the web.

        **Answer:** 47818 (decided by agent, T-0099, 2026-11-20)

        ### Which shell runs commands?
        - **pwsh** (recommended): what the saved commands were written for.
        - **cmd**: always there.

        **Answer:** cmd (decided by agent, T-0098, 2026-11-20)

        ## Design
        A thin client over the API.

        ## Cards

        ### Remote access
        TLS on its own port and a token per phone, so a phone can call the API from anywhere it has a route to the PC.
        **Where:** `src/Remote.cs`
        **Done when:** a paired phone calls the API and one without a token is refused.
        **Blocked by:** none

        ### Pairing in the app
        Scan the pairing code on the phone and keep the token it gives, so pairing happens once per phone and not again.
        **Where:** `src/Pair.cs`
        **Done when:** a paired phone keeps its token across restarts.
        **Blocked by:** none
        """;

    static string ProjectPlan() => """
        ---
        kind: project
        status: agreed
        ---
        # Proj

        Everything Proj is meant to become.

        ## Goals
        - One place for the work.

        ## Features
        - [Phone](phone.md): Proj on the phone.
        - Themes: colours to pick from.
        - Sounds: a chime when an agent asks.
        """;

    /// <summary>The project's docs folder made a git repo of its own, with a bare remote it pushes to, and the
    /// project's commit rule for it set to push.</summary>
    static (string Remote, string Design) DocsRepo(DargApp app)
    {
        var remote = Path.Combine(app.Folder, "remote.git");
        Git(app.Folder, "init", "-q", "--bare", "-b", "main", remote);
        Git(app.Docs, "init", "-q", "-b", "main");
        Git(app.Docs, "config", "user.name", "Darg tests");
        Git(app.Docs, "config", "user.email", "tests@example.com");
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        File.WriteAllText(Path.Combine(design, "phone.md"), PhonePlan());
        File.WriteAllText(Path.Combine(design, "proj.md"), ProjectPlan());
        Git(app.Docs, "add", ".");
        Git(app.Docs, "commit", "-q", "-m", "First");
        Git(app.Docs, "remote", "add", "origin", remote);
        Git(app.Docs, "push", "-q", "-u", "origin", "main");
        var configFile = Path.Combine(app.Home, "instance.json");
        var config = JsonNode.Parse(File.ReadAllText(configFile))!;
        config["projects"]![0]!["commits"] = new JsonObject { [app.Docs] = "push" };
        File.WriteAllText(configFile, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return (remote, design);
    }

    [AvaloniaFact]
    public async Task Every_edit_the_design_page_makes_is_committed_alone_and_pushed()
    {
        using var app = DargApp.Make();
        var (remote, design) = DocsRepo(app);
        var phone = Path.Combine(design, "phone.md");
        // other work in the repo, changed and staged, which no plan commit may take
        File.AppendAllText(Path.Combine(app.Docs, "PLAN.md"), "Someone else's edit.\n");
        File.WriteAllText(Path.Combine(app.Docs, "notes.txt"), "staged by someone else\n");
        Git(app.Docs, "add", "notes.txt");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Loaded, "the plans read");
        Show(d, "phone");
        await app.Until(() => d.Page?.File == phone, "the phone plan open");
        var commits = Count(app.Docs);

        // one commit per action, touching only that plan, on the remote too, other work left as it was
        async Task Committed(string what, string file = "design/phone.md")
        {
            await app.Until(() => Count(app.Docs) == commits + 1, $"a commit for {what}");
            await d.LastCommit;
            commits++;
            Assert.Equal(file, Git(app.Docs, "show", "--name-only", "--format=", "HEAD").Trim());
            Assert.StartsWith("Design: ", Git(app.Docs, "log", "-1", "--format=%s").Trim());
            Assert.Equal(Git(app.Docs, "rev-parse", "HEAD"), Git(remote, "rev-parse", "main"));
            var status = Git(app.Docs, "status", "--porcelain");
            Assert.Contains(" M PLAN.md", status);
            Assert.Contains("A  notes.txt", status);
            Assert.DoesNotContain("design/", status);
        }

        // editing a part
        var page = d.Page!;
        page.Goals.EditCommand.Execute(null);
        page.Goals.Draft = "- Answer agents from the sofa.\n- Never miss a decision.";
        page.Goals.SaveCommand.Execute(null);
        await Committed("editing Goals");
        Assert.Contains("Never miss a decision", File.ReadAllText(phone));
        await app.Until(() => d.Status.Contains("Committed and pushed."), "the status saying it was committed and pushed");

        // answering a question
        await app.Until(() => d.Page != page, "the page read again");
        page = d.Page!;
        page.Questions.Single(q => q.Question.StartsWith("How does")).Options[0].PickCommand.Execute(null);
        await Committed("an answer");
        Assert.Contains("**Answer:** Tailscale", File.ReadAllText(phone));

        // Keep: the agent's answer stays, with the person's name on it
        await app.Until(() => d.Page != page, "the page read again");
        page = d.Page!;
        var port = page.Questions.Single(q => q.Question == "Which port?");
        Assert.True(port.ByAgent);
        port.KeepCommand.Execute(null);
        await Committed("Keep");
        Assert.DoesNotContain("47818 (decided by agent", File.ReadAllText(phone));

        // Change: a new answer for the agent's choice
        await app.Until(() => d.Page != page, "the page read again");
        page = d.Page!;
        var shell = page.Questions.Single(q => q.Question == "Which shell runs commands?");
        shell.ChangeCommand.Execute(null);
        shell.OwnAnswer = "pwsh";
        shell.SendOwnCommand.Execute(null);
        await Committed("Change");
        Assert.Contains("**Answer:** pwsh", File.ReadAllText(phone));

        // unticking a row: kept in the plan, committed, and read back by a page built afresh (as after a restart)
        await app.Until(() => d.Page != page, "the page read again");
        page = d.Page!;
        page.Cards.Single(c => c.Title == "Pairing in the app").Ticked = false;
        await Committed("unticking a row");
        Assert.Equal(["Pairing in the app"], PlanDoc.Load(phone).Unticked);
        var fresh = new DesignViewModel(app.Project.Config, design);
        fresh.Reload();
        await app.Until(() => fresh.Loaded, "a fresh Design section's plans read");
        Show(fresh, "phone");
        Assert.False(fresh.Page!.Cards.Single(c => c.Title == "Pairing in the app").Ticked);
        Assert.True(fresh.Page.Cards.Single(c => c.Title == "Remote access").Ticked);
        Assert.Equal(1, fresh.Page.MakeCount);

        // Make cards: the ticked one is made and its id committed
        page.MakeCardsCommand.Execute(null);
        await Committed("Make cards");
        var made = PlanDoc.Load(phone);
        Assert.True(made.Cards.Single(c => c.Title == "Remote access").Made);
        Assert.False(made.Cards.Single(c => c.Title == "Pairing in the app").Made);

        // Draft feature plans, on the project plan: the feature's link committed
        Show(d, "proj");
        await app.Until(() => d.Page?.File == Path.Combine(design, "proj.md"), "the project plan open");
        page = d.Page!;
        page.Features.Single(f => f.Title == "Sounds").Ticked = false;
        await Committed("unticking a feature", "design/proj.md");
        Assert.Equal(["Sounds"], PlanDoc.Load(Path.Combine(design, "proj.md")).UntickedFeatures);
        page.MakeFeaturesCommand.Execute(null);
        await Committed("Draft feature plans", "design/proj.md");
        var proj = PlanDoc.Load(Path.Combine(design, "proj.md"));
        Assert.NotEqual("", proj.Features.Single(f => f.Title == "Themes").Link);
        Assert.Equal("", proj.Features.Single(f => f.Title == "Sounds").Link);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_planner_writes_its_chat_into_the_plan()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Loaded, "the plans read");
        d.Idea = "Phone app: FAKE_PLAN";
        d.StartPlanCommand.Execute(null);
        var file = Path.Combine(app.Docs, "design", "phone-app.md");
        var planner = app.Agents.PlannerFor(file)!;
        await app.Until(() => d.Page?.File == file && planner is { Working: false, SessionId: not null }, "the drafted plan open, its planner's turn over");
        await app.Until(() => PlanDoc.Load(file).PlannerSession == planner.SessionId, "the planner's session in the plan's planner: property");
        Assert.Contains($"planner: {planner.SessionId}", File.ReadAllText(file));
        // the docs folder isn't a repo of its own here (it sits in Darg's ignored .shots, or in no repo): nothing to
        // commit, and nothing fails
        Assert.Contains((await d.LastCommit).Outcome, new[] { PlanCommits.Outcome.Nothing, PlanCommits.Outcome.NotInGit });
    }

    [AvaloniaFact]
    public async Task A_plans_planner_chat_is_found_from_the_plan_file_on_a_fresh_instance()
    {
        using var app = DargApp.Make();
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        var file = Path.Combine(design, "phone.md");
        File.WriteAllText(file, PhonePlan().ReplaceLineEndings("\n").Replace("parent: proj\n", "parent: proj\nplanner: fake-session-from-another-instance\n"));
        await app.Start();
        Assert.Empty(app.Agents.Agents);
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Page?.File == file, "the plan open");

        var planner = d.Planner;
        Assert.NotNull(planner);
        Assert.Equal("fake-session-from-another-instance", planner.SessionId);
        Assert.Equal(AgentPurposes.Plan, planner.Purpose);
        Assert.Equal(file, planner.Plan);
        Assert.Same(planner, app.Agents.PlannerFor(file));
        Assert.Equal("Plan: Phone", planner.Name);
        Assert.False(d.NoPlanner);

        // a message carries the conversation on: the session resumed, with the plan and board folders to read
        d.Ask = "Sharpen the goals";
        d.AskPlannerCommand.Execute(null);
        await app.Until(() => app.Prompts().Any(p => p.Contains("Sharpen the goals")), "the message at the planner");
        var start = app.ClaudeCalls().Last(c => (string?)c["kind"] == "start");
        var args = start["args"]!.AsArray().Select(a => (string?)a).ToList();
        var at = args.IndexOf("--resume");
        Assert.True(at >= 0, "resumed: " + string.Join(' ', args));
        Assert.Equal("fake-session-from-another-instance", args[at + 1]);
        Assert.Contains(design, args);
        Assert.Contains(app.Boards, args);
        // seen again, the same chat, not another
        Show(d, "phone");
        Assert.Same(planner, d.Planner);
        Assert.Single(app.Agents.Agents);
    }

    /// <summary>Pick a plan in the tree by its file name, as clicking it does.</summary>
    static void Show(DesignViewModel d, string slug)
    {
        static IEnumerable<PlanNode> All(IEnumerable<PlanNode> nodes) => nodes.SelectMany(n => All(n.Children).Prepend(n));
        d.SelectedNode = All(d.Tree).Single(n => n.Plan.Slug == slug);
    }

    static int Count(string repo) => int.Parse(Git(repo, "rev-list", "--count", "HEAD").Trim());

    static string Git(string cwd, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)}: {error}");
        return output.Result;
    }
}
