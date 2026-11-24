using Avalonia.Headless.XUnit;
using Darg.Core;

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
}
