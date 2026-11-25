using Avalonia;
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
            Assert.Contains("1 of 2 cards made, 0 done", page.Summary);
            Assert.Equal("building", page.State);   // agreed, with a card made
            // the open question waits on the person: it's in Needs you, which the progress line leaves to it (WOR-0200)
            Assert.Equal(["question"], page.Needs.Select(n => n.Kind));
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
            // WOR-0199: committed at once where the plan is in a repo (PlanCommitTests); this docs folder is in none of its own
            Assert.Contains("Saved to phone.md.", d.Status);

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

    /// <summary>WOR-0189: a reload asked for while one is reading used to be dropped, leaving the page on the old file.</summary>
    [AvaloniaFact]
    public async Task A_reload_asked_for_while_one_reads_runs_once_more_and_shows_the_newer_file()
    {
        using var app = DargApp.Make();
        var card = app.Card("Remote access", "In Progress");
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        var file = Path.Combine(design, "phone.md");
        File.WriteAllText(file, FeaturePlan(card.Id));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Page is not null, "the plan page");
        using var read = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reads = 0;
        d.AfterRead = () => { if (Interlocked.Increment(ref reads) == 1) { read.Set(); release.Wait(TimeSpan.FromSeconds(20)); } };
        d.Reload();
        Assert.True(await Task.Run(() => read.Wait(TimeSpan.FromSeconds(20))), "the first reload reading");
        File.WriteAllText(file, FeaturePlan(card.Id).Replace("A thin client over the API.", "A THICK client over the API."));
        d.Reload(); // asked for while the first still holds the old read
        release.Set();
        await app.Until(() => d.Page is { } p && p.Design.Markdown.Contains("THICK"), "the page showing the second state");
        Assert.True(Volatile.Read(ref reads) >= 2); // the file watcher may add a third
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

        // WOR-0201: the thin card is marked with what it lacks, before any press, and the button offers the other two
        var thin = page.Cards.Single(c => c.Title == "Notifications");
        Assert.True(thin.HasCantMake);
        Assert.Contains("when it's done", thin.CantMakeText);
        Assert.Equal("Make the 2 that can be made", page.MakeText);
        Assert.Contains("1 of the 3 ticked can't be made", page.CantText);
        Assert.False(Directory.Exists(Path.Combine(app.Boards, "Android app")));

        // the API, asked for all three, makes nothing and names it
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("plan-cards", new { project = "proj", plan = "android", confirm = "true" }));
        Assert.Contains("\"Notifications\"", refused.Message);
        Assert.Contains("ready: true", refused.Message);
        Assert.False(Directory.Exists(Path.Combine(app.Boards, "Android app")));

        // untick it: one press asks about the question, the next makes the other two
        thin.Ticked = false;
        Assert.False(thin.HasCantMake);
        Assert.False(page.Confirming);
        Assert.Equal(2, page.MakeCount);
        Assert.Equal("Make 2 cards", page.MakeText);
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

    /// <summary>WOR-0200: what waits on the person in a plan (an open question, an answer an agent chose, a made card
    /// waiting on a decision or in QA, the planner asking) shows as a Needs you strip at the top of its page and of its
    /// project's, each row landing on what it names, and as a count on its row in the tree that the project's adds up;
    /// the progress line says the plan's state, its cards made and done and what its planner is doing.</summary>
    [AvaloniaFact]
    public async Task Needs_you_lists_what_waits_on_the_person_lands_on_each_and_the_tree_counts_it()
    {
        using var app = DargApp.Make();
        var asks = app.Card("Remote access", BoardStore.AttentionName,
            "TLS on its own port.\n\n## Decision\n\n**Question:** Ship it on port 47818?\n\n" +
            "- **Yes** (recommended) → done: it's free here.\n- **Pick another** → back: say which.\n\n**Context:** nothing else uses it.\n");
        var check = app.Card("Pairing", "QA Ready");
        var quietCard = app.Card("Quiet work", "To Do");
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        File.WriteAllText(Path.Combine(design, "proj.md"), ProjectPlan());
        var phoneFile = Path.Combine(design, "phone.md");
        File.WriteAllText(phoneFile, $"""
            ---
            kind: feature
            status: agreed
            board: Main
            parent: proj
            ---
            # Phone

            ## Questions

            ### How does the phone reach the PC?
            - **Tailscale** (recommended): nothing goes through anyone else's server.
            - **Home network only**: simplest.

            ### Which relay?
            - **Tailscale**: none
            - **Own server**: more work

            **Answer:** Tailscale (decided by agent, {check.Id}, 2026-11-20)

            ## Cards

            ### {asks.Id}: Remote access
            TLS on its own port.

            ### {check.Id}: Pairing
            Scan the code.
            """);
        File.WriteAllText(Path.Combine(design, "quiet.md"), $"---\nkind: feature\nstatus: agreed\nboard: Main\n---\n# Quiet\n\n## Cards\n\n### {quietCard.Id}: Quiet work\nNothing waits.\n");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Tree.Count == 2 && d.Tree.Any(n => n.Children.Count == 1), "the plan tree");
        PlanNode Node(string title) => d.Tree.SelectMany(n => n.Children.Prepend(n)).Single(n => n.Title == title);

        // the tree: Phone's four, and the project's count includes them
        Assert.Equal(4, Node("Phone").Needs);
        Assert.Equal(4, Node("Proj").Needs);
        Assert.Equal(0, Node("Quiet").Needs);
        Assert.False(Node("Quiet").HasNeeds);

        // Phone's page: the strip lists all four in order, and draws them
        d.SelectedNode = Node("Phone");
        var page = d.Page!;
        Assert.Equal(["question", "agent's choice", "decision", "to check"], page.Needs.Select(n => n.Kind));
        Assert.Equal("How does the phone reach the PC?", page.Needs[0].Text);
        Assert.Contains("chose \"Tailscale\" for \"Which relay?\"", page.Needs[1].Text);
        Assert.Equal($"{asks.Id} Remote access asks: Ship it on port 47818?", page.Needs[2].Text);
        Assert.Contains($"{check.Id} Pairing is in QA Ready", page.Needs[3].Text);
        Assert.Equal("Needs you · 4", page.NeedsHeading);
        Assert.Contains("2 of 2 cards made, 0 done", page.Summary);
        Assert.Equal("building", page.State);
        await AvaloniaSettle();
        var strip = app.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NeedsStrip");
        Assert.True(strip.IsEffectivelyVisible);
        Assert.Equal(4, strip.GetVisualDescendants().OfType<Button>().Count());
        app.Shot("design-needs");

        // the project's page lists Phone's items too, and a question row lands on Phone's page, lit
        d.SelectedNode = Node("Proj");
        Assert.Equal(4, d.Page!.Needs.Count);
        Assert.StartsWith("Phone: ", d.Page.Needs[0].Text);
        d.Page.Needs[0].OpenCommand.Execute(null);
        Assert.Equal("Phone", d.Page!.Title);
        Assert.True(d.Page.Questions.Single(q => q.Question == "How does the phone reach the PC?").Highlighted);
        // the agent's choice lands on its question, the light moving with it
        d.Page.Needs[1].OpenCommand.Execute(null);
        Assert.True(d.Page.Questions.Single(q => q.Question == "Which relay?").Highlighted);
        Assert.False(d.Page.Questions.Single(q => q.Question == "How does the phone reach the PC?").Highlighted);
        await AvaloniaSettle();
        app.Shot("design-needs-lit");

        // the decision row opens the card on the board
        d.Page.Needs[2].OpenCommand.Execute(null);
        Assert.Equal(Section.Board, app.Project.Section);
        Assert.Equal("Remote access", app.Board.Editor?.Title);
        await app.Api("show", new { project = "proj", section = "Design" });

        // answering the open question drops the counts
        d.SelectedNode = Node("Phone");
        d.Page!.Questions.Single(q => q.Question == "How does the phone reach the PC?").Options[0].PickCommand.Execute(null);
        await app.Until(() => Node("Proj").Needs == 3 && Node("Phone").Needs == 3, "the tree's counts after the answer");
        Assert.Equal(3, d.Page!.Needs.Count);

        // a plan with nothing waiting has no strip
        d.SelectedNode = Node("Quiet");
        Assert.False(d.Page!.HasNeeds);
        Assert.Contains("1 of 1 cards made, 0 done", d.Page.Summary);
        Assert.Equal("", d.PlannerText);
        await AvaloniaSettle();
        Assert.False(strip.IsEffectivelyVisible);
        app.Shot("design-no-needs");

        // the planner asking: the progress line says so, Needs you has it, and the tree counts it
        d.Ask = "FAKE_ASK which board should the cards go on?";
        d.AskPlannerCommand.Execute(null);
        await app.Until(() => d.PlannerText == "planner: waiting on you", "the planner waiting on the person");
        Assert.True(d.PlannerWaiting);
        await app.Until(() => Node("Quiet").Needs == 1 && d.Page is { HasNeeds: true }, "the planner's ask in Needs you and the tree");
        var ask = Assert.Single(d.Page!.Needs);
        Assert.Equal("planner", ask.Kind);
        Assert.StartsWith("The planner asks: ", ask.Text);
        ask.OpenCommand.Execute(null);
        Assert.Equal("Quiet", d.Page!.Title);
        await AvaloniaSettle();
        app.Shot("design-planner-waiting");
        Assert.Equal("", app.Crashes());
    }

    /// <summary>WOR-0200 rework: in the 300 px Plans column, a plan with a long title still shows its needs-you count and
    /// its n/m on screen; the title is cut short instead (QA found them past the column's right edge on real plans, each
    /// row laid out against the widest one).</summary>
    [AvaloniaFact]
    public async Task A_long_titled_plan_keeps_its_needs_count_and_progress_inside_the_tree()
    {
        using var app = DargApp.Make();
        var made = app.Card("Seamless machine switching first card", "To Do");
        var design = Directory.CreateDirectory(Path.Combine(app.Docs, "design")).FullName;
        File.WriteAllText(Path.Combine(design, "proj.md"), """
            ---
            kind: project
            status: draft
            ---
            # Seamless machine switching between every computer I work on, with nothing to remember

            ## Features
            - [Agents and Fettle chats travel with the work from machine to machine](phone.md): the chats go too.
            """);
        File.WriteAllText(Path.Combine(design, "phone.md"), $"""
            ---
            kind: feature
            status: draft
            board: Main
            parent: proj
            ---
            # Agents and Fettle chats travel with the work from machine to machine

            ## Questions

            ### Which machine wins when both changed?
            - **The newer** (recommended): simplest.
            - **Ask**: safest.

            ## Cards

            ### {made.Id}: Seamless machine switching first card
            The first step.

            ### Second card
            **What's wanted:** more. **Where:** here. **Done when:** done.
            """);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Design" });
        var d = app.Project.Design!;
        await app.Until(() => d.Tree.Count == 1 && d.Tree[0].Children.Count == 1 && d.Tree[0].Needs == 1, "the plan tree");
        d.Tree[0].IsExpanded = true;
        await AvaloniaSettle();
        app.Shot("design-tree-long-titles");

        var tree = app.Window.GetVisualDescendants().OfType<TreeView>().Single(t => t.ItemsSource == d.Tree);
        var shown = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
        foreach (var node in new[] { d.Tree[0], d.Tree[0].Children[0] })
        {
            var row = tree.GetVisualDescendants().OfType<DockPanel>().Single(p => p.DataContext == node && p.Parent is not DockPanel);
            var parts = row.GetVisualDescendants().OfType<TextBlock>().ToList();
            var count = parts.Single(t => t.Text == node.NeedsText);
            var progress = parts.Single(t => t.Text == node.Progress && t.Text != "");
            foreach (var part in new[] { count, progress })
            {
                Assert.True(part.IsEffectivelyVisible, $"{part.Text} on {node.Title} hidden");
                var right = part.TranslatePoint(new Point(part.Bounds.Width, 0), shown)!.Value.X;
                Assert.True(right <= shown.Viewport.Width + 0.5,
                    $"\"{part.Text}\" on \"{node.Title}\" ends at {right:0} px, past the tree's {shown.Viewport.Width:0} px");
            }
            Assert.StartsWith(node.Title + "\n", node.Tip);   // the title cut short is there in full on hover
        }
        Assert.Equal("", app.Crashes());
    }

    static Task AvaloniaSettle() => DargApp.Settle(150);
}
