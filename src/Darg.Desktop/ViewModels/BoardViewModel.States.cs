using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>WOR-0214: the All board and the split by state.
///
/// The All board is every card of the project on one board, its lists every board's merged by name
/// (<see cref="CardStates.MergeColumns"/>). Picking it makes every board a row, as Ctrl+clicking each tab would, so
/// everything that works on the rows (moves, agents, picks, decisions, the reports written as cards move) works as it
/// always has; the view then draws one row, <see cref="AllRow"/>, whose lists hold those rows' own card tiles. Each card
/// keeps its board and its tile names it.
///
/// Split by state draws a list as narrow sub-columns, one per <see cref="CardState"/> its cards are in. Each board (and the
/// All board) keeps its own switch, in the instance's board-views file (<see cref="BoardViews"/>).</summary>
public partial class BoardViewModel
{
    BoardViews.View? _views;
    BoardViews.View Views => Root._views ??= BoardViews.Load(_project.Id);

    /// <summary>This is the All board's row: it reads no folder; its lists are made from the rows'.</summary>
    public bool IsAll { get; }
    /// <summary>The name a list's sort and split are kept under: the board's, or <see cref="BoardViews.AllKey"/>.</summary>
    public string? KeyName => IsAll ? BoardViews.AllKey : SelectedBoard;

    /// <summary>The All board is showing (the root's).</summary>
    [ObservableProperty] public partial bool ShowingAll { get; set; }
    BoardViewModel? _allRow;
    readonly ObservableCollection<BoardViewModel> _allRows = [];
    /// <summary>The All board's row, made the first time it's picked.</summary>
    public BoardViewModel? AllRow => _allRow;
    /// <summary>The rows the view draws: the boards' own, or the All board alone.</summary>
    public ObservableCollection<BoardViewModel> ShownRows => ShowingAll ? _allRows : Rows;
    /// <summary>The All tab is there once the project has two boards or more.</summary>
    public bool HasAllTab => _parent is null && Boards.Count > 1;

    /// <summary>The All board's row.</summary>
    BoardViewModel(BoardViewModel parent, bool all)
    {
        _project = parent._project;
        _parent = parent;
        IsAll = all;
        SplitByState = Views.Split.Contains(BoardViews.AllKey);
    }

    /// <summary>The All tab: show every card of the project on one board.</summary>
    [RelayCommand]
    void PickAllBoard() => SetAll(true);

    /// <summary>The root has been made: a board picked from here on (not the first, while the All board is restored) leaves the All board.</summary>
    bool _made;

    /// <summary>The picked board changed other than by its tab (which leaves the All board first): leave it, so that board shows.</summary>
    void LeaveAll()
    {
        if (_parent is not null || !_made || !ShowingAll) return;
        ShowingAll = false;
        Views.All = false;
        BoardViews.Save(_project.Id, Views);
        OnPropertyChanged(nameof(ShownRows));
        OnPropertyChanged(nameof(ActiveSplit));
    }

    void SetAll(bool on)
    {
        if (_parent is not null || ShowingAll == on) return;
        if (on && _allRow is null) { _allRow = new BoardViewModel(this, all: true); _allRows.Add(_allRow); }
        ShowingAll = on;
        Views.All = on;
        BoardViews.Save(_project.Id, Views);
        OnPropertyChanged(nameof(ShownRows));
        OnPropertyChanged(nameof(ActiveSplit));
        _notice = null;
        SyncRows();
    }

    /// <summary>This board's columns are split by state, a narrow sub-column per state (kept per board).</summary>
    [ObservableProperty] public partial bool SplitByState { get; set; }

    partial void OnSplitByStateChanged(bool value)
    {
        if (KeyName is { } key && (value ? Views.Split.Add(key) : Views.Split.Remove(key))) BoardViews.Save(_project.Id, Views);
        foreach (var l in Lists) l.Regroup();
        Root.OnPropertyChanged(nameof(ActiveSplit));
    }

    /// <summary>The section header's By state switch: the All board's while it shows, else the picked board's.</summary>
    public bool ActiveSplit
    {
        get => ShowingAll && _allRow is { } a ? a.SplitByState : SplitByState;
        set { if (ShowingAll && _allRow is { } a) a.SplitByState = value; else SplitByState = value; }
    }

    /// <summary>The section header carries the switch while one board shows (or the All board); with several rows, each
    /// row's header has its own.</summary>
    public bool ShowsSectionSplit => ShowingAll || Rows.Count == 1;

