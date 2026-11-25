using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0201: a project plan's Make all makes every feature plan's ticked cards, each on its own board, in the order
/// they unblock each other across plans, with real ids in every Blocked by; a loop across plans makes nothing and names
/// it; a heading with commas in it no longer stops a plan's Make cards. The plans are the copies of the Seamless machine
/// switching plans in tests/Darg.Core.Tests/PlanData/seamless (see PlanMakingTests for what was changed in them).</summary>
public class MakeAllTests
{
    static string Seamless()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "tests", "Darg.Core.Tests", "PlanData", "seamless");
            if (Directory.Exists(f)) return f;
        }
        throw new DirectoryNotFoundException("tests/Darg.Core.Tests/PlanData/seamless");
    }

    /// <summary>Scroll the page to the button named <paramref name="name"/>, so a shot shows it and what is above it.</summary>
    static Button ShowButton(DargApp app, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var button = app.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name && b.IsEffectivelyVisible);
        button.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        return button;
    }

    /// <summary>Lines drawn on screen that start with the text, wrapped rather than cut off.</summary>
    static List<TextBlock> Drawn(DargApp app, string start) =>
        app.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Text?.StartsWith(start) == true && t.TextWrapping == Avalonia.Media.TextWrapping.Wrap).ToList();

    static string CopyPlans(DargApp app, params string[] names)
    {
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        foreach (var f in Directory.GetFiles(Seamless(), "*.md"))
            if (names.Length == 0 || names.Contains(Path.GetFileNameWithoutExtension(f)))
                File.Copy(f, Path.Combine(design, Path.GetFileName(f)));
        return design;
    }

    [AvaloniaFact]
    public async Task Make_all_on_the_project_plan_makes_every_features_cards_on_their_own_boards_cross_plan_blockers_first()
    {
        using var app = DargApp.Make();
        var design = CopyPlans(app);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Tree.Count == 1 && d.Tree[0].Children.Count == 8, "the project plan with its eight feature plans under it");
        d.SelectedNode = d.Tree[0];
        var page = d.Page!;
        Assert.True(page.ShowMakeAll);
        var features = PlanStore.List(design).Where(p => !p.IsProject).ToList();
        var total = features.Sum(p => p.Cards.Count);
        Assert.Equal($"Make all {total} cards", page.MakeAllText);
        Assert.False(page.HasAllCant);
        Assert.True(page.HasAllOpen);   // the feature plans have questions open
        ShowButton(app, "Make all");
        app.Shot("make-all-before");

        // the first press asks about the open questions and makes nothing
        page.MakeAllCommand.Execute(null);
        Assert.True(page.ConfirmingAll);
        Assert.EndsWith("anyway", page.MakeAllText);
        Assert.Contains("still open", d.Status);
        Assert.Equal(["Main"], BoardStore.List(app.Boards));

        page.MakeAllCommand.Execute(null);
        Assert.Contains($"Made {total} cards", d.Status);
        features = PlanStore.List(design).Where(p => !p.IsProject).ToList();
        var all = new Dictionary<string, Card>();
        foreach (var p in features)
        {
            Assert.All(p.Cards, c => Assert.True(c.Made, $"{p.Title}: {c.Title}"));
            var board = BoardStore.ReadCards(Path.Combine(app.Boards, p.Board)).ToDictionary(c => c.Id);
            Assert.Equal(p.Cards.Select(c => c.Id).Order(), board.Keys.Order());   // each feature on its own board
            foreach (var (id, c) in board) all[id] = c;
        }
        Assert.Equal(9, BoardStore.List(app.Boards).Count);
        Assert.All(all.Values, c => Assert.All(c.BlockedBy, b => Assert.True(all.ContainsKey(b), $"{c.Id} waits on {b}, which is a real id")));
        string Id(string title) => features.SelectMany(p => p.Cards).Single(c => c.Title == title).Id;
        // a wait on a card in another feature's plan is its id
        Assert.Contains(Id("Keep both sides of a sync conflict"), all[Id("Put .darg on a darg branch and bring today's files in")].BlockedBy);
        Assert.Contains(Id("One sync round for the darg branch"), all[Id("A sync round that runs unattended")].BlockedBy);
        // a heading with commas in it, in its own plan
        Assert.Equal([Id("Spike script: make a session, copy it under another path, resume it")], all[Id("Run the spike on this PC and record what has to be copied")].BlockedBy);

        await app.Until(() => d.Page is { MakeAllText: "Every card made" }, "the project page reading the made cards");
        await app.Until(() => app.Board.Boards.Contains("Auto-sync"), "the Board section reading the new boards");
        app.Shot("make-all-after");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Plan_make_all_refuses_a_loop_across_plans_naming_it_and_ready_makes_the_rest()
    {
        using var app = DargApp.Make();
        var design = CopyPlans(app);
        var auto = Path.Combine(design, "auto-sync.md");
        var text = File.ReadAllText(auto);
        var at = text.IndexOf("**Blocked by:** none", text.IndexOf("### Keep both sides of a sync conflict"));
        File.WriteAllText(auto, text[..at] + "**Blocked by:** Put .darg on a darg branch and bring today's files in" + text[(at + "**Blocked by:** none".Length)..]);
        await app.Start();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.Api("plan-make-all", new { project = "proj", plan = "seamless-machine-switching", confirm = "true" }));
        Assert.Contains("loop", ex.Message);
        Assert.Contains("Put .darg on a darg branch and bring today's files in", ex.Message);
        Assert.Contains("ready: true", ex.Message);
        Assert.Equal(["Main"], BoardStore.List(app.Boards));

        // the page says the same before anything is pressed
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Tree.Count == 1 && d.Tree[0].Children.Count == 8, "the plans");
        d.SelectedNode = d.Tree[0];
        Assert.True(d.Page!.HasAllCant);
        Assert.Contains(d.Page.AllCant, l => l.Contains("loop"));
        Assert.StartsWith("Make the ", d.Page.MakeAllText);
        ShowButton(app, "Make all");
        Assert.Contains(Drawn(app, "Can't be made yet: "), t => t.Text!.Contains("loop"));
        app.Shot("make-all-loop");

        var made = JsonNode.Parse(await app.Api("plan-make-all", new { project = "proj", plan = "seamless-machine-switching", confirm = "true", ready = "true" }))!;
        var cant = made["cantMake"]!.AsArray().Select(c => (string)c!["card"]!).ToList();
        Assert.Contains("Keep both sides of a sync conflict", cant);
        Assert.Contains("Put .darg on a darg branch and bring today's files in", cant);
        Assert.Contains("A sync round that runs unattended", cant);   // waits on one in the loop
        var madeTitles = made["made"]!.AsArray().Select(m => (string)m!["title"]!).ToList();
        Assert.Contains("State the rule for the agents feature", madeTitles);
        Assert.DoesNotContain("Keep both sides of a sync conflict", madeTitles);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Make_cards_on_the_resume_spike_plan_makes_all_four_and_a_row_that_cant_be_made_says_why()
    {
        using var app = DargApp.Make();
        var design = CopyPlans(app, "seamless-machine-switching", "resume-spike", "agents-and-fettle-chats-travel");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Tree.Count == 1 && d.Tree[0].Children.Count == 2, "the plans");

        // Agents and Fettle chats: every card can be made (its waits are in its own plan)
        d.SelectedNode = d.Tree[0].Children.Single(n => n.Title == "Resume spike");
        var page = d.Page!;
        Assert.Equal(4, page.MakeCount);
        Assert.False(page.HasCant);
        Assert.Equal("Make 4 cards", page.MakeText);
        page.MakeCardsCommand.Execute(null);   // three questions open: asks first
        Assert.Contains("still open", d.Status);
        page.MakeCardsCommand.Execute(null);
        Assert.Contains("Made 4 cards", d.Status);
        var spike = PlanDoc.Load(Path.Combine(design, "resume-spike.md"));
        var cards = BoardStore.ReadCards(Path.Combine(app.Boards, "Resume spike")).ToDictionary(c => c.Id);
        for (var i = 1; i < 4; i++) Assert.Equal([spike.Cards[i - 1].Id], cards[spike.Cards[i].Id].BlockedBy);

        // a card in the agents plan made to wait on a heading no plan has, and one on a card of the spike that has no id
        d.SelectedNode = d.Tree[0].Children.Single(n => n.Title.StartsWith("Agents"));
        Assert.False(d.Page!.HasCant);
        var agents = Path.Combine(design, "agents-and-fettle-chats-travel.md");
        var text = File.ReadAllText(agents);
        var at = text.IndexOf("**Blocked by:** none", text.IndexOf("### Resume that fails starts fresh from the transcript"));
        File.WriteAllText(agents, text[..at] + "**Blocked by:** Run the spike on this PC and record what has to be copied, A heading nobody wrote" + text[(at + "**Blocked by:** none".Length)..]);
        d.RefreshCommand.Execute(null);
        await app.Until(() => d.Page is { HasCant: true } p && p.File == agents, "the agents plan read again, with a row that can't be made");
        page = d.Page!;
        var row = page.Cards.Single(c => c.Title == "Resume that fails starts fresh from the transcript");
        Assert.True(row.HasCantMake);
        Assert.Contains("\"A heading nobody wrote\" is no card in this plan or the plans beside it", row.CantMakeText);
        Assert.Contains("waits on " + spike.Cards[1].Id, row.Blockers);   // the made spike card, by id
        Assert.True(row.BlockerWarning);
        var dependent = page.Cards.Single(c => c.Title == "Two machines in one test: an agent carried on from the other");
        Assert.Contains("which can't be made", dependent.CantMakeText);
        Assert.Equal($"Make the {page.CanCount} that can be made", page.MakeText);
        ShowButton(app, "Make cards");
        Assert.Contains(Drawn(app, "Can't be made yet: "), t => t.Text!.Contains("A heading nobody wrote"));
        app.Shot("cant-make-rows");

        // the API says the same and makes nothing; the page's button makes the rest
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("plan-cards", new { project = "proj", plan = "agents-and-fettle-chats-travel", confirm = "true" }));
        Assert.Contains("A heading nobody wrote", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(app.Boards, "Agents and Fettle chats travel")));
        page.MakeCardsCommand.Execute(null);   // every question in this plan is answered: one press
        Assert.Contains("Made 7 cards", d.Status);
        Assert.Contains("2 left unmade", d.Status);
        var made = BoardStore.ReadCards(Path.Combine(app.Boards, "Agents and Fettle chats travel")).Select(c => c.Title).ToList();
        Assert.Equal(7, made.Count);
        Assert.DoesNotContain("Resume that fails starts fresh from the transcript", made);
        Assert.Equal("", app.Crashes());
    }
}
