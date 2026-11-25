using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0214: a card's state for the board's split view, the sub-columns it makes, the All board's merged
/// columns and where the view's choices are kept.</summary>
public class CardStatesTests
{
    static CardState Of(bool working = false, bool asking = false, bool failed = false, bool qa = false, bool attention = false, bool blocked = false) =>
        CardStates.Of(working, asking, failed, qa, attention, blocked);

    [Fact]
    public void A_cards_state_puts_what_waits_on_you_first_then_whatever_runs()
    {
        Assert.Equal(CardState.Ready, Of());
        Assert.Equal(CardState.Blocked, Of(blocked: true));
        Assert.Equal(CardState.Working, Of(working: true, blocked: true)); // started anyway: it's at work
        Assert.Equal(CardState.RunningQa, Of(working: true, qa: true));
        Assert.Equal(CardState.NeedsYou, Of(working: true, asking: true, qa: true)); // a QA agent asking asks
        Assert.Equal(CardState.NeedsYou, Of(attention: true, blocked: true));
        Assert.Equal(CardState.Working, Of(working: true, attention: true)); // an agent at work on it in attention
        Assert.Equal(CardState.Failed, Of(failed: true, blocked: true));
        Assert.Equal(CardState.NeedsYou, Of(failed: true, attention: true));
    }

    [Fact]
    public void Ready_reads_as_QA_ready_in_the_QA_column_and_finished_lists_dont_split()
    {
        Assert.Equal("QA ready", CardStates.Label(CardState.Ready, "qa"));
        Assert.Equal("Ready to go", CardStates.Label(CardState.Ready, "backlog"));
        Assert.Equal("Running QA", CardStates.Label(CardState.RunningQa));
        Assert.Distinct(Enum.GetValues<CardState>().Select(s => CardStates.Label(s)));
        Assert.False(CardStates.Splits("done"));
        Assert.False(CardStates.Splits("completed"));
        Assert.True(CardStates.Splits("backlog"));
        Assert.True(CardStates.Splits(""));
        Assert.True(CardStates.Splits("attention"));
    }

    [Fact]
    public void Group_makes_a_group_per_state_in_order_keeping_each_items_order_and_none_for_an_empty_state()
    {
        var items = new[] { ("a", CardState.Ready), ("b", CardState.Blocked), ("c", CardState.Ready), ("d", CardState.NeedsYou) };
        var groups = CardStates.Group(items, i => i.Item2);
        Assert.Equal([CardState.Blocked, CardState.Ready, CardState.NeedsYou], groups.Select(g => g.State));
        Assert.Equal(["a", "c"], groups[1].Items.Select(i => i.Item1));
        Assert.Empty(CardStates.Group(Array.Empty<(string, CardState)>(), i => i.Item2));
    }

    [Fact]
    public void The_All_boards_columns_are_every_boards_merged_by_name_in_board_order()
    {
        var merged = CardStates.MergeColumns([
            ["Backlog", "To Do", "In Progress", "QA", "Done", "Completed"],
            ["Backlog", "in progress", "Design", "Review", "Done", "Completed"],
            ["Backlog", "Art", "Done", "Completed"],
        ]);
        Assert.Equal(["Backlog", "Art", "To Do", "In Progress", "Design", "Review", "QA", "Done", "Completed"], merged);
        Assert.Empty(CardStates.MergeColumns([]));
    }

    [Fact]
    public void The_views_choices_are_kept_per_project_in_the_instance()
    {
        var home = Directory.CreateTempSubdirectory("darg-views").FullName;
        try
        {
            Assert.False(BoardViews.Load("p", home).All);
            Assert.Empty(BoardViews.Load("p", home).Split);
            BoardViews.Save("p", new BoardViews.View(true, new(StringComparer.OrdinalIgnoreCase) { "Main", BoardViews.AllKey }), home);
            var back = BoardViews.Load("p", home);
            Assert.True(back.All);
            Assert.True(back.Split.SetEquals(["main", "*"]));
            Assert.False(BoardViews.Load("other", home).All);
        }
        finally { Directory.Delete(home, true); }
    }
}
