using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>AND-0004: a project's boards as the phone draws and changes them, column by column.</summary>
public sealed class BoardLayoutTests : IDisposable
{
    // the boards get a folder of their own: BoardStore.Create numbers past every board beside it (WOR-0127)
    readonly string _root = Directory.CreateTempSubdirectory("darg-layout-").FullName;
    readonly string _boards;
    static readonly string[] Columns = ["To Do", "In Progress", "QA Ready", BoardStore.AttentionName, "needs more work", "Done"];

    public BoardLayoutTests() => _boards = Directory.CreateDirectory(Path.Combine(_root, "tasks")).FullName;
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void Every_list_shows_in_order_with_its_kind_empty_ones_too_and_the_inbox_is_marked()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", Columns, "T");
        BoardStore.CreateBoard(_boards, "Art", ["Doing", "Shipped"], "A");
        var a = BoardStore.Create(main, "first");
        var b = BoardStore.Create(main, "second");
        BoardStore.Move(main, a.Id, BoardStore.Load(main).Columns.Single(c => c.Name == "QA Ready").Id);

        var layouts = BoardLayouts.Read(_boards);
        Assert.Equal(["Art", "Main"], layouts.Select(l => l.Name));
        Assert.Equal([false, true], layouts.Select(l => l.Inbox));
        var m = layouts.Single(l => l.Name == "Main");
        Assert.Equal(["Backlog", "To Do", "In Progress", "QA Ready", BoardStore.AttentionName, "needs more work", "Done", "Completed"],
            m.Lists.Select(l => l.Name));
        Assert.Equal(["backlog", "", "", "qa", "attention", "rework", "done", "completed"], m.Lists.Select(l => l.Kind));
        Assert.Equal([b.Id], m.Lists[0].Cards.Select(c => c.Id));
        Assert.Equal([a.Id], m.Lists[3].Cards.Select(c => c.Id));
        Assert.Empty(m.Lists[1].Cards);
    }

    [Fact]
    public void A_column_is_named_by_id_name_or_kind()
    {
        var main = BoardStore.Load(BoardStore.CreateBoard(_boards, "Main", Columns, "T"));
        var qa = main.Columns.Single(c => c.Name == "QA Ready").Id;
        Assert.Equal(qa, BoardLayouts.ListFor(main, qa));
        Assert.Equal(qa, BoardLayouts.ListFor(main, "qa ready"));
        Assert.Equal(qa, BoardLayouts.ListFor(main, "QA"));
        Assert.Equal(main.Columns.Last().Id, BoardLayouts.ListFor(main, "done"));
        Assert.Equal(Board.Backlog, BoardLayouts.ListFor(main, "Backlog"));
        Assert.Equal(Board.Completed, BoardLayouts.ListFor(main, "completed"));
        Assert.Equal(main.Columns.Single(c => c.Name == "needs more work").Id, BoardLayouts.ListFor(main, "rework"));
        Assert.Null(BoardLayouts.ListFor(main, "Shelved"));
        Assert.Null(BoardLayouts.ListFor(main, " "));
    }

    [Fact]
    public void Moving_sets_the_status_and_the_place_and_lands_before_a_card_in_that_list_only()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", Columns, "T");
        var a = BoardStore.Create(main, "first");
        var b = BoardStore.Create(main, "second");
        var c = BoardStore.Create(main, "third");

        Assert.Equal(("Main", "In Progress"), BoardLayouts.Move(_boards, a.Id, "in progress"));
        Assert.Equal(("Main", "In Progress"), BoardLayouts.Move(_boards, b.Id.ToLowerInvariant(), "In Progress", beforeId: a.Id));
        BoardLayouts.Move(_boards, c.Id, "In Progress", beforeId: "T-9999"); // not there: the end
        var board = BoardStore.Load(main);
        var doing = board.Columns.Single(x => x.Name == "In Progress").Id;
        Assert.Equal([b.Id, a.Id, c.Id], board.Lists[doing].Select(x => x.Id));
        Assert.All(board.Lists[doing], x => Assert.Equal("In Progress", x.Status));

        BoardLayouts.Move(_boards, a.Id, "completed");
        Assert.Equal("Completed", BoardStore.ReadCards(main).Single(x => x.Id == a.Id).Status);
        Assert.Contains("- **Done:** " + DateTime.Now.ToString("yyyy-MM-dd"), File.ReadAllText(CardIndex.Find(_boards, a.Id)!.File));
    }

    [Fact]
    public void Moving_to_a_column_or_card_that_isnt_there_is_refused_and_changes_nothing()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", Columns, "T");
        var a = BoardStore.Create(main, "first");
        var before = File.ReadAllText(Path.Combine(main, "board.json"));

        var e = Assert.Throws<ArgumentException>(() => BoardLayouts.Move(_boards, a.Id, "Shelved"));
        Assert.Contains("In Progress", e.Message); // it says what the columns are
        Assert.Throws<ArgumentException>(() => BoardLayouts.Move(_boards, "T-0042", "To Do"));
        Assert.Equal(before, File.ReadAllText(Path.Combine(main, "board.json")));
    }

    [Fact]
    public void Quick_add_makes_the_card_on_the_board_and_in_the_column_named()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", Columns, "T");
        var art = BoardStore.CreateBoard(_boards, "Art", ["Doing", "Shipped"], "A");

        var doing = BoardLayouts.Add(_boards, "art", "Doing", "Paint the splash screen");
        Assert.Equal("Doing", doing.Status);
        Assert.Equal([doing.Id], BoardStore.Load(art).Lists[BoardStore.Load(art).Columns[0].Id].Select(x => x.Id));

        var backlog = BoardLayouts.Add(_boards, "Main", null, "The export button crashes");
        Assert.Equal("Backlog", backlog.Status);
        Assert.Equal("Bug", backlog.Type); // guessed from the words, as new-card does
        Assert.DoesNotContain(BoardStore.ReadCards(art), x => x.Id == backlog.Id);

        Assert.Throws<ArgumentException>(() => BoardLayouts.Add(_boards, "Nope", null, "x"));
        Assert.Throws<ArgumentException>(() => BoardLayouts.Add(_boards, "Main", "Shelved", "x"));
        Assert.Single(BoardStore.ReadCards(main)); // a refused add makes nothing
    }
}
