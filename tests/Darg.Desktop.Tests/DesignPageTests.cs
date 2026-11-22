using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0175: the Design section lists the plans in docs/design/ as a tree and opens one as a page in its parts;
/// an answer tapped there lands in the file, a part is edited in place, and a made card's row shows its column and
/// agent from the board and opens the card.</summary>
public class DesignPageTests
{
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
        """;

    static string FeaturePlan(string madeId) => $"""
        ---
        kind: feature
        status: agreed
        board: Main
        parent: proj
        ---
        # Phone

        Proj on the phone, for answering what waits on me.

        ## Goals
        - Answer agents from the sofa.

        ## Not doing
        - Tablets.

        ## Questions

        ### How does the phone reach the PC?
        - **Tailscale** (recommended): nothing goes through anyone else's server.
        - **Home network only**: simplest, no use on the move.

        ## Design
        A thin client over the API.

        ## Cards

        ### {madeId}: Remote access
        TLS on its own port and a token per phone.
        **Where:** `src/Remote.cs`
        **Done when:** a paired phone calls the API and one without a token is refused.
        **Blocked by:** none

        ### Pairing in the app
        Scan the code and keep the token.
        **Where:** `src/Pair.cs`
        **Done when:** a paired phone keeps its token across restarts.
        **Blocked by:** {madeId}
        """;

    [AvaloniaFact]
    public async Task The_design_page_shows_a_plan_in_its_parts_takes_answers_and_links_its_cards()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var made = app.Card("Remote access", "In Progress");
            var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
            File.WriteAllText(Path.Combine(design, "proj.md"), ProjectPlan());
            var phoneFile = Path.Combine(design, "phone.md");
            // CRLF whatever the checkout (a raw string takes the source file's line endings, which autocrlf makes
            // CRLF here and LF elsewhere), so the edit below always checks that a typed \n becomes the file's own
            File.WriteAllText(phoneFile, FeaturePlan(made.Id).ReplaceLineEndings("\r\n"));
            await app.Start();
            await app.Api("work-on", new { project = "proj", card = made.Id });
            await app.Until(() => app.Agents.ForCard(made.Id) is not null, "an agent on the made card");

            await app.Api("show", new { project = "proj", section = "Design" });
            Assert.Equal(Section.Design, app.Project.Section);
            var d = app.Project.Design!;
            // the tree: the project plan with its feature under it
            await app.Until(() => d.Tree.Count == 1 && d.Tree[0].Children.Count == 1, "the plan tree");
            Assert.Equal("Proj", d.Tree[0].Title);
            var phone = d.Tree[0].Children[0];
            Assert.Equal("Phone", phone.Title);
            Assert.Equal("0/2", phone.Progress);
            // WOR-0179: the project has no cards of its own, so it shows its features' rolled up
            Assert.Equal("0/2", d.Tree[0].Progress);

            // the project plan's page has Features, one linked to its plan
            Assert.Equal("Proj", d.Page!.Title);
            Assert.True(d.Page.ShowFeatures);
            Assert.Equal(["Phone", "Themes"], d.Page.Features.Select(f => f.Title));
            d.Page.Features[0].OpenCommand.Execute(null);
            Assert.Same(phone, d.SelectedNode);

            var page = d.Page!;
            Assert.Equal("Phone", page.Title);
            Assert.Contains("sofa", page.Goals.Markdown);
            Assert.Contains("Tablets", page.NotDoing.Markdown);
            Assert.Contains("thin client", page.Design.Markdown);
            Assert.Contains("1 question open", page.Summary);
            Assert.Contains("0 of 2 cards done", page.Summary);
            var q = Assert.Single(page.Questions);
            Assert.True(q.Asking);
            Assert.Equal("Tailscale (recommended)", q.Options[0].Label);

            // a made card's row: its column and live agent from the board; a proposed one: its score and its blocker
            Assert.Equal(2, page.Cards.Count);
            var row = page.Cards[0];
            Assert.True(row.Made);
            Assert.Equal("In Progress", row.Column);
            Assert.StartsWith("✦ ", row.Agent);
            var proposed = page.Cards[1];
            Assert.False(proposed.Made);
            Assert.Equal(3, proposed.Quality!.Stars);
            Assert.Equal($"waits on {made.Id}", proposed.Blockers);
            await AvaloniaSettle();
            app.Shot("design");
            // the page fills the width it's given, with no empty strip on the right of a wide window (QA, 2026-11-22)
            var scroll = app.Window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PageScroll");
            var parts = app.Window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PageParts");
            // (left-aligned, the parts shrank to their widest text, ~460 px of a 640 px viewport beside the planner's chat)
            Assert.True(scroll.Viewport.Width > 500, $"the page's viewport is {scroll.Viewport.Width} wide at a 1400 px window");
            Assert.InRange(scroll.Viewport.Width - parts.Bounds.Width, 0, 40); // only the 18 px margins either side

            // tapping an option writes the Answer line into the plan, and the page shows it answered
            q.Options[0].PickCommand.Execute(null);
            Assert.Contains($"**Answer:** Tailscale ({Environment.UserName}, {DateTime.Now:yyyy-MM-dd})", File.ReadAllText(phoneFile));
            await app.Until(() => d.Page is { } p && p != page && p.Questions[0].Answered, "the page read again with the answer");
            Assert.False(d.Page!.Questions[0].Asking);
            Assert.Contains("not committed", d.Status);

            // a part edited in place: only its lines change
            var before = File.ReadAllText(phoneFile);
            var goals = d.Page.Goals;
            goals.EditCommand.Execute(null);
            Assert.Equal("- Answer agents from the sofa.", goals.Draft);
            goals.Draft = "- Answer agents from anywhere.\n- Never miss a decision.";
            goals.SaveCommand.Execute(null);
            Assert.Equal(before.Replace("- Answer agents from the sofa.", "- Answer agents from anywhere.\r\n- Never miss a decision."), File.ReadAllText(phoneFile));
            await app.Until(() => d.Page!.Goals.Markdown.Contains("Never miss"), "the page read again with the new goals");
            await AvaloniaSettle();
            app.Shot("design-answered");

            // the made card's row opens its panel on the board
            d.Page!.Cards[0].OpenCommand.Execute(null);
            Assert.Equal(Section.Board, app.Project.Section);
            Assert.Equal("Remote access", app.Board.Editor?.Title);
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); } // the Sandbox default: unset, fake claude turns run 60 s
    }

    /// <summary>WOR-0178: a card made from a plan gets the plan's path and the rule for a choice the plan didn't settle;
    /// a choice an agent wrote into the plan shows with Keep and Change, Change sends its card back with the new answer,
    /// and the Log lists a finished card's report, all without the page writing anything but the answer line.</summary>
    [AvaloniaFact]
    public async Task A_choice_an_agent_wrote_into_the_plan_shows_with_keep_and_change_and_the_log_lists_reports()
    {
        using var app = DargApp.Make();
        var first = app.Card("Remote access", "To Do");
        var second = app.Card("Pairing", "QA Ready");
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        var file = Path.Combine(design, "phone.md");
        File.WriteAllText(file, FeaturePlan(first.Id));
        // the second made card too, so the plan holds both
        File.WriteAllText(file, File.ReadAllText(file).Replace("### Pairing in the app", $"### {second.Id}: Pairing in the app"));
        await app.Start();

        // the card agent is told which plan its card came from and what to do with a choice the plan left open
        await app.Api("work-on", new { project = "proj", card = first.Id });
        await app.Until(() => app.Prompts().Any(p => p.Contains("Work on this card")), "the card agent's prompt");
        var prompt = app.Prompts().First(p => p.Contains("Work on this card"));
        Assert.Contains("docs/design/phone.md", prompt);   // relative to the repo, so the agent edits its own worktree's copy
        Assert.DoesNotContain(file.Replace('\\', '/'), prompt);
        Assert.Contains("decided by agent, " + first.Id, prompt);

        // the agent writes its choice into the plan's Questions
        var agentsAnswer = $"**Answer:** Tailscale (decided by agent, {second.Id}, 2026-11-20)";
        File.WriteAllText(file, File.ReadAllText(file).TrimEnd() + "\n\n## Questions\n\n### Which relay?\n- **Tailscale** (recommended): none\n- **Own server**: more work\n\n" + agentsAnswer + "\n");
        // a finished card's report is in the Log
        var reports = ReportStore.FolderFor("proj");
        Directory.CreateDirectory(reports);
        File.WriteAllText(Path.Combine(reports, $"20261120-1200-{second.Id}.md"),
            $"---\ncard: {second.Id}\ntitle: Pairing in the app\nagent: a1\ncommits: abc1234\ndate: 2026-11-20 12:00\nread: false\n---\n## Asked\nx\n## Done\ny\n## Left\nNone\n");

        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Page is { } p && p.Title == "Phone" && p.Questions.Any(q => q.ByAgent), "the decided question on the page");
        var q = d.Page!.Questions.Single(x => x.ByAgent);
        Assert.Equal("Tailscale", q.Chosen);
        Assert.Equal(second.Id, q.DecidedCard);
        Assert.Contains(second.Id, q.DecidedText);
        Assert.Contains(d.Page.Log, r => r.Text.StartsWith(second.Id));
        var before = File.ReadAllText(file);
        d.Page.Log[0].OpenCommand.Execute(null);   // opening a report writes nothing to the plan
        Assert.Equal(before, File.ReadAllText(file));

        // Change: the card goes back to its agent with the new answer as notes
        Assert.Equal("Change", (string)Views.DesignView.ChangeLabel.Convert(q.Changing, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)!);
        q.ChangeCommand.Execute(null);
        Assert.Equal("Cancel", (string)Views.DesignView.ChangeLabel.Convert(q.Changing, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)!);   // never a second Keep
        q.Options.First(o => o.Label.StartsWith("Own server")).PickCommand.Execute(null);
        Assert.Contains("Own server", File.ReadAllText(file));
        await app.Until(() => app.Prompts().Any(p => p.Contains("Own server") && p.Contains("Which relay?")), "the card sent back with the new answer");
        Assert.Contains("sent back", d.Status);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Keeping_an_agents_choice_puts_the_persons_name_on_it_and_sends_nothing_back()
    {
        using var app = DargApp.Make();
        var card = app.Card("Remote access", "QA Ready");
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        var file = Path.Combine(design, "phone.md");
        File.WriteAllText(file, FeaturePlan(card.Id) + $"\n\n## Questions\n\n### Which relay?\n- **Tailscale**: none\n- **Own server**: more\n\n**Answer:** Tailscale (decided by agent, {card.Id}, 2026-11-20)\n");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Page is { } p && p.Questions.Any(x => x.ByAgent), "the decided question");
        var calls = app.ClaudeCalls().Count;
        d.Page!.Questions.Single(x => x.ByAgent).KeepCommand.Execute(null);
        await app.Until(() => d.Page is { } p && p.Questions.All(x => !x.ByAgent), "the question kept");
        Assert.Contains($"**Answer:** Tailscale ({Environment.UserName}, {DateTime.Now:yyyy-MM-dd})", File.ReadAllText(file));
        Assert.DoesNotContain("decided by agent", File.ReadAllText(file));
        Assert.Equal(calls, app.ClaudeCalls().Count);
    }

    [AvaloniaFact]
    public async Task With_no_plans_the_page_says_where_they_go()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Loaded, "the plans read");
        Assert.True(d.Empty);
        Assert.Contains(Path.Combine(app.Docs, "design"), d.EmptyText);
        Assert.Equal("", app.Crashes());
    }

    static string AndroidPlan(string extra = "", string notifyDone = "**Done when:** a decision shows on the phone within a minute.") => $"""
        ---
        kind: feature
        status: draft
        ---
        # Android app

