using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>Card dependencies on the board (WOR-0172): a card's Blocked by ids are looked up across all the project's
/// boards, so a tile can name the blockers still open. The lookup is kept per board and never reads a card file itself
/// (WOR-0229): it takes every board from the reload pass that read them off the UI thread (<see cref="ReloadLater"/>), and
/// a row's board from the row's own read when a click reads it; a board.json changing on a board no row shows asks for a pass.</summary>
public partial class BoardViewModel
{
    /// <summary>One board's part of the lookup: its cards, and the board.json they were read with (write time and length,
    /// taken before the read; default when not known), so starting work reads again only a board changed since.</summary>
    sealed record BlockBoard(List<BoardCard> Cards, (DateTime, long) Stamp);

    /// <summary>Each board's cards by board name (the root's).</summary>
    readonly Dictionary<string, BlockBoard> _blockBoards = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Every card of the project by id, made from <see cref="_blockBoards"/>; null once a part of it changes.</summary>
    Dictionary<string, BoardCard>? _blockIndex;
    /// <summary>Every board has been read once (before that, an id not found may be on a board not read yet, so it isn't
    /// warned of).</summary>
    bool _blockAllRead;
    DispatcherTimer? _blockDebounce;

    /// <summary>The project's cards by id (the root's, shared by every row), made from what is already read.</summary>
    public Dictionary<string, BoardCard> BlockIndex()
    {
        var root = Root;
        if (root._blockIndex is not null) return root._blockIndex;
        using var timing = StallWatch.Shared.Section("Board.BlockIndex");
        return root._blockIndex = Blockers.Index(root.BlockCards());
    }

    /// <summary>Every card of the project as the lookup holds it, board by board in name order (as <see cref="CardIndex.Read"/>).</summary>
    List<BoardCard> BlockCards() =>
        _blockBoards.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).SelectMany(b => b.Value.Cards).ToList();

    /// <summary>Every card of the project, as things are now (for the panel's Blocks list).</summary>
    public IReadOnlyList<BoardCard> AllProjectCards()
    {
        BlockersFresh();
        return Root.BlockCards();
    }

    /// <summary>The board has cards with a Blocked by line, so its tiles depend on other cards (maybe on other boards).</summary>
    bool HasBlockedCards => _board?.AllCards.Any(c => c.BlockedBy.Count > 0) == true;

    static (DateTime, long) StampOf(string folder)
    {
        try { var f = new FileInfo(Path.Combine(folder, "board.json")); return f.Exists ? (f.LastWriteTimeUtc, f.Length) : default; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return default; }
    }

    /// <summary>This row read its board on the UI thread (the person's click): its part of the lookup is what it read.</summary>
    void BlockersRead()
    {
        if (_board is null || Folder is null) return;
        var root = Root;
        root._blockBoards[_board.Name] = new BlockBoard(CardIndex.Cards(_board.Name, _board), StampOf(Folder));
        root._blockIndex = null;
    }

    /// <summary>A reload pass landed (the root's): every board is as it read them, but for <paramref name="keep"/>, rows read
    /// by a click since the pass began, whose parts are newer.</summary>
    void BlockersLanded(ProjectBoards all, IReadOnlyDictionary<string, (DateTime, long)> stamps, IEnumerable<string> keep)
    {
        var kept = keep.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _blockBoards.Keys.Where(k => !kept.Contains(k)).ToList()) _blockBoards.Remove(gone);
        foreach (var board in all.Boards.Where(b => !kept.Contains(b.Name)))
            _blockBoards[board.Name] = new BlockBoard(CardIndex.Cards(board.Name, board), stamps.GetValueOrDefault(board.Name));
        _blockAllRead = true;
        _blockIndex = null;
    }

    /// <summary>Before starting anything (WOR-0173), catch up with the boards as they are now: each board.json's write time is
    /// checked and only a board changed since it was read (or never read) is read again, so in the usual case, the passes
    /// having kept up, nothing is.</summary>
    void BlockersFresh()
    {
        var root = Root;
        if (_project.Boards is not { } boards) return;
        using var timing = StallWatch.Shared.Section("Board.BlockersFresh");
        var names = BoardStore.List(boards);
        var changed = false;
        foreach (var gone in root._blockBoards.Keys.Except(names, StringComparer.OrdinalIgnoreCase).ToList())
            changed |= root._blockBoards.Remove(gone);
        foreach (var name in names)
        {
            var folder = Path.Combine(boards, name);
            var stamp = StampOf(folder);
            if (root._blockBoards.TryGetValue(name, out var had) && had.Stamp == stamp && stamp != default) continue;
            try { root._blockBoards[name] = new BlockBoard(CardIndex.Cards(name, BoardStore.Load(folder)), stamp); changed = true; }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        }
        root._blockAllRead = true;
        if (changed) root._blockIndex = null;
    }

    /// <summary>After a board was read again and drawn: other rows whose cards wait on cards are drawn again, since the
    /// card they wait on may be on this one.</summary>
    void RedrawBlockedRows()
    {
        foreach (var r in Root.Rows) if (r != this && r.HasBlockedCards) r.Apply();
    }

    /// <summary>A board.json changed anywhere in the project. One a row shows is the row's own watcher's to reload; one no row
    /// shows (a card moved on a board not showing, which may clear or reopen a blocker shown here) asks for a pass once the
    /// changes settle, which reads it off the UI thread and redraws the rows.</summary>
    void BoardJsonChanged(string path)
    {
        if (_parent is not null || _project.Boards is null) return;
        var name = Path.GetFileName(Path.GetDirectoryName(path));
        if (name is null || Rows.Any(r => string.Equals(r.SelectedBoard, name, StringComparison.OrdinalIgnoreCase))) return;
        _blockDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
        {
            _blockDebounce!.Stop();
            if (!_disposed) ReloadLater();
        });
        _blockDebounce.Stop();
        _blockDebounce.Start();
    }

    /// <summary>The blockers to show on a card's tile: open ones, unknown ids and cycles (<see cref="BlockerState.Shows"/>).
    /// Until every board has been read once, an id not found isn't called unknown: its board may not be read yet.</summary>
    internal IReadOnlyList<BlockerState> BlockersFor(Card card)
    {
        if (card.BlockedBy.Count == 0) return [];
        var index = BlockIndex();
        var all = Root._blockAllRead;
        return Blockers.Of(card, index).Where(b => b.Shows && (b.Found || all)).ToList();
    }
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
