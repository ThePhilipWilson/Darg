using Darg.Core;

namespace Darg.Core.Tests;

public sealed class BoardTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-board-").FullName;

    public BoardTests()
    {
        File.WriteAllText(Path.Combine(_dir, "board.json"), """
            {"columns":[{"id":"c1","name":"To Do"},{"id":"c2","name":"In Progress"},{"id":"c3","name":"Done"}],
             "prefix":"T","nextId":5,"order":{"backlog":["T-0002","T-0001"],"c1":["T-0003"]}}
            """);
        Directory.CreateDirectory(Path.Combine(_dir, "items"));
        Card(1, "First", "Backlog", "art, cat");
        Card(2, "Second", "Backlog", "");
        Card(3, "Third", "To Do", "engine");
        Card(4, "Fourth", "Shelved", "");
    }

    void Card(int n, string title, string status, string labels) =>
        File.WriteAllText(Path.Combine(_dir, "items", $"T-{n:0000} - {title}.md"),
            $"# T-{n:0000}: {title}\r\n\r\n- **Status:** {status}\r\n- **Priority:**\r\n- **Type:** Task\r\n- **Labels:** {labels}\r\n- **Created:** 2026-11-16\r\n- **Done:**\r\n\r\n## Description\r\n\r\nAbout {title}.\r\n");

    [Fact]
    public void Reads_lists_in_board_json_order()
    {
        var b = BoardStore.Load(_dir);
        Assert.Equal(["T-0002", "T-0001"], b.Lists[Board.Backlog].Select(c => c.Id));
        Assert.Equal(["T-0003"], b.Lists["c1"].Select(c => c.Id));
        Assert.Equal(["T-0004"], b.Lists[Board.Unsorted].Select(c => c.Id));
        Assert.Equal(["art", "cat"], b.Lists[Board.Backlog][1].Labels);
        Assert.Equal("About First.", b.Lists[Board.Backlog][1].Description);
    }

    [Fact]
    public void Move_sets_status_and_order_and_keeps_line_endings()
    {
        BoardStore.Move(_dir, "T-0001", "c1", beforeId: "T-0003");
        var b = BoardStore.Load(_dir);
        Assert.Equal(["T-0001", "T-0003"], b.Lists["c1"].Select(c => c.Id));
        Assert.Equal(["T-0002"], b.Lists[Board.Backlog].Select(c => c.Id));
        var text = File.ReadAllText(Path.Combine(_dir, "items", "T-0001 - First.md"));
        Assert.Contains("- **Status:** To Do\r\n", text);
        Assert.DoesNotContain("\n\n\n", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Completing_stamps_done_and_reopening_clears_it()
    {
        BoardStore.Move(_dir, "T-0002", Board.Completed);
        Assert.Matches(@"- \*\*Done:\*\* \d{4}-\d{2}-\d{2}", File.ReadAllText(Path.Combine(_dir, "items", "T-0002 - Second.md")));
        BoardStore.Move(_dir, "T-0002", Board.Backlog);
        Assert.Contains("- **Done:**\r\n", File.ReadAllText(Path.Combine(_dir, "items", "T-0002 - Second.md")));
    }

    [Fact]
    public void Delete_moves_the_file_aside_and_drops_it_from_the_order()
    {
        BoardStore.Delete(_dir, "T-0002");
        var b = BoardStore.Load(_dir);
        Assert.DoesNotContain(b.AllCards, c => c.Id == "T-0002");
        Assert.True(File.Exists(Path.Combine(_dir, ".deleted", "T-0002 - Second.md")));
        Assert.DoesNotContain("T-0002", File.ReadAllText(Path.Combine(_dir, "board.json")));
    }

    [Fact]
    public void Moving_to_an_unknown_list_is_refused() =>
        Assert.Throws<ArgumentException>(() => BoardStore.Move(_dir, "T-0001", "c9"));

    [Fact]
    public void Renaming_a_column_renames_its_cards_status()
    {
        BoardStore.RenameColumn(_dir, "c1", "  Next   up ");
        var b = BoardStore.Load(_dir);
        Assert.Equal("Next up", b.Columns[0].Name);
        Assert.Equal(["T-0003"], b.Lists["c1"].Select(c => c.Id));
        Assert.Contains("- **Status:** Next up\r\n", File.ReadAllText(Path.Combine(_dir, "items", "T-0003 - Third.md")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("done")]
    [InlineData("Backlog")]
    [InlineData("completed")]
    public void A_column_name_that_clashes_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => BoardStore.RenameColumn(_dir, "c1", name));
        Assert.Throws<ArgumentException>(() => BoardStore.AddColumn(_dir, name));
    }

    [Fact]
    public void Renaming_a_column_to_its_own_name_in_another_case_is_fine()
    {
        BoardStore.RenameColumn(_dir, "c1", "TO DO");
        Assert.Equal("TO DO", BoardStore.Load(_dir).Columns[0].Name);
    }

    [Fact]
    public void Adding_a_column_takes_the_next_id()
    {
        Assert.Equal("c4", BoardStore.AddColumn(_dir, "Review", index: 2));
        Assert.Equal(["To Do", "In Progress", "Review", "Done"], BoardStore.Load(_dir).Columns.Select(c => c.Name));
    }

    [Fact]
    public void Removing_a_column_sends_its_cards_to_the_end_of_the_backlog()
    {
        BoardStore.SetMinimized(_dir, "c1", true);
        Assert.Equal(1, BoardStore.RemoveColumn(_dir, "c1"));
        var b = BoardStore.Load(_dir);
        Assert.Equal(["In Progress", "Done"], b.Columns.Select(c => c.Name));
        Assert.Equal(["T-0002", "T-0001", "T-0003"], b.Lists[Board.Backlog].Select(c => c.Id));
        Assert.Empty(b.Minimized);
        Assert.Contains("- **Status:** Backlog\r\n", File.ReadAllText(Path.Combine(_dir, "items", "T-0003 - Third.md")));
        Assert.DoesNotContain("\"c1\"", File.ReadAllText(Path.Combine(_dir, "board.json")));
    }

    [Fact]
    public void The_last_column_cant_be_removed()
    {
        BoardStore.RemoveColumn(_dir, "c1");
        BoardStore.RemoveColumn(_dir, "c2");
        Assert.Throws<ArgumentException>(() => BoardStore.RemoveColumn(_dir, "c3"));
    }

    [Fact]
    public void Minimized_lists_are_kept_in_board_json()
    {
        BoardStore.SetMinimized(_dir, Board.Backlog, true);
        BoardStore.SetMinimized(_dir, "c2", true);
        Assert.Equal([Board.Backlog, "c2"], BoardStore.Load(_dir).Minimized.Order());
        BoardStore.SetMinimized(_dir, Board.Backlog, false);
        BoardStore.SetMinimized(_dir, "c2", false);
        Assert.Empty(BoardStore.Load(_dir).Minimized);
        Assert.DoesNotContain("minimized", File.ReadAllText(Path.Combine(_dir, "board.json")));
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
