using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>Card dependencies on the board (WOR-0172): a card's Blocked by ids are looked up across all the project's
/// boards, so a tile can name the blockers still open. The lookup is read once and kept until a board changes.</summary>
public partial class BoardViewModel
{
    /// <summary>Every card of the project by id, read when a row with blocked cards needs it; null once a board changes.</summary>
    Dictionary<string, BoardCard>? _blockIndex;
    DispatcherTimer? _blockDebounce;

    /// <summary>The project's cards by id (the root's, shared by every row).</summary>
    public Dictionary<string, BoardCard> BlockIndex()
    {
        var root = Root;
        if (root._blockIndex is not null) return root._blockIndex;
        using var timing = StallWatch.Shared.Section("Board.BlockIndex");
        return root._blockIndex = Blockers.Index(CardIndex.Read(_project.Boards));
    }

    /// <summary>Every card of the project, as read now (for the panel's Blocks list).</summary>
    public IReadOnlyList<BoardCard> AllProjectCards() => CardIndex.Read(_project.Boards);

    /// <summary>The board has cards with a Blocked by line, so its tiles depend on other cards (maybe on other boards).</summary>
    bool HasBlockedCards => _board?.AllCards.Any(c => c.BlockedBy.Count > 0) == true;

    /// <summary>A board was read again (before its lists are drawn): the lookup is stale.</summary>
    void BlockersStale() => Root._blockIndex = null;

    /// <summary>After a board was read again and drawn: other rows whose cards wait on cards are drawn again, since the
    /// card they wait on may be on this one.</summary>
    void RedrawBlockedRows()
    {
        foreach (var r in Root.Rows) if (r != this && r.HasBlockedCards) r.Apply();
    }

    /// <summary>A board.json changed anywhere in the project (a card moved on a board that may not be showing): rows
    /// with blocked cards are drawn again from a fresh lookup.</summary>
    void BoardJsonChanged()
    {
        if (_parent is not null) return;
        _blockDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
        {
            _blockDebounce!.Stop();
            if (!Rows.Any(r => r.HasBlockedCards)) return;
            _blockIndex = null;
            foreach (var r in Rows) if (r.HasBlockedCards) r.Apply();
        });
        _blockDebounce.Stop();
        _blockDebounce.Start();
    }

    /// <summary>The blockers to show on a card's tile: open ones, unknown ids and cycles (<see cref="BlockerState.Shows"/>).</summary>
    internal IReadOnlyList<BlockerState> BlockersFor(Card card) =>
        card.BlockedBy.Count == 0 ? [] : Blockers.Of(card, BlockIndex()).Where(b => b.Shows).ToList();
}

/// <summary>A card named in the panel's Blocked by or Blocks list: a link that opens it, with where it is.</summary>
public sealed record BlockerLink(string Id, string Text, bool IsWarning, bool Cleared, IRelayCommand Open)
{
    /// <summary>Waited on and not cleared, or a warning: drawn in the Warn colour; a cleared one is dim.</summary>
    public bool IsOpen => !Cleared || IsWarning;
}

/// <summary>The panel's Blocked by row (WOR-0172): the ids typed, and the cards named either way as links.</summary>
public partial class CardEditor
{
    /// <summary>The ids this card waits on, as typed (commas or spaces between them); written to its Blocked by line on Save.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial string BlockedByText { get; set; } = "";
    /// <summary>Its blockers as they stand, each a link to the card: where it is, cleared or not, or what's wrong.</summary>
    public IReadOnlyList<BlockerLink> BlockedByLinks { get; private set; } = [];
    /// <summary>The cards that wait on this one, each a link.</summary>
    public IReadOnlyList<BlockerLink> BlocksLinks { get; private set; } = [];
    public bool HasBlockedByLinks => BlockedByLinks.Count > 0;
    public bool HasBlocksLinks => BlocksLinks.Count > 0;

    void InitBlockers()
    {
        if (_card is null) return;
        BlockedByText = Blockers.Format(_card.Card.BlockedBy);
        IReadOnlyList<BoardCard> all;
        try { all = _board.AllProjectCards(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { all = []; }
        var index = Blockers.Index(all);
        var id = _card.Id;
        IRelayCommand OpenOf(string target) => new RelayCommand(() => _board.Root.OpenById(target));
        BlockedByLinks = Blockers.Of(_card.Card, index)
            .Select(b => new BlockerLink(b.Id, b.Text, b.IsWarning, b.Cleared, OpenOf(b.Id))).ToList();
        // while this card is open (not in QA or done), what names it is still waiting on it
        var waiting = index.GetValueOrDefault(id) is not { } me || !Blockers.IsCleared(me);
        BlocksLinks = Blockers.Blocks(id, all).Select(c => new BlockerLink(c.Card.Id,
            $"{c.Card.Id}: {c.Card.Title} ({c.Column} on {c.Board}){(waiting ? ", waiting on this" : "")}", false, !waiting, OpenOf(c.Card.Id))).ToList();
    }

    /// <summary>Why what's typed can't be saved (the card naming itself), or null.</summary>
    string? BlockersError(string? cardId) =>
        cardId is not null && Blockers.ParseIds(BlockedByText).Contains(cardId, StringComparer.OrdinalIgnoreCase) ? $"{cardId} can't wait on itself." : null;

    /// <summary>Write what's typed to the card's Blocked by line, if it changed. Unknown ids are kept (the tile warns);
    /// the card itself is refused.</summary>
    void SaveBlockers(string folder, string cardId, IReadOnlyList<string> before)
    {
        var ids = Blockers.ParseIds(BlockedByText);
        if (ids.SequenceEqual(before, StringComparer.OrdinalIgnoreCase)) return;
        BoardStore.SetBlockedBy(folder, cardId, ids);
    }
}
