using Workbench.Core;

namespace Workbench.Core.Tests;

/// <summary>Several boards in one project: making one, and moving cards between them.</summary>
public sealed class NewBoardTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("wb-boards-").FullName;
    readonly string _main;

    public NewBoardTests()
    {
        _main = Directory.CreateDirectory(Path.Combine(_root, "Main")).FullName;
        File.WriteAllText(Path.Combine(_main, "board.json"), """
            {"columns":[{"id":"c1","name":"To Do"},{"id":"c2","name":"In Progress"},{"id":"c3","name":"Done"}],
             "prefix":"WOR","nextId":4,"order":{"backlog":["WOR-0001"],"c2":["WOR-0002"]}}
            """);
        Directory.CreateDirectory(Path.Combine(_main, "items"));
        Card(1, "Backlog one", "Backlog");
        Card(2, "Busy one", "In Progress");
        Card(3, "Shelved one", "Shelved");
    }

    void Card(int n, string title, string status) =>
        File.WriteAllText(Path.Combine(_main, "items", $"WOR-{n:0000} - {title}.md"),
            $"# WOR-{n:0000}: {title}\r\n\r\n- **Status:** {status}\r\n- **Priority:**\r\n- **Type:** Task\r\n- **Labels:**\r\n- **Created:** 2026-11-16\r\n- **Done:**\r\n\r\n## Description\r\n\r\nAbout {title}.\r\n");

    [Fact]
    public void A_new_board_takes_the_projects_prefix_and_numbers_past_every_board()
    {
        var ui = BoardStore.CreateBoard(_root, "UI");
        Assert.Equal(["Main", "UI"], BoardStore.List(_root));
        var b = BoardStore.Load(ui);
        Assert.Equal("WOR", b.Prefix);
        Assert.Equal(["To Do", "In Progress", "Done"], b.Columns.Select(c => c.Name));
        Assert.Equal("WOR-0004", BoardStore.Create(ui, "First on UI").Id); // Main's nextId is 4
        Assert.Equal("WOR-0005", BoardStore.Create(_main, "Next on Main").Id); // and Main sees UI's
    }

    [Fact]
    public void A_new_board_can_have_its_own_columns()
    {
        var b = BoardStore.Load(BoardStore.CreateBoard(_root, "Art", ["Sketch", " Paint ", "sketch", ""]));
        Assert.Equal(["Sketch", "Paint"], b.Columns.Select(c => c.Name));
        Assert.Equal(["c1", "c2"], b.Columns.Select(c => c.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Main")]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData(".hidden")]
    public void Bad_or_taken_names_are_refused(string name) =>
        Assert.Throws<ArgumentException>(() => BoardStore.CreateBoard(_root, name));

    [Fact]
    public void Backlog_and_completed_cannot_be_columns() =>
        Assert.Throws<ArgumentException>(() => BoardStore.CreateBoard(_root, "X", ["To Do", "Completed"]));

    [Fact]
    public void Transfer_keeps_the_id_and_the_column_by_name()
    {
        var ui = BoardStore.CreateBoard(_root, "UI");
        var c = BoardStore.Transfer(_main, ui, "WOR-0002");
        Assert.Equal("WOR-0002", c.Id);
        var to = BoardStore.Load(ui);
        Assert.Equal(["WOR-0002"], to.Lists[to.Columns.Single(x => x.Name == "In Progress").Id].Select(x => x.Id));
        Assert.DoesNotContain(BoardStore.Load(_main).AllCards, x => x.Id == "WOR-0002");
        Assert.DoesNotContain("WOR-0002", File.ReadAllText(Path.Combine(_main, "board.json")));
        Assert.Contains("\r\n", File.ReadAllText(Path.Combine(ui, "items", c.File)));
    }

    [Fact]
    public void A_status_the_other_board_has_no_column_for_goes_to_its_backlog()
    {
        var art = BoardStore.CreateBoard(_root, "Art", ["Sketch"]);
        BoardStore.Transfer(_main, art, "WOR-0002");
        BoardStore.Transfer(_main, art, "WOR-0003");
        var b = BoardStore.Load(art);
        Assert.Equal(["WOR-0002", "WOR-0003"], b.Lists[Board.Backlog].Select(x => x.Id));
        Assert.All(b.Lists[Board.Backlog], x => Assert.Equal("Backlog", x.Status));
    }

    [Fact]
    public void Transfer_to_the_same_board_is_refused() =>
        Assert.Throws<ArgumentException>(() => BoardStore.Transfer(_main, _main + Path.DirectorySeparatorChar, "WOR-0001"));

    public void Dispose() => Directory.Delete(_root, true);
}
