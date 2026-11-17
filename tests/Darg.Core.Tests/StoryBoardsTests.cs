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
        Assert.Contains("- Notes wall: post-it shading; column menu", p);
        Assert.Contains("- Art (no cards yet)", p);
        Assert.EndsWith("Card:\npolaroids look real\ntape at the top", p);
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

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }
}
