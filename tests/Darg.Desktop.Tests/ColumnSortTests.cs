using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0146: a board column sorts by title, newest or longest in the column, either way round, from its menu's
/// commands; the pick is kept; a drag within a sorted column is refused; a move stamps Moved.</summary>
public class ColumnSortTests
{
    static string Titles(ListViewModel list) => string.Join(" | ", list.Cards.Select(c => c.Card.Title));

    [AvaloniaFact]
    public async Task A_column_sorts_each_way_keeps_the_pick_and_refuses_a_drag_within_it()
    {
        using var app = DargApp.Make();
        var beta = app.Card("beta", "To Do");
        var alpha = app.Card("alpha", "To Do");
        var gamma = app.Card("gamma", "To Do");
        // entered the column: gamma first (oldest), then beta, then alpha
        BoardStore.MarkMoved(app.BoardFolder, gamma, new DateTime(2026, 11, 1, 9, 0, 0));
        BoardStore.MarkMoved(app.BoardFolder, beta, new DateTime(2026, 11, 5, 9, 0, 0));
        BoardStore.MarkMoved(app.BoardFolder, alpha, new DateTime(2026, 11, 9, 9, 0, 0));
        await app.Start();
        var list = app.Board.Lists.Single(l => l.Name == "To Do");
        Assert.Equal("beta | alpha | gamma", Titles(list)); // manual: creation order

        list.SortTitleCommand.Execute(null);
        Assert.Equal("alpha | beta | gamma", Titles(list));
        list.ReverseSortCommand.Execute(null);
        Assert.Equal("gamma | beta | alpha", Titles(list));
        list.SortLongestCommand.Execute(null);
        Assert.Equal("gamma | beta | alpha", Titles(list)); // longest in the column first
        list.ReverseSortCommand.Execute(null);
        Assert.Equal("alpha | beta | gamma", Titles(list));
        list.SortNewestCommand.Execute(null); // all made today: the higher number is newer
        Assert.Equal("gamma | alpha | beta", Titles(list));

        // kept: a fresh read of the file the sort is saved in, and the same order after a reload
        var kept = ColumnSorts.Load("proj");
        Assert.Equal(new ColumnSort(SortBy.Newest), kept[list.Key]);
        app.Board.Reload();
        Assert.Equal("gamma | alpha | beta", Titles(list));

        // a drag within the sorted column is refused, with the reason; board.json's order is untouched
        var order = File.ReadAllText(Path.Combine(app.BoardFolder, "board.json"));
        app.Board.Drop([list.Cards[2]], list, list.Cards[0].Id);
        Assert.Equal(order, File.ReadAllText(Path.Combine(app.BoardFolder, "board.json")));
        Assert.Contains("sorted", app.Board.Status);

        // dropping a card from another column still moves it
        var other = app.Board.Lists.Single(l => l.Name == "In Progress");
        var doing = app.Card("delta", "In Progress");
        app.Board.Reload();
        app.Board.Drop([other.Cards.Single(c => c.Id == doing.Id)], list, null);
        Assert.Contains("delta", Titles(list));

        list.SortManualCommand.Execute(null);
        Assert.Equal("beta | alpha | gamma | delta", Titles(list)); // the drag order comes back
        Assert.False(ColumnSorts.Load("proj").ContainsKey(list.Key));
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Moving_a_card_restarts_its_time_in_the_column_whoever_moves_it()
    {
        using var app = DargApp.Make();
        var dragged = app.Card("dragged", "To Do");
        var edited = app.Card("edited by an agent", "To Do");
        BoardStore.MarkMoved(app.BoardFolder, dragged, new DateTime(2026, 1, 1));
        BoardStore.MarkMoved(app.BoardFolder, edited, new DateTime(2026, 1, 1));
        await app.Start();
        var before = DateTime.Now.AddSeconds(-5);

        app.Board.Move(app.Board.Lists.Single(l => l.Name == "To Do").Cards.Single(c => c.Id == dragged.Id), app.ColumnId("In Progress"));
        Assert.True(app.Read(dragged.Id).Moved > before);

        // an agent edits the Status line itself: the next read of the board stamps it
        var file = Path.Combine(app.BoardFolder, "items", edited.File);
        File.WriteAllText(file, File.ReadAllText(file).Replace("**Status:** To Do", "**Status:** In Progress"));
        app.Board.Reload();
        await app.Until(() => app.Read(edited.Id).Moved > before, "the agent's move to be stamped");
        Assert.Equal("", app.Crashes());
    }
}
