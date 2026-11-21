using Avalonia.Headless.XUnit;
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

    static Task AvaloniaSettle() => DargApp.Settle(150);
}
