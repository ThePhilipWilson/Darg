using Darg.Core;

namespace Darg.Core.Tests;

public sealed class StoryBoardsTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("wb-story-").FullName;
    static readonly string[] Boards = ["Art pipeline", "Main", "Notes wall"];

    [Theory]
    [InlineData(new[] { "Art", "Main", "Inbox" }, "Inbox")]
    [InlineData(new[] { "Art", "main" }, "main")]
    [InlineData(new[] { "Art", "Backlog" }, "Backlog")]
    [InlineData(new[] { "Art", "Code" }, "Art")]
    public void The_inbox_is_named_Inbox_Main_or_Backlog_else_the_first(string[] boards, string expected) =>
        Assert.Equal(expected, StoryBoards.Inbox(boards));

    [Fact]
    public void No_boards_no_inbox() => Assert.Null(StoryBoards.Inbox([]));

    // WOR-0219: a default board set from a tab's menu wins over the name guess while it's there
    [Theory]
    [InlineData(new[] { "Art", "Main", "Inbox" }, "Art", "Art")]
    [InlineData(new[] { "Art", "Main" }, "art", "Art")]          // as the board is named
    [InlineData(new[] { "Art", "Main" }, "Sound", "Main")]       // deleted or renamed: back to the name guess
    [InlineData(new[] { "Art", "Code" }, "Gone", "Art")]
    [InlineData(new[] { "Art", "Main" }, null, "Main")]          // none set: as before
    [InlineData(new[] { "Art", "Main" }, " ", "Main")]
    public void The_default_board_is_the_inbox_while_it_exists(string[] boards, string? preferred, string expected) =>
        Assert.Equal(expected, StoryBoards.Inbox(boards, preferred));

    [Fact]
    public void The_default_board_is_kept_per_project_beside_the_shown_boards()
    {
        Assert.Null(DefaultBoard.Load("p", _root));
        Assert.True(DefaultBoard.Save("p", "Merge-safe boards", _root));
        Assert.Equal("Merge-safe boards", DefaultBoard.Load("p", _root));
        Assert.Null(DefaultBoard.Load("other", _root));
        Assert.Equal(Path.Combine(Path.GetDirectoryName(ShownBoards.FileFor("p", _root))!, "default-board"), DefaultBoard.FileFor("p", _root));
        Assert.True(DefaultBoard.Save("p", null, _root));
        Assert.Null(DefaultBoard.Load("p", _root));
        Assert.True(DefaultBoard.Save("p", null, _root)); // clearing none is fine
    }

    [Fact]
    public async Task Placing_from_a_default_board_offers_the_other_boards_Main_included()
    {
        var main = Board("Main");
        var art = Board("Art");
        var card = BoardStore.Create(art, "fix the crash on start");
        IReadOnlyList<(string Name, IReadOnlyList<string> Titles)>? seen = null;
        Assert.Null(await StoryBoards.Place(_root, "Art", card.Id, suggest: (_, s) => { seen = s; return Task.FromResult<string?>(null); }));
        Assert.Equal(["Main"], seen!.Select(s => s.Name));
        Assert.Contains(BoardStore.ReadCards(art), c => c.Id == card.Id); // unsure: it stays on the default
        Assert.Empty(BoardStore.ReadCards(main));
    }

    // WOR-0213: the board tabs' kept order
    [Fact]
    public void Board_order_puts_kept_ones_first_then_new_ones_A_to_Z_and_drops_gone_ones() =>
        Assert.Equal(["Site", "main", "Art", "Zed"], BoardOrder.Arrange(["Zed", "Art", "main", "Site"], ["Gone", "Site", "Main"]));

    [Fact]
    public void Board_order_with_none_kept_is_A_to_Z() =>
        Assert.Equal(["art", "Main", "Site"], BoardOrder.Arrange(["Site", "Main", "art"], []));

    [Fact]
    public void Board_order_is_saved_and_read_back_per_project()
    {
        BoardOrder.Save("p", ["Site", "Main"], _root);
        Assert.Equal(["Site", "Main"], BoardOrder.Load("p", _root));
        Assert.Empty(BoardOrder.Load("other", _root));
        Assert.Equal(Path.GetDirectoryName(ShownBoards.FileFor("p", _root)), Path.GetDirectoryName(BoardOrder.FileFor("p", _root)));
    }

    [Fact]
    public void Boards_by_age_are_oldest_folder_first_ties_A_to_Z()
    {
        var folder = Path.Combine(_root, "boards");
        var t = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var (name, days) in new[] { ("New", 9), ("Old", 1), ("Beta", 5), ("Alpha", 5) })
        {
            Directory.CreateDirectory(Path.Combine(folder, name));
            Directory.SetCreationTimeUtc(Path.Combine(folder, name), t.AddDays(days));
        }
        Assert.Equal(["Old", "Alpha", "Beta", "New", "Missing"], BoardOrder.ByAge(folder, ["New", "Missing", "Beta", "Old", "Alpha"]));
    }

    [Theory] // replies a model gives, and the board kept
    [InlineData("Art pipeline", "Art pipeline")]
    [InlineData("art pipeline", "Art pipeline")]
    [InlineData("\"Notes wall\"", "Notes wall")]
    [InlineData("**Notes wall**", "Notes wall")]
    [InlineData("Board: Notes wall.", "Notes wall")]
    [InlineData("- Notes wall", "Notes wall")]
    [InlineData("`Notes wall`\n", "Notes wall")]
    [InlineData("```\nNotes wall\n```", "Notes wall")]
    public void Takes_a_board_from_the_list_and_strips_its_wrapping(string reply, string expected) =>
        Assert.Equal(expected, StoryBoards.Pick(reply, Boards));

    [Theory]
    [InlineData("none")]
    [InlineData("None.")]
    [InlineData("N/A")]
    [InlineData("Board: none")]                          // WOR-0219's prompt names boards after "Board:"
    [InlineData("post-it shading")]                      // a card's title, not a board
    [InlineData("no board fits")]
    [InlineData("Art")]                                  // not a board on the list
    [InlineData("Code review")]                          // made up
    [InlineData("It belongs on Notes wall.")]            // talk around it
    [InlineData("Notes wall\nbecause it's about notes")] // a second line
    [InlineData("Notes wall or Art pipeline")]
    [InlineData("")]
    public void Refuses_none_and_anything_not_a_board(string reply) => Assert.Null(StoryBoards.Pick(reply, Boards));

    [Fact]
    public void The_prompt_names_each_board_with_its_cards_and_carries_the_card()
    {
        var card = new Card("WOR-0001", "polaroids look real", "Backlog", "", "", [], "x.md", "tape at the top");
        var p = StoryBoards.Prompt(card, [("Notes wall", ["post-it shading", "column menu"]), ("Art", [])]);
        Assert.Contains("Board: Notes wall\n  Its cards: post-it shading; column menu", p);
        Assert.Contains("Board: Art\n  Its cards: none yet", p);
        Assert.EndsWith("New card:\nTitle: polaroids look real\nDescription: tape at the top", p);
        // WOR-0219: strict, and only a board's name back
        Assert.Contains("shares a word or a topic", p);
        Assert.Contains("Never a card's title", p);
    }

    string Board(string name) => BoardStore.CreateBoard(_root, name, ["To Do", "In Progress", "Done"], "WOR");

    [Fact]
    public async Task Moves_a_new_card_from_the_inbox_to_the_board_picked()
    {
        var main = Board("Main");
        var wall = Board("Notes wall");
        BoardStore.Create(wall, "post-it shading");
        var card = BoardStore.Create(main, "polaroid tape");
        IReadOnlyList<(string Name, IReadOnlyList<string> Titles)>? seen = null;
        var to = await StoryBoards.Place(_root, "Main", card.Id, suggest: (_, s) => { seen = s; return Task.FromResult<string?>("Notes wall"); });
        Assert.Equal("Notes wall", to);
        Assert.Contains(BoardStore.ReadCards(wall), c => c.Id == card.Id);
        Assert.DoesNotContain(BoardStore.ReadCards(main), c => c.Id == card.Id);
        Assert.Equal(["post-it shading"], Assert.Single(seen!).Titles); // the inbox itself isn't offered
    }

    [Fact]
    public async Task Leaves_it_when_none_fits_or_there_is_no_other_board()
    {
        var main = Board("Main");
        var card = BoardStore.Create(main, "polaroid tape");
        var asked = false;
        Assert.Null(await StoryBoards.Place(_root, "Main", card.Id, suggest: (_, _) => { asked = true; return Task.FromResult<string?>("Main"); }));
        Assert.False(asked); // no story board to go to: Haiku isn't asked
        Board("Art");
        Assert.Null(await StoryBoards.Place(_root, "Main", card.Id, suggest: (_, _) => Task.FromResult<string?>(null)));
        Assert.Contains(BoardStore.ReadCards(main), c => c.Id == card.Id);
    }

    [Fact]
    public async Task Leaves_a_card_picked_up_while_Haiku_thought()
    {
        var main = Board("Main");
        Board("Art");
        var card = BoardStore.Create(main, "paint the logo");
        var to = await StoryBoards.Place(_root, "Main", card.Id, suggest: (_, _) =>
        {
            BoardStore.Move(main, card.Id, "c2"); // someone started on it
            return Task.FromResult<string?>("Art");
        });
        Assert.Null(to);
        Assert.Contains(BoardStore.ReadCards(main), c => c.Id == card.Id);
    }

    [Fact]
    public async Task Leaves_a_card_an_agent_holds()
    {
        var main = Board("Main");
        Board("Art");
        var card = BoardStore.Create(main, "paint the logo");
        Assert.Null(await StoryBoards.Place(_root, "Main", card.Id, busy: _ => true, suggest: (_, _) => Task.FromResult<string?>("Art")));
        Assert.Contains(BoardStore.ReadCards(main), c => c.Id == card.Id);
    }

    [Fact]
    public void Shown_boards_are_kept_per_project_in_the_instance()
    {
        Assert.Empty(ShownBoards.Load("workbench", _root));
        ShownBoards.Save("workbench", ["Art", "Notes wall", "art"], _root);
        Assert.Equal(["Art", "Notes wall"], ShownBoards.Load("workbench", _root));
        Assert.Equal(Path.Combine(_root, "projects", "workbench", "shown-boards"), ShownBoards.FileFor("workbench", _root));
    }

    [Fact]
    public void Rows_not_set_share_the_space_and_more_than_two_fit() // WOR-0160
    {
        Assert.Equal([700], BoardRowHeights.Fit(700, [500]));            // one row fills, set or not
        Assert.Equal([350, 350], BoardRowHeights.Fit(700, [null, null]));
        Assert.Equal([233, 233, 233], BoardRowHeights.Fit(700, [null, null, null])); // three on a 700 high screen
        Assert.Equal([180, 180, 180, 180, 180], BoardRowHeights.Fit(700, [null, null, null, null, null])); // past that they scroll
    }

    [Fact]
    public void A_dragged_row_keeps_its_height_and_the_rest_share_what_is_left()
    {
        Assert.Equal([400, 180, 180], BoardRowHeights.Fit(700, [400, null, null])); // never shared below FitMin: they scroll
        Assert.Equal([200, 250, 250], BoardRowHeights.Fit(700, [200, null, null]));
        Assert.Equal([140, 560], BoardRowHeights.Fit(700, [20, 100]));   // no smaller than Min; every row set, the last takes the spare
        Assert.Equal([900, 900], BoardRowHeights.Fit(700, [900, 900])); // bigger than the screen: they scroll
    }

    [Fact]
    public void Row_heights_are_kept_per_project_in_the_instance()
    {
        Assert.Empty(BoardRowHeights.Load("workbench", _root));
        BoardRowHeights.Save("workbench", new Dictionary<string, double> { ["Art pipeline"] = 312.4, ["Main"] = 500 }, _root);
        var back = BoardRowHeights.Load("workbench", _root);
        Assert.Equal(312, back["art pipeline"]);
        Assert.Equal(500, back["Main"]);
        Assert.Equal(Path.Combine(_root, "projects", "workbench", "board-heights"), BoardRowHeights.FileFor("workbench", _root));
    }

    // WOR-0219: this project's story boards as they stood on 2026-11-25, when a new card was put on the wrong one
    static readonly (string Name, IReadOnlyList<string> Titles)[] DargStories =
    [
        ("Agents and Fettle chats travel", ["Two machines in one test an agent carried on from the other", "Card branches are pushed at turn end and checked out on Carry on",
            "The Agents list and Fettle's chats follow changes on disk", "Agents show which machine runs them and only that machine runs them",
            "The Claude conversation is copied into .darg and back before a resume", "Preview pictures go with the agent that shows them",
            "Saved agents and project Fettle chats move into .darg", "Resume that fails starts fresh from the transcript", "Agent records store paths as tokens that read on both machines"]),
        ("Android", ["Android pairing and TLS in the phone app", "Android Needs you screen agent asks and card decisions answered", "Android theming, offline behaviour and release",
            "Android notes, docs and reports reading views", "Android Fettle chat on mobile", "Android push notifications for decisions and finished agents",
            "Android agents list and transcript viewer", "Android board view with touch-friendly cards", "Android project scaffold and app shell",
            "Android remote access to the desktop Darg API", "Android decide the approach and scope"]),
        ("Auto-sync", ["Sync in the Darg API", "The Sync panel conflicts side by side, and the last syncs", "Sync line in the footer", "Sync around making a card, and on close",
            "Sync on open, on switching, on focus, on a timer and after commits", "A sync round that runs unattended", "Darg's readers skip .mine files", "Keep both sides of a sync conflict"]),
        ("darg on its own branch", ["Docs and CLAUDE.md for .darg on its own branch", "Move vault boards and notes into the repo's .darg", "A checkout in the main checkout can't lose .darg",
            "Two machines' first syncs keep every file", "Sync now in the footer and the API", "A sync never commits a mass delete", "One sync round for the darg branch",
            "Pick where a repo's darg branch pushes", "Put .darg on a darg branch and bring today's files in", "Take the force-added .darg off Darg's branches"]),
        ("Leaving check", ["The leaving-check API command", "Check on close", "The Leaving check panel and button", "Live agents in the check", "Leaving check for .darg",
            "Push what the commit rules allow", "Leaving check report"]),
        ("Merge-safe boards", ["Agents stop editing board.json", "Flag two cards with one id on a board read", "Renumber a card and everything that points at it",
            "Find id clashes between fetch and merge", "Three-way merge of board.json", "Bring existing boards' order into Rank lines", "Card order from Rank lines",
            "Fractional rank keys", "Next id from every card file in the project"]),
        ("Per-user settings sync", ["Keep the settings repo synced", "Connect the settings repo from Settings", "Apply synced settings that change while Darg is open",
            "Fettle chats with no project live in synced chats", "Open on the project and section used last, on either machine", "Setup offers to clone projects from the other machine",
            "A synced project list with each repo's remote", "Phone notification settings move to synced ntfy.json", "Move the settings files into a synced folder"]),
        ("Project settings in the repo", ["Update CLAUDE.md and the docs for project.json", "Commands that belong to one OS", "Reload a project when its project.json changes",
            "Keep a copy of project.json when the first repo is missing", "Offer to clone a repo that is missing here", "Second machine integration test",
            "Adding a folder keeps the project's id", "Setup edits the shared and machine parts separately", "Move today's settings out of instance.json on first start",
            "Commit rules keyed by repo name, docs and boards", "Load and save a project's settings through the repo", "Define the project.json file and read and write it"]),
        ("Resume spike", ["State the rule for the agents feature", "Run the Mac step and write what the agent makes of Windows paths",
            "Run the spike on this PC and record what has to be copied", "Spike script make a session, copy it under another path, resume it"]),
    ];

    /// <summary>Real Haiku calls (DARG_LIVE=1): a bug about the Work All count fits no story and stays on the default
    /// board; a card plainly about a story still goes there.</summary>
    [Theory]
    [InlineData("In Progress Work All count should show workable backlog tickets",
        "> The In Progress column's Work All button should show the number of tickets in the backlog that can be worked on (not blocked by open cards, no live agent). On the Main board it currently shows 0 even though there are workable backlog cards.",
        null)]
    [InlineData("Story cards should link out to the task cards they contain",
        "> Story tickets currently look like a tag, with no functional difference from other cards. They should link out to the task cards they contain (e.g. list the contained cards on the story card, each clickable to open it), so a story is visibly and usefully a container.",
        null)]
    [InlineData("Show why a card is in Needs more work at the top of its pane",
        "> When a card is in Needs more work (sent back from QA), the reason is buried in the QA notes at the bottom of the card, so finding out why it was sent back means reading the whole file. Show the reason at the top of the card's inspect panel.",
        null)]
    [InlineData("Three-way merge of board.json conflicts",
        "When two machines both change board.json, a pull leaves a git conflict in it. Merge the two sides' card lists three ways instead.",
        "Merge-safe boards")]
    [InlineData("Android: swipe a card to move it to the next list",
        "On the phone's board, swiping a card sideways should move it to the next list, as the move sheet does now.",
        "Android")]
    public async Task A_live_model_moves_only_cards_plainly_part_of_a_story(string title, string description, string? expected)
    {
        if (Environment.GetEnvironmentVariable("DARG_LIVE") != "1") return;
        var card = new Card("WOR-0900", title, "Backlog", "", "Bug", [], "x.md", description);
        for (var i = 0; i < 3; i++) // a model's pick varies: every try must agree
        {
            var to = await StoryBoards.Suggest(card, DargStories);
            Console.WriteLine($"{title}: {to ?? "none"}");
            Assert.Equal(expected, to);
        }
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }
}