    /// <summary>The split view must be drawn again: agents' states changed (a turn started, a question asked). Held
    /// a moment so a burst of changes regroups once.</summary>
    public void StatesChanged()
    {
        var root = Root;
        if (!root.Rows.Any(r => r.SplitByState) && root._allRow?.SplitByState != true) return;
        if (root._regroupPosted) return;
        root._regroupPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            root._regroupPosted = false;
            foreach (var r in root.Rows.Append(root._allRow).OfType<BoardViewModel>().Where(r => r.SplitByState))
                foreach (var l in r.Lists) l.Regroup();
        }, DispatcherPriority.Background);
    }
    bool _regroupPosted;

    /// <summary>The All board's lists made again from the rows' (after any row is drawn): a list per name, holding each
    /// row's cards of that name in board order, or in the All list's own sort.</summary>
    void BuildMerged()
    {
        var root = Root;
        if (!root.ShowingAll || root._allRow is not { } all) return;
        var rows = root.Rows.Where(r => r._board is not null).ToList();
        var names = CardStates.MergeColumns(rows.Select(r => (IReadOnlyList<string>)r.Lists.Select(l => l.Name).ToList()));
        var old = new Dictionary<string, ListViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in all.Lists) old.TryAdd(l.Name, l);
        var lists = new List<ListViewModel>();
        var shown = 0;
        foreach (var name in names)
        {
            var parts = rows.SelectMany(r => r.Lists.Where(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
            var views = parts.SelectMany(p => p.Cards).ToList();
            var byCard = new Dictionary<Card, CardViewModel>(ReferenceEqualityComparer.Instance);
            foreach (var v in views) byCard.TryAdd(v.Card, v);
            var sort = all.SortOf(BoardViews.AllKey + "/" + name);
            var cards = sort.IsManual ? views : sort.Apply(views.Select(v => v.Card)).Select(c => byCard[c]).ToList();
            shown += cards.Count;
            var count = parts.Sum(p => p.All);
            if (old.TryGetValue(name, out var list)) list.Show(cards, count);
            else list = new ListViewModel(all, name, name, cards, count);
            list.Kind = parts.Select(p => p.Kind).FirstOrDefault(k => k.Length > 0) ?? "";
            lists.Add(list);
        }
        ListSync.Sync(all.Lists, lists);
        foreach (var l in all.Lists) { l.SortChanged(); l.Regroup(); }
        var total = rows.Sum(r => r._board!.AllCards.Count());
        root.CountText = $"{shown} of {total} cards on {rows.Count} boards";
        root.Status = root._notice ?? root.CountText;
    }

    /// <summary>Cards dropped on an All list: each goes to the list of that name on its own board (a drop never moves a
    /// card between boards here). A card whose board has no such list stays, named.</summary>
    void DropMerged(IReadOnlyList<CardViewModel> cards, ListViewModel list, string? beforeId)
    {
        var root = Root;
        var stayed = new List<string>();
        var moved = 0;
        foreach (var group in cards.GroupBy(c => c.Board))
        {
            var row = group.Key;
            // ListOrder's items are tuples, so FirstOrDefault would never be null: look for the id
            if (row._board?.ListOrder().Where(l => l.Name.Equals(list.Name, StringComparison.OrdinalIgnoreCase)).Select(l => l.Id).FirstOrDefault() is not { } to)
            {
                stayed.AddRange(group.Select(c => $"{c.Id} ({row.SelectedBoard} has no {list.Name})"));
                continue;
            }
            var before = beforeId is not null && row._board.AllCards.Any(c => c.Id == beforeId) ? beforeId : null;
            row.Move(group.ToList(), to, before);
            moved += group.Count();
        }
        root._notice = stayed.Count == 0 ? null
            : $"Moved {moved} to {list.Name}; left {string.Join(", ", stayed)} where {(stayed.Count == 1 ? "it was" : "they were")}";
        BuildMerged();
    }
}

/// <summary>One state's cards in a split list (WOR-0214): a narrow sub-column with its heading and count. Cards
/// dropped on it go to its list (its Tag is the list's key).</summary>
public sealed partial class StateGroup(ListViewModel list, CardState state) : ObservableObject
{
    public const double Width = 214;
    public ListViewModel List { get; } = list;
    public CardState State { get; } = state;
    public string Key => List.Key;
    public string Name => CardStates.Label(State, List.Kind);
    public ObservableCollection<CardViewModel> Cards { get; } = [];
    public string Count => Cards.Count.ToString();
    public bool IsBlocked => State == CardState.Blocked;
    public bool IsWorking => State is CardState.Working or CardState.RunningQa;
    public bool IsNeeds => State == CardState.NeedsYou;
    public bool IsFailed => State == CardState.Failed;

    public void Show(IReadOnlyList<CardViewModel> cards)
    {
        ListSync.Sync(Cards, cards);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Name));
    }
}
