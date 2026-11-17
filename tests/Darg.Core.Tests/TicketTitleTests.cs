using Darg.Core;

namespace Darg.Core.Tests;

public sealed class TicketTitleTests : IDisposable
{
    // The board gets a folder of its own: Create numbers past every board beside it, and %TEMP% holds other
    // tests' boards (a T board left there by a killed run made this file's card T-0002 for weeks).
    readonly string _root = Directory.CreateTempSubdirectory("wb-tt-").FullName;
    string _dir => Path.Combine(_root, "board");

    const string Perf = "performance issues. noticeable hanging, particularly when starting multiple tickets";
    const string Truncate = "when turning a note into a ticket don't truncate out the title";

    [Theory] // replies a model gave, or could, and what's kept of them
    [InlineData("performance: hangs when starting multiple tickets", Perf, "performance: hangs when starting multiple tickets")]
    [InlineData("\"performance: hangs when starting multiple tickets\"", Perf, "performance: hangs when starting multiple tickets")]
    [InlineData("Title: hanging when starting multiple tickets.", Perf, "hanging when starting multiple tickets")]
    [InlineData("**Title:** hanging when starting tickets", Perf, "hanging when starting tickets")]
    [InlineData("```\nhanging when starting multiple tickets\n```", Perf, "hanging when starting multiple tickets")]
    [InlineData("`don't truncate the ticket title`", Truncate, "don't truncate the ticket title")]
    [InlineData("  don't truncate the title when turning a note into a ticket \r\n", Truncate, "don't truncate the title when turning a note into a ticket")]
    public void Keeps_a_title_and_strips_its_wrapping(string reply, string note, string expected) =>
        Assert.Equal(expected, TicketTitle.Clean(reply, note));

    [Theory]
    [InlineData("broken link in marketing page", Truncate)]          // mistral 7B, invented
    [InlineData("refactor login page layout", Truncate)]             // mistral 7B, invented
    [InlineData("at most 60 characters", Truncate)]                  // qwen3:1.7b, the instructions echoed
    [InlineData("Here's a title: don't truncate the title", Truncate)] // talk around it
    [InlineData("Sure! don't truncate the title", Truncate)]
    [InlineData("don't truncate the title\nIt keeps the words.", Truncate)] // a second line
    [InlineData("", Truncate)]
    [InlineData("ok", Truncate)]
    [InlineData("when turning a note into a ticket don't truncate out the title, keep every word of it on the card", Truncate)] // too long
    public void Refuses_a_reply_that_isnt_a_title_from_the_note(string reply, string note) =>
        Assert.Null(TicketTitle.Clean(reply, note));

    [Fact]
    public void The_prompt_carries_the_note()
    {
        var p = TicketTitle.Prompt("  fix logo aliasing \n");
        Assert.EndsWith("Note:\nfix logo aliasing", p);
        Assert.Contains("at most 60 characters", p);
    }

    string Board()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "board.json"), """{"columns":[],"prefix":"T","nextId":1,"order":{}}""");
        return _dir;
    }

    [Fact]
    public async Task Retitling_a_short_note_keeps_its_words_at_the_top_of_the_description()
    {
        var board = Board();
        var card = BoardStore.Create(board, "fix write it down text being white on postits", "From a note on the wall (n1).");
        var saved = await TicketTitle.Retitle(board, card.Id, card.Title, card.Title, _ => Task.FromResult<string?>("white text on post-its"));
        Assert.NotNull(saved);
        var back = BoardStore.ReadCards(board).Single();
        Assert.Equal("white text on post-its", back.Title);
        Assert.Equal("fix write it down text being white on postits\n\nFrom a note on the wall (n1).", back.Description);
        Assert.Equal("T-0001 - white text on post-its.md", back.File);
    }

    [Fact]
    public async Task Retitling_a_cut_title_leaves_the_description_which_has_the_whole_line()
    {
        var board = Board();
        var card = BoardStore.Create(board, "not particularly easy to create new boards, would really like a Smart…", "the whole line\n\nFrom a note on the wall (n1).");
        await TicketTitle.Retitle(board, card.Id, card.Title, "x", _ => Task.FromResult<string?>("smart generate boards"));
        var back = BoardStore.ReadCards(board).Single();
        Assert.Equal("smart generate boards", back.Title);
        Assert.Equal("the whole line\n\nFrom a note on the wall (n1).", back.Description);
    }

    [Fact]
    public async Task A_card_renamed_meanwhile_or_no_title_back_is_left_alone()
    {
        var board = Board();
        var card = BoardStore.Create(board, "fix logo aliasing", "d");
        BoardStore.Update(board, card.Id, title: "logo edges are jagged");
        Assert.Null(await TicketTitle.Retitle(board, card.Id, "fix logo aliasing", "fix logo aliasing", _ => Task.FromResult<string?>("logo aliasing")));
        Assert.Null(await TicketTitle.Retitle(board, card.Id, "logo edges are jagged", "x", _ => Task.FromResult<string?>(null)));
        Assert.Null(await TicketTitle.Retitle(board, "T-0099", "x", "x", _ => Task.FromResult<string?>("y")));
        Assert.Null(await TicketTitle.Retitle(board, card.Id, "logo edges are jagged", "x", _ => throw new IOException("gone")));
        Assert.Equal("logo edges are jagged", BoardStore.ReadCards(board).Single().Title);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
