using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0227: every board of a project read once, answering what the agents list asks after each reload (each card's
/// board, the done cards, whether a card is still being worked) as the per-question reads of every card file did.</summary>
public sealed class ProjectBoardsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "darg-projectboards-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _boards;

    public ProjectBoardsTests() => _boards = Directory.CreateDirectory(Path.Combine(_root, "tasks")).FullName;
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    static void MoveTo(string board, string id, string column) =>
        BoardStore.Move(board, id, BoardStore.Load(board).Columns.First(c => c.Name == column).Id);

    [Fact]
    public void Answers_each_cards_board_the_done_cards_and_what_is_being_worked_across_boards()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", ["To Do", "In Progress", "QA Ready", "Done"], "T");
        var other = BoardStore.CreateBoard(_boards, "Other", ["To Do", "In Progress", "Shipped"], "O");
        var working = BoardStore.Create(main, "being worked", "");
        var waiting = BoardStore.Create(main, "waiting", "");
        var done = BoardStore.Create(main, "done on main", "");
        var shipped = BoardStore.Create(other, "shipped on other", "");
        var completed = BoardStore.Create(other, "completed on other", "");
        MoveTo(main, working.Id, "In Progress");
        MoveTo(main, done.Id, "Done");
        MoveTo(other, shipped.Id, "Shipped");
        BoardStore.Move(other, completed.Id, Board.Completed);

        var all = ProjectBoards.Read(_boards);

        Assert.Equal(["Main", "Other"], all.Boards.Select(b => b.Name));
        var map = all.CardBoards();
        Assert.Equal("Main", map[working.Id]);
        Assert.Equal("Other", map[shipped.Id.ToLowerInvariant()]);
        Assert.Equal(5, map.Count);
        map.Clear(); // a copy: the next caller still gets every card
        Assert.Equal(5, all.CardBoards().Count);

        Assert.Equal(new[] { done.Id, shipped.Id }.Order(), all.DoneCards().Select(d => d.Card.Id).Order());
        Assert.Equal(new[] { completed.Id, done.Id, shipped.Id }.Order(), all.DoneCards(completedToo: true).Select(d => d.Card.Id).Order());
        Assert.Equal(Path.GetFullPath(Path.Combine(main, "items", done.File)), all.DoneCards().Single(d => d.Card.Id == done.Id).File);
        Assert.True(all.DoneCardIds().Contains(completed.Id.ToLowerInvariant()));
        Assert.False(all.DoneCardIds().Contains(waiting.Id));

        Assert.True(all.StillWorked(working.Id));
        Assert.False(all.StillWorked(waiting.Id));
        Assert.Equal(CutOffTurn.StillWorked(_boards, working.Id), all.StillWorked(working.Id));
    }

    [Fact]
    public void A_board_already_read_is_taken_as_it_is_and_an_unreadable_one_is_left_out()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", ["To Do", "Done"], "T");
        var broken = BoardStore.CreateBoard(_boards, "Broken", ["To Do", "Done"], "B");
        var card = BoardStore.Create(main, "on main", "");
        File.WriteAllText(Path.Combine(broken, "board.json"), "{ not json");
        var mainRead = BoardStore.Load(main);
        BoardStore.Create(main, "made after the row read its board", "");

        var all = ProjectBoards.Read(_boards, new Dictionary<string, Board> { [ProjectBoards.Key(main + Path.DirectorySeparatorChar)] = mainRead });

        Assert.Same(mainRead, all.Boards.Single());
        Assert.Equal([card.Id], all.CardBoards().Keys);
        Assert.Empty(ProjectBoards.Read(null).Boards);
        Assert.Empty(ProjectBoards.Empty.DoneCardIds());
    }
}
