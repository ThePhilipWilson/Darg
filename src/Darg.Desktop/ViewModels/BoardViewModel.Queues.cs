using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>Queues on the board (WOR-0212): the strip of the project's queues above the lists, a chip on each queued
/// card's tile, an "Add to queue" for each queue in its menu, and starting a queued card on whichever board holds it.</summary>
public partial class BoardViewModel
{
    /// <summary>The project's queues (set by the project when it has agents): the strip binds to it.</summary>
    public QueuesViewModel? Queues { get => _parent is null ? field : _parent.Queues; set; }

    /// <summary>+ Queue in the header: opens the strip's form for a new queue, filled from the board showing.</summary>
    [RelayCommand]
    void NewQueue() => Queues?.BeginNew(SelectedBoard);

    /// <summary>Start an agent on a queued card as Work on does, on whichever of the project's boards holds it, without
    /// changing the board on screen: it moves to its board's working column if it was in the backlog or the first column.
    /// Null when started, else why not.</summary>
    public string? StartQueued(string id)
    {
        if (_project.Boards is null || WorkOn is null) return "the project has no boards or agents";
        if (AgentFor?.Invoke(id) is not null) return null; // at work already: the queue waits on it
        if (CardIndex.Find(_project.Boards, id) is not { } found) return "no board has it";
        // a board on screen starts it as its own Work on does, and redraws
        var row = Root.Rows.FirstOrDefault(r => r.SelectedBoard == found.Board);
        if (row?._board?.AllCards.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } card)
        {
            row.StartWork(card, [], show: false, reload: true);
            return AgentFor?.Invoke(id) is null ? "no agent started" : null;
        }
        var folder = Path.Combine(_project.Boards, found.Board);
        try
        {
            var board = BoardStore.Load(folder);
            WorkOn(found.Card, found.File, [], false);
            var working = BulkStart.Working(board.Columns);
            var place = BoardStore.PlaceOf(found.Card.Status, board.Columns);
            if (working is not null && (place == Core.Board.Backlog || place == board.Columns[0].Id) && place != working.Id)
                BoardStore.Move(folder, found.Card.Id, working.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return e.Message; }
        return AgentFor?.Invoke(id) is null ? "no agent started" : null;
    }

    /// <summary>The tile's queue chips for a card: "⏵ Travel 3" per queue it's in.</summary>
    internal IReadOnlyList<string> QueueChipsFor(string id) => Queues?.ChipsFor(id) ?? [];

    /// <summary>The card's menu entries for queues: add it to each queue it isn't in, take it out of each it is.</summary>
    internal IEnumerable<MoveTarget> QueueTargets(CardViewModel card) => Queues is not { } queues || card.InDone ? [] : queues.TargetsFor(card.Id);
}

public sealed partial class CardViewModel
{
    /// <summary>WOR-0212: a chip per queue the card is in, with its place: "⏵ Travel 3".</summary>
    public IReadOnlyList<string> QueueChips => _queueChips ??= Board.QueueChipsFor(Id);
    IReadOnlyList<string>? _queueChips;
    public bool InQueue => QueueChips.Count > 0;
    /// <summary>What the chips were made from, so a tile is made again when the card joins, leaves or moves in a queue.</summary>
    public string QueueKey => string.Join(",", QueueChips);
}