        ## Goals
        - Answer agents from the sofa.
        {extra}
        ## Cards

        ### Pairing in the app
        Scan the code and keep the token so a phone stays paired, and show who is paired in Settings.
        **Where:** `src/Pair.cs`
        **Done when:** a paired phone keeps its token across restarts.
        **Blocked by:** Remote access

        ### Remote access
        TLS on its own port and a token per phone, so a phone on the home network can call the API and anything else is turned away.
        **Where:** `src/Remote.cs`
        **Done when:** a paired phone calls the API and one without a token is refused.

        ### Notifications
        Push what waits on me to the phone: a question from an agent, a card needing a decision, or a turn that ran long.
        **Where:** `src/Push.cs`
        {notifyDone}
        """.ReplaceLineEndings("\r\n");

    [AvaloniaFact]
    public async Task Plan_cards_makes_the_story_board_with_blockers_and_the_plan_holds_the_ids()
    {
        // WOR-0177: three cards, one blocked by another, made through the API in one call; a second call makes nothing
        using var app = DargApp.Make();
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        var file = Path.Combine(design, "android.md");
        File.WriteAllText(file, AndroidPlan());
        await app.Start();

        var made = System.Text.Json.Nodes.JsonNode.Parse(await app.Api("plan-cards", new { project = "proj", plan = "android" }))!;
        Assert.Equal("Android app", (string?)made["board"]);
        Assert.Equal(3, made["made"]!.AsArray().Count);
        var folder = Path.Combine(app.Boards, "Android app");
        var cards = BoardStore.ReadCards(folder).ToDictionary(c => c.Title);
        Assert.All(cards.Values, c => Assert.StartsWith("AND-", c.Id));
        Assert.Equal([cards["Remote access"].Id], cards["Pairing in the app"].BlockedBy);
        Assert.Empty(cards["Remote access"].BlockedBy);
        Assert.Equal(DargApp.Columns, BoardStore.Load(folder).Columns.Select(c => c.Name));
        var plan = PlanDoc.Load(file);
        Assert.Equal("agreed", plan.Status);
        Assert.Equal("Android app", plan.Board);
        Assert.Equal(cards.Values.Select(c => c.Id).Order(), plan.Cards.Select(c => c.Id).Order());

        // the Board section sees the new board, and the page has no card left to make
        await app.Until(() => app.Board.Boards.Contains("Android app"), "the Board section reading the new board");
        await app.Until(() => app.Project.Design!.Page is { HasProposed: false }, "the plan page reading its made cards");
        await app.Api("show", new { project = "proj", section = "Design" });
        app.Shot("plan-made");

        // again: nothing to make, and nothing deleted
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("plan-cards", new { project = "proj", plan = "android" }));
        Assert.Contains("already", ex.Message);
        Assert.Equal(3, BoardStore.ReadCards(folder).Count);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task The_make_button_makes_the_ticked_cards_after_a_second_press_on_open_questions_and_refuses_a_thin_card()
    {
        using var app = DargApp.Make();
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        var file = Path.Combine(design, "android.md");
        var question = "\n## Questions\n\n### Which port?\n- **A** (recommended): one\n- **B**: two\n";
        File.WriteAllText(file, AndroidPlan(question, notifyDone: "").ReplaceLineEndings("\n"));   // Notifications has no Done when
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Page is { Cards.Count: 3 }, "the plan page");
        var page = d.Page!;
        Assert.All(page.Cards, c => Assert.True(c.Ticked));   // all ticked to start
        Assert.Equal(3, page.MakeCount);
        Assert.Contains("Which port?", page.OpenQuestionsText);
        Assert.True(page.Cards.Single(c => c.Title == "Notifications").Quality!.Stars < 3);

        // the thin card is refused with what it lacks: nothing made, no board
        page.MakeCardsCommand.Execute(null);   // refused before the open question is even asked
        Assert.Contains("\"Notifications\"", d.Status);
        Assert.Contains("when it's done", d.Status);
        Assert.False(Directory.Exists(Path.Combine(app.Boards, "Android app")));

        // untick it: one press asks about the question, the next makes the other two
        page.Cards.Single(c => c.Title == "Notifications").Ticked = false;
        Assert.False(page.Confirming);
        Assert.Equal(2, page.MakeCount);
        page.MakeCardsCommand.Execute(null);
        Assert.Contains("still open", d.Status);
        Assert.False(Directory.Exists(Path.Combine(app.Boards, "Android app")));
        page.MakeCardsCommand.Execute(null);
        var cards = BoardStore.ReadCards(Path.Combine(app.Boards, "Android app")).ToDictionary(c => c.Title);
        Assert.Equal(["Pairing in the app", "Remote access"], cards.Keys.Order());
        Assert.Equal([cards["Remote access"].Id], cards["Pairing in the app"].BlockedBy);
        Assert.Contains("Made 2 cards", d.Status);
        await app.Until(() => d.Page is { } p && p.Cards.Count(c => c.Made) == 2 && !p.Cards.Single(c => c.Title == "Notifications").Made, "the page reading the made cards");
        Assert.Equal(0, d.Page!.MakeCount);   // the row left unticked stays unticked across the reload
        Assert.Equal("", app.Crashes());
    }

    static Task AvaloniaSettle() => DargApp.Settle(150);
}
