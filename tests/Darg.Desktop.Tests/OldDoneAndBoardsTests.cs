using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>DAR-0003: old Done cards and every board of the project show on the board section.</summary>
public class OldDoneAndBoardsTests
{
    [AvaloniaFact]
    public async Task Done_cards_and_every_boards_folder_show()
    {
        using var app = DargApp.Make();
        var a = app.Card("old finished", "Done");
        var b = app.Card("also finished");
        BoardStore.Move(app.BoardFolder, b.Id, Board.Completed);
        BoardStore.CreateBoard(app.Boards, "Story two", ["To Do", "Done"]);
        await app.Start();
        Assert.Equal(2, app.Board.Boards.Count);
        var done = app.Board.Lists.Single(l => l.Name == "Done");
        Assert.Contains(done.Cards, c => c.Id == a.Id);
        app.Board.ShowCompleted = true;
        Assert.Contains(app.Board.Lists.Single(l => l.Name == "Completed").Cards, c => c.Id == b.Id);
    }

    /// <summary>What it was (2026-11-25): a commit renamed two cards' files, the checkout left the old names behind
    /// (ignored, so git never took them away), and two files with one id made every read of the board throw "An item
    /// with the same key has already been added", so the board never showed its Done cards or a second board's row.</summary>
    [AvaloniaFact]
    public async Task A_card_in_two_files_still_shows_the_board_and_says_which()
    {
        using var app = DargApp.Make();
        var a = app.Card("old finished", "Done");
        var mid = app.Card("in the middle", "In Progress");
        File.Copy(Path.Combine(app.BoardFolder, "items", a.File), Path.Combine(app.BoardFolder, "items", $"{a.Id} - its old name.md"));
        BoardStore.CreateBoard(app.Boards, "Story two", ["To Do", "Done"]);
        await app.Start();
        Assert.Contains(app.Board.Lists.Single(l => l.Name == "Done").Cards, c => c.Id == a.Id);
        Assert.Contains(app.Board.Lists.Single(l => l.Name == "In Progress").Cards, c => c.Id == mid.Id);
        // a card finished on disk (an agent's edit) reaches the board: before the fix every read threw and the board froze
        var file = Path.Combine(app.BoardFolder, "items", mid.File);
        File.WriteAllText(file, File.ReadAllText(file).Replace("**Status:** In Progress", "**Status:** Done"));
        app.Board.Reload();
        Assert.Contains(app.Board.Lists.Single(l => l.Name == "Done").Cards, c => c.Id == mid.Id);
        Assert.DoesNotContain("Couldn't read the board", app.Board.Status);
        Assert.Contains($"{a.Id} is in 2 files", app.Board.Status);
        Assert.True(app.Board.ShowsNotice);
        app.Board.ShowAllBoardsCommand.Execute(null);
        Assert.Equal(2, app.Board.Rows.Count);
        app.Project.Section = Section.Board;
        app.Shot("duplicate-id");
    }
}
