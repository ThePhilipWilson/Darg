using Workbench.Core;

namespace Workbench.Core.Tests;

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
    public void Moving_to_an_unknown_list_is_refused() =>
        Assert.Throws<ArgumentException>(() => BoardStore.Move(_dir, "T-0001", "c9"));

    public void Dispose() => Directory.Delete(_dir, true);
}
