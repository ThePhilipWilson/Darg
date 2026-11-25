using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A project's boards: the one picked, and any others shown with it a row each (WOR-0087: a board per
/// story), filtered by text and labels, kept live from disk (Tatty, Obsidian or an agent changing a card shows here
/// within a moment). Each further row is a BoardViewModel of its own whose <see cref="Root"/> is this one: the
/// filter, label chips, Show completed, the card panel and the toolbar's picks are the root's.</summary>
public partial class BoardViewModel : ObservableObject, IDisposable
{
    readonly ProjectConfig _project;
    readonly BoardViewModel? _parent;
    FileSystemWatcher? _watch;
    DispatcherTimer? _debounce;
    Board? _board;

    /// <summary>The board section's own view model: this one, or the one whose further row this is.</summary>
    public BoardViewModel Root => _parent ?? this;
    /// <summary>The boards on screen, a row each: the picked one first, then the others shown with it.</summary>
    public ObservableCollection<BoardViewModel> Rows { get; } = [];
    /// <summary>The others shown with the picked one (kept in the instance, <see cref="ShownBoards"/>).</summary>
    readonly HashSet<string> _also = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>A tab per board (WOR-0149): lit for each board showing, the picked one and those shown with it.</summary>
    public ObservableCollection<BoardTab> Tabs { get; } = [];
    /// <summary>Set by the view as a tab is pressed: whether Ctrl was down, which the click that follows reads.</summary>
    public bool TabCtrl { get; set; }
    /// <summary>A row's header (its board's name) shows once there is more than one row.</summary>
    [ObservableProperty] public partial bool ShowsHeader { get; set; }
    public bool IsExtraRow => _parent is not null;
    /// <summary>A row's height, so several share the screen (WOR-0160): one dragged by its divider keeps it, the
    /// rest share what is left (<see cref="BoardRowHeights"/>). One row on its own fills the space.</summary>
    [ObservableProperty] public partial double RowHeight { get; set; } = double.NaN;
    /// <summary>The height the rows have between them, set by the view as it is laid out.</summary>
    public double RowsSpace { get => _rowsSpace; set { if (_rowsSpace == value) return; _rowsSpace = value; FitRows(); } }
    double _rowsSpace;
    /// <summary>The heights rows were dragged to, by board (kept in the instance).</summary>
    Dictionary<string, double>? _heights;

    /// <summary>Set by the project: the live agent working on a card, if any.</summary>
    public Func<string, AgentViewModel?>? AgentFor { get => _parent is null ? field : _parent.AgentFor; set; }
    /// <summary>Whether a QA agent (Run QA) is running on a card: Run QA is offered unless one is, whatever other agents the card has (WOR-0110).</summary>
    public Func<string, bool>? QaRunning { get => _parent is null ? field : _parent.QaRunning; set; }
    /// <summary>Set by the project: start an agent on a card. Passed: the card's file path, the ids of cards
    /// other agents are being started on at the same time, and whether to show the agent.</summary>
    public Action<Card, string, IReadOnlyList<string>, bool>? WorkOn { get => _parent is null ? field : _parent.WorkOn; set; }
    /// <summary>Set by the project: start an agent that QAs a card in QA (WOR-0108): the card's file path.</summary>
    public Action<Card, string>? RunQa { get => _parent is null ? field : _parent.RunQa; set; }
    /// <summary>Picked card ids. Only ever cards that are showing: a filter that hides one unpicks it, so a
    /// batch never touches a card you can't see.</summary>
    readonly HashSet<string> _picked = [];
    /// <summary>What was just done ("Started 3 agents…"): it stays in the status line through the reload
    /// the file watcher does a moment later, until the filter changes or a card is picked.</summary>
    string? _notice;
    /// <summary>Set by the project: the flows a card can be run through.</summary>
    public Func<IReadOnlyList<Flow>>? Flows { get => _parent is null ? field : _parent.Flows; set; }
    /// <summary>Set by the project: run a flow on a card (passed the card's file path).</summary>
    public Action<Card, string, Flow>? RunFlow { get => _parent is null ? field : _parent.RunFlow; set; }
    /// <summary>Set by the project: show an agent.</summary>
    public Action<AgentViewModel>? OpenAgent { get => _parent is null ? field : _parent.OpenAgent; set; }
    /// <summary>Set by the project: start an agent and show it. Passed: the prompt, its folder, its name and more
    /// folders it may work in.</summary>
    public Action<string, string, string, IReadOnlyList<string>>? StartAgent { get => _parent is null ? field : _parent.StartAgent; set; }
    /// <summary>Set by the project: start an agent that rounds a thin card out (passed the card's file path).</summary>
    public Action<Card, string>? Improve { get => _parent is null ? field : _parent.Improve; set; }
    /// <summary>Set by the project: start an agent reworking a card sent back from QA (passed the card's file path
    /// and the person's notes, already written into the card).</summary>
    public Action<Card, string, string>? Rework { get => _parent is null ? field : _parent.Rework; set; }
    FileSystemWatcher? _watchBoards;
    /// <summary>Set by the project (WOR-0092): a card has landed in the needs-attention column (passed the card and
    /// its file); the project writes its decision report.</summary>
    public Action<Card, string>? NeedsAttention { get => _parent is null ? field : _parent.NeedsAttention; set; }
    /// <summary>Set by the project (WOR-0102): a card that was elsewhere has entered the QA column / the done column
    /// (passed the card and its file); the project writes or archives its reports.</summary>
    public Action<Card, string>? EnteredQa { get => _parent is null ? field : _parent.EnteredQa; set; }
    public Action<Card, string>? EnteredDone { get => _parent is null ? field : _parent.EnteredDone; set; }
    readonly ColumnWatch _moves = new();
    readonly MovedWatch _stamps = new();
    Dictionary<string, ColumnSort>? _sorts;

    /// <summary>The sort picked for a list (WOR-0146), kept per project beside the shown boards, by <see cref="ListViewModel.Key"/>.</summary>
    public ColumnSort SortOf(string key) =>
        (Root._sorts ??= ColumnSorts.Load(_project.Id)).GetValueOrDefault(key, ColumnSort.Manual);

    public void SetSort(ListViewModel list, ColumnSort sort)
    {
        var all = Root._sorts ??= ColumnSorts.Load(_project.Id);
        if (sort.IsManual) all.Remove(list.Key); else all[list.Key] = sort;
        ColumnSorts.Save(_project.Id, all);
        Root.ApplyAll();
    }
    /// <summary>Set by the project: the newest report written for a card (its file), or null.</summary>
    public Func<string, string?>? ReportFor { get => _parent is null ? field : _parent.ReportFor; set; }
    /// <summary>Set by the project: show a report in Reports.</summary>
    public Action<string>? OpenReport { get => _parent is null ? field : _parent.OpenReport; set; }

    public ObservableCollection<string> Boards { get; } = [];
    public ObservableCollection<ListViewModel> Lists { get; } = [];
    public ObservableCollection<LabelChip> Labels { get; } = [];

    [ObservableProperty] public partial string? SelectedBoard { get; set; }
    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial bool ShowCompleted { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsNotice), nameof(ShowsRowNotice))] public partial string Status { get; set; } = "";
    /// <summary>The card count ("12 of 30 cards"), which the header shows while <see cref="Status"/> says something else
    /// in full on a line of its own below it (WOR-0173 QA: a notice cut off at the header's width lost which cards were
    /// left out and what they wait on).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsNotice), nameof(ShowsRowNotice))] public partial string CountText { get; set; } = "";
    /// <summary>Status says more than the count: a notice or an error, shown whole and wrapped below the header.</summary>
    public bool ShowsNotice => Status.Length > 0 && Status != CountText;
    /// <summary>A further row's notice shows under its own header; the first row's is the section header's.</summary>
    public bool ShowsRowNotice => ShowsNotice && IsExtraRow;
    /// <summary>The notice line's ✕: back to the count.</summary>
    [RelayCommand] void DismissNotice() { _notice = null; Status = CountText; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditing))] public partial CardEditor? Editor { get; set; }
    public bool IsEditing => Editor is not null;
    [RelayCommand]
    void NewCard()
    {
        // on the All board (WOR-0214) a new card goes to the inbox board, as a note's ticket does
        var on = ShowingAll ? Rows.FirstOrDefault(r => r.Folder is { } f && f == InboxFolder) ?? this : this;
        if (on.Folder is not null) Root.Editor = new CardEditor(on, null);
    }

    /// <summary>The side panel for making boards: one by hand, or an agent sorting the cards into several.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsMaking))] public partial BoardMaker? Maker { get; set; }
    public bool IsMaking => Maker is not null;
    [RelayCommand]
    void NewBoard()
    {
        if (_project.Boards is null) return;
        Editor = null;
        Maker = Maker is null ? new BoardMaker(this, _board?.Columns.Select(c => c.Name) ?? BoardStore.DefaultColumns) : null;
    }
    partial void OnEditorChanged(CardEditor? value) { if (value is not null) Maker = null; }

    /// <summary>Make a board and show it.</summary>
    public void CreateBoard(string name, IEnumerable<string> columns)
    {
        var folder = BoardStore.CreateBoard(_project.Boards!, name, columns);
        if (_watchBoards is null) WatchBoards(); // the boards folder may only now exist
        RefreshBoards();
        SelectedBoard = Path.GetFileName(folder);
        _notice = $"Made the board {SelectedBoard}. Right-click a card on another board to move it here.";
        Apply();
    }

    /// <summary>Start an agent that reads every card on the project's boards, proposes how to split them
    /// into boards, asks, and then makes the boards and moves the cards.</summary>
    public void SortIntoBoards(string? hint)
    {
        if (_project.Boards is null || StartAgent is null) return;
        var busy = BoardStore.List(_project.Boards)
            .SelectMany(b => BoardStore.ReadCards(Path.Combine(_project.Boards, b)))
            .Where(c => AgentFor?.Invoke(c.Id) is not null).Select(c => c.Id).ToList();
        StartAgent(SortPrompt(_project.Name, _project.Boards, BoardStore.List(_project.Boards), busy, hint, Core.Commits.For(_project, _project.Boards)),
            _project.Boards, $"Sort {_project.Name}'s cards into boards", []);
        Maker = null;
    }

    /// <summary>Start an agent that groups the done column's cards into stories (WOR-0074): a doc each in the
    /// project's docs folder and a Story card in the column, the cards themselves to Completed. It asks first.</summary>
    public void WrapUp()
    {
        if (Folder is not { } folder || _board?.Columns.LastOrDefault() is not { } done || StartAgent is null) return;
        var inColumn = _board.Lists.GetValueOrDefault(done.Id) ?? [];
        var busy = inColumn.Where(c => AgentFor?.Invoke(c.Id) is not null).Select(c => c.Id).ToList();
        var cards = Stories.ToWrap(inColumn, busy);
        if (cards.Count == 0) { _notice = $"Nothing in {done.Name} to wrap up: only stories are there."; Apply(); return; }
        var stories = Directory.Exists(_project.Docs) ? Stories.Folder(_project) : null;
        string? reports = ReportStore.FolderFor(_project.Id);
        if (!Directory.Exists(reports)) reports = null;
        var planFiles = PlanStore.FolderFor(_project) is { } pf
            ? PlanStore.List(pf).Where(p => cards.Any(c => PlanRules.PlanOf([p], c.Id) is not null)).Select(p => p.File).ToList() : [];
        StartAgent(Stories.Prompt(_project.Name, folder, done, cards, inColumn.Where(c => c.IsStory).ToList(), busy, stories, reports,
                _project.ExistingRepos(), Core.Commits.For(_project, folder), stories is null ? null : Core.Commits.For(_project, stories), planFiles),
            folder, $"Wrap up {_project.Name}'s {done.Name} into stories", stories is null ? [] : [_project.Docs!]);
    }

    /// <summary>What the sorting agent is asked: read the cards, propose boards, ask, then make them in
    /// Tatty's format and move the cards (IDs kept, cards with a live agent left alone).</summary>
    public static string SortPrompt(string projectName, string boardsFolder, IReadOnlyList<string> boards, IReadOnlyList<string> busy, string? hint, CommitRule? commits = null)
    {
        var lines = new List<string>
        {
            $"Sort the {projectName} project's cards into boards. Its boards are folders in {boardsFolder} (now: {string.Join(", ", boards)}); " +
            "this is your working folder.",
            "",
            "1. Read every card on every board. Skip Completed cards unless they decide a grouping.",
            "2. Work out how best to organise them: a board per story, a handful (usually 2 to 6) that each hold one piece of work " +
            "someone would name in a line and sit down to do together (a feature, an area of the app). Every card goes on exactly one board. " +
            "Keep a board that already fits rather than renaming it. " +
            (StoryBoards.Inbox(boards) is { } inbox
                ? $"Keep the {inbox} board: it is the inbox, where new cards land before Darg moves each to its story, and it keeps the cards that fit no story."
                : ""),
            "3. Ask me with AskUserQuestion before changing anything. Put the proposal in the question itself, since I may only see " +
            "the question: each board's name, one line on what it holds, and the card IDs going there. Make your proposal the " +
            "first option, marked (Recommended); add one or two real alternatives (fewer boards, or a different cut) and a " +
            "\"Leave the boards as they are\" option.",
            "4. Then make the boards and move the cards, exactly as Darg does:",
            "   - A board is a folder with board.json and an items folder. Copy the columns of the board the cards come from " +
            "(same names, ids c1, c2 and on), the same prefix, \"version\": 1, nextId one past the highest number used on any board " +
            "with that prefix, and order {\"backlog\": []}. A board's name is its folder's name: none of / \\ : * ? \" < > |.",
            "   - Moving a card: move its file from <old board>/items to <new board>/items unchanged (it keeps its ID and file name), " +
            "remove its ID from every list in the old board.json's order, and add it to the new board.json's order under the list " +
            "it belongs in: the column whose name matches its Status, else backlog (then set its Status line to Backlog).",
            "   - Don't renumber, rename or delete any card. If a board ends up empty, leave it and say so; I'll decide.",
            "5. When you stop, list the boards and what moved where. Darg shows the changes as you make them. " +
            (commits is { } rule ? Core.Commits.Describe(rule, boardsFolder) : "Never commit or push."),
        };
        if (busy.Count > 0)
            lines.AddRange(["", $"Agents are working on {string.Join(", ", busy)} right now: leave those cards on the board they're on (their agents hold the file's path)."]);
        if (hint is { Length: > 0 }) lines.AddRange(["", $"How I'd like it sorted: {hint}"]);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The boards in the folder, re-read (an agent or Tatty may have made one); the shown one stays.</summary>
    void RefreshBoards()
    {
        var now = BoardOrder.Arrange(BoardStore.List(_project.Boards), _order ??= BoardOrder.Load(_project.Id));
        // the picked board stays listed even once gone from disk, where it was, so the pick isn't lost under it
        if (SelectedBoard is { } keep && !now.Contains(keep) && Boards.IndexOf(keep) is var was and >= 0) now.Insert(Math.Min(was, now.Count), keep);
        ArrangeBoards(now);
        if (SelectedBoard is null && Boards.Count > 0) SelectedBoard = Boards[0];
        OnPropertyChanged(nameof(OtherBoards));
        OnPropertyChanged(nameof(HasAllTab));
        SyncRows();
    }

    /// <summary>The tabs' order as kept (WOR-0213, <see cref="BoardOrder"/>): dragged, or sorted once.</summary>
    List<string>? _order;

    /// <summary><see cref="Boards"/> (and the tabs) put in this order, moving rather than rebuilding.</summary>
    void ArrangeBoards(IReadOnlyList<string> want)
    {
        if (want.SequenceEqual(Boards)) return;
        foreach (var b in Boards.Except(want).ToList()) Boards.Remove(b);
        for (var i = 0; i < want.Count; i++)
        {
            var at = Boards.IndexOf(want[i]);
            if (at == i) continue;
            if (at < 0) Boards.Insert(i, want[i]); else Boards.Move(at, i);
        }
    }

    /// <summary>A tab dragged from one place in the row to another (WOR-0213): that order is kept.</summary>
    public void MoveBoard(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Boards.Count || to >= Boards.Count) return;
        Boards.Move(from, to);
        if (Tabs.Count == Boards.Count) Tabs.Move(from, to);
        KeepOrder();
    }

    /// <summary>The tabs A to Z, once: that becomes the kept order, which dragging then carries on from.</summary>
    [RelayCommand] void SortBoardsByName() => SortBoards(BoardOrder.ByName(Boards), "A to Z");

    /// <summary>The tabs oldest board first, once (by when each board's folder was made).</summary>
    [RelayCommand] void SortBoardsByAge() { if (_project.Boards is { } f) SortBoards(BoardOrder.ByAge(f, Boards), "oldest first"); }

    void SortBoards(IReadOnlyList<string> order, string how)
    {
        ArrangeBoards(order);
        KeepOrder();
        _notice = $"Boards sorted {how}. Drag a tab to move it.";
        Apply();
    }

    /// <summary>The sort buttons show once there are two boards to sort.</summary>
    public bool ShowsBoardSort => Boards.Count > 1;

    void KeepOrder()
    {
        _order = [.. Boards];
        BoardOrder.Save(_project.Id, _order);
        SyncRows(); // the rows below the picked one follow the tabs' order
    }

    /// <summary>The project's other boards, for "Move to board".</summary>
    public IReadOnlyList<string> OtherBoards => Root.Boards.Where(b => b != SelectedBoard).ToList();

    /// <summary>The rows and the ticks brought up to date with the boards on disk, the picked one and the others shown.</summary>
    void SyncRows()
    {
        if (_parent is not null) return;
        var others = Boards.Where(b => b != SelectedBoard).ToList();
        if (!Boards.SequenceEqual(Tabs.Select(t => t.Name)))
        {
            // the same tabs moved, not new ones, so a sort or a drag doesn't redraw the row
            var had = Tabs.ToDictionary(t => t.Name);
            ListSync.Sync(Tabs, Boards.Select(b => had.GetValueOrDefault(b) ?? new BoardTab(this, b)).ToList());
        }
        foreach (var t in Tabs) t.IsShown = !ShowingAll && (t.Name == SelectedBoard || _also.Contains(t.Name));
        // the All board (WOR-0214) is every board as a row, drawn as one
        var want = ShowingAll ? others : others.Where(_also.Contains).ToList();
        foreach (var row in Rows.Skip(1).Where(r => !want.Contains(r.SelectedBoard!)).ToList()) { Rows.Remove(row); row.Dispose(); }
        for (var i = 0; i < want.Count; i++)
        {
            var at = Rows.Skip(1).ToList().FindIndex(r => r.SelectedBoard == want[i]);
            if (at == i) continue;
            if (at >= 0) Rows.Move(at + 1, i + 1);
            else Rows.Insert(i + 1, new BoardViewModel(this, want[i]));
        }
        foreach (var r in Rows) r.ShowsHeader = Rows.Count > 1;
        OnPropertyChanged(nameof(ShowsSectionSplit));
        FitRows();
        SyncLabels();
        ApplyAll();
    }

    /// <summary>A list on any row, by its <see cref="ListViewModel.Key"/>.</summary>
    public ListViewModel? ListByKey(string key) =>
        Root.Rows.SelectMany(r => r.Lists).Concat(Root._allRow?.Lists ?? []).FirstOrDefault(l => l.Key == key);

    /// <summary>Each row's height worked out again: those dragged keep theirs, the rest share what is left.</summary>
    void FitRows()
    {
        var root = Root;
        if (root._rowsSpace <= 0) return;
        if (root._allRow is { } all) all.RowHeight = Math.Max(root._rowsSpace, BoardRowHeights.Min);
        var heights = root._heights ??= BoardRowHeights.Load(_project.Id);
        var fit = BoardRowHeights.Fit(root._rowsSpace,
            root.Rows.Select(r => r.SelectedBoard is { } b && heights.TryGetValue(b, out var h) ? h : (double?)null).ToList());
        for (var i = 0; i < fit.Length; i++) root.Rows[i].RowHeight = fit[i];
    }

    /// <summary>A row's divider dragged: the row takes that height (no less than <see cref="BoardRowHeights.Min"/>),
    /// kept for its board once <paramref name="save"/> (the drag let go); the others share again.</summary>
    public void ResizeRow(double height, bool save)
    {
        if (SelectedBoard is not { } board) return;
        var root = Root;
        var heights = root._heights ??= BoardRowHeights.Load(_project.Id);
        heights[board] = Math.Max(BoardRowHeights.Min, Math.Round(height));
        FitRows();
        if (save) BoardRowHeights.Save(_project.Id, heights);
    }

    /// <summary>A divider double-clicked: its row goes back to sharing the space with the others.</summary>
    [RelayCommand]
    public void ResetRowHeight()
    {
        if (SelectedBoard is not { } board) return;
        var heights = Root._heights ??= BoardRowHeights.Load(_project.Id);
        if (!heights.Remove(board)) return;
        FitRows();
        BoardRowHeights.Save(_project.Id, heights);
    }

    /// <summary>Show a board as a row of its own beside the picked one, or stop showing it.</summary>
    public void SetShown(string board, bool shown)
    {
        if (shown ? !_also.Add(board) : !_also.Remove(board)) return;
        ShownBoards.Save(_project.Id, _also.Order(StringComparer.OrdinalIgnoreCase));
        SyncRows();
    }

    /// <summary>A tab's click (WOR-0149): plain shows only that board; Ctrl adds it to those showing, or takes it off
    /// (never the last one showing). The picked board is the first row, so taking it off promotes the next.</summary>
    public void PickBoard(string board, bool additive)
    {
        // from the All board (WOR-0214), any tab shows just that board
        if (ShowingAll) { SetAll(false); additive = false; }
        if (!additive)
        {
            _also.Clear();
            ShownBoards.Save(_project.Id, []);
            if (SelectedBoard == board) SyncRows(); else SelectedBoard = board; // the change syncs
            return;
        }
        if (board == SelectedBoard)
        {
            var next = Boards.FirstOrDefault(_also.Contains);
            if (next is null) return;
            _also.Remove(next);
            ShownBoards.Save(_project.Id, _also.Order(StringComparer.OrdinalIgnoreCase));
            SelectedBoard = next;

            return;
        }
        SetShown(board, !_also.Contains(board));
    }

    /// <summary>Show every board at once.</summary>
    [RelayCommand]
    void ShowAllBoards()
    {
        foreach (var b in Boards) _also.Add(b);
        ShownBoards.Save(_project.Id, _also.Order(StringComparer.OrdinalIgnoreCase));
        SyncRows();
    }

    /// <summary>Back to the picked board alone.</summary>
    [RelayCommand]
    void ShowOneBoard()
    {
        _also.Clear();
        ShownBoards.Save(_project.Id, []);
        SyncRows();
    }

    /// <summary>A row's ✕: stop showing its board.</summary>
    [RelayCommand] void HideRow() { if (_parent is not null && SelectedBoard is { } b) _parent.SetShown(b, false); }

    /// <summary>The board new cards land on (a note's ticket, + Card on it), from where Haiku moves each to its story's
    /// board (<see cref="StoryBoards"/>). The picked board while there's only one.</summary>
    public string? InboxFolder => _project.Boards is null || StoryBoards.Inbox(BoardOrder.ByName(Root.Boards)) is not { } inbox ? Folder : Path.Combine(_project.Boards, inbox);

    /// <summary>A card was just made on <paramref name="boardFolder"/>: if that's the inbox and the project has other
    /// boards, Haiku moves it to the story it fits once <paramref name="after"/> (its retitling) is done.</summary>
    public void PlaceLater(string boardFolder, string cardId, Task? after = null)
    {
        if (_project.Boards is not { } boards || Root.Boards.Count < 2 || StoryBoards.Inbox(BoardOrder.ByName(Root.Boards)) is not { } inbox) return;
        if (!string.Equals(Path.GetFullPath(boardFolder).TrimEnd('\\', '/'), Path.GetFullPath(Path.Combine(boards, inbox)).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
        var root = Root;
        _ = Run();
        async Task Run()
        {
            if (after is not null) { try { await after; } catch (Exception) { } }
            var to = await StoryBoards.Place(boards, inbox, cardId, id => root.AgentFor?.Invoke(id) is not null);
            if (to is null) return;
            Dispatcher.UIThread.Post(() => { root._notice = $"Haiku put {cardId} on {to}"; root.ApplyAll(); });
        }
    }

    /// <summary>Move a card to another of the project's boards (it keeps its ID).</summary>
    public void MoveToBoard(CardViewModel card, string board)
    {
        if (Folder is null || _project.Boards is null) return;
        try { BoardStore.Transfer(Folder, Path.Combine(_project.Boards, board), card.Id); }
        catch (Exception e) { _notice = $"Couldn't move {card.Id}: {e.Message}"; Apply(); return; }
        _notice = $"Moved {card.Id} to {board}";
        Reload();
    }
    public void Open(CardViewModel card) => Root.Editor = new CardEditor(this, card);

    /// <summary>Each card's board (its story, WOR-0113), by card id, from the boards on disk.</summary>
    public Dictionary<string, string> CardBoards()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_project.Boards is null) return map;
        foreach (var b in BoardStore.List(_project.Boards))
            foreach (var c in BoardStore.ReadCards(Path.Combine(_project.Boards, b))) map.TryAdd(c.Id, b);
        return map;
    }

    /// <summary>Open a card by its ID, on whichever board holds it (clearing filters that hide it).</summary>
    public bool OpenById(string id)
    {
        if (Show(id) is not { } card) return false;
        card.Board.Open(card);
        return true;
    }

    /// <summary>The card with this ID, on whichever board holds it, brought into view (its board picked, filters
    /// that hide it cleared); null, with the status saying so, when no board here has it.</summary>
    public CardViewModel? Show(string id)
    {
        foreach (var b in Boards.ToList())
        {
            if (_project.Boards is null) break;
            if (!BoardStore.ReadCards(Path.Combine(_project.Boards, b)).Any(c => c.Id == id)) continue;
            // a board showing as a row stays where it is; another becomes the picked one
            var row = Rows.FirstOrDefault(r => r.SelectedBoard == b);
            if (row is null) { SelectedBoard = b; row = this; }
            // a card made a moment ago (a ticket from a note) is on disk before the watcher's debounced
            // reload has run, so the lists don't have it yet: read the board now rather than find nothing
            if (row._board?.AllCards.Any(c => c.Id == id) != true) row.Reload();
            Filter = "";
            foreach (var l in Labels) l.Mode = ChipMode.Off;
            if (!ShowCompleted && row._board?.Lists[Board.Completed].Any(c => c.Id == id) == true) ShowCompleted = true;
            return row.Lists.SelectMany(l => l.Cards).FirstOrDefault(c => c.Id == id);
        }
        Status = $"No card {id} on this project's boards.";
        return null;
    }

    public void MarkDone(CardViewModel card)
    {
        if (_board is null) return;
        Move(card, _board.Columns.LastOrDefault()?.Id ?? Board.Completed);
    }

    public BoardViewModel(ProjectConfig project)
    {
        _project = project;
        Rows.Add(this);
        foreach (var b in ShownBoards.Load(project.Id)) _also.Add(b);
        foreach (var b in BoardOrder.Arrange(BoardStore.List(project.Boards), _order = BoardOrder.Load(project.Id))) Boards.Add(b);
        Boards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowsBoardSort));
        if (Views.All && Boards.Count > 1) { _allRow = new BoardViewModel(this, all: true); _allRows.Add(_allRow); ShowingAll = true; }
        SelectedBoard = Boards.FirstOrDefault();
        if (Boards.Count == 0) Status = project.Boards is null ? "This project has no boards folder set." : $"No boards in {project.Boards}. + Board makes one.";
        WatchBoards();
        SyncRows();
        _made = true;
    }

    /// <summary>A further row: another board shown with the picked one. Its callbacks are the root's, read through
    /// (rows kept from the last session are made before the project sets them).</summary>
    BoardViewModel(BoardViewModel parent, string board)
    {
        _project = parent._project;
        _parent = parent;
        ShowsHeader = true;
        SelectedBoard = board;
    }

    /// <summary>A board made or removed outside Darg (an agent sorting cards, Tatty) shows in the list.</summary>
    void WatchBoards()
    {
        if (_project.Boards is null || !Directory.Exists(_project.Boards)) return;
        var t = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (s, _) => { ((DispatcherTimer)s!).Stop(); RefreshBoards(); });
        _watchBoards = new FileSystemWatcher(_project.Boards, "board.json") { IncludeSubdirectories = true, EnableRaisingEvents = true };
        void Poke(object? s, FileSystemEventArgs e) { StallWatch.Shared.Count("Watch.BoardJson"); Dispatcher.UIThread.Post(() => { t.Stop(); t.Start(); }); }
        _watchBoards.Created += Poke; _watchBoards.Deleted += Poke; _watchBoards.Renamed += (s, e) => Poke(s, e);
        // WOR-0172: a card moved on any board may clear (or reopen) a blocker shown on another
        _watchBoards.Changed += (_, _) => Dispatcher.UIThread.Post(BoardJsonChanged);
    }

    partial void OnSelectedBoardChanged(string? value)
    {
        _notice = null;
        OnPropertyChanged(nameof(OtherBoards));
        SplitByState = value is not null && Views.Split.Contains(value);
        LeaveAll(); // a board picked some other way than its tab (Design's link, a new board) shows that board
        Watch();
        Reload();
        SyncRows(); // the picked board leaves the rows below it, if it was one
    }
    partial void OnFilterChanged(string value) { _notice = null; ApplyAll(); }
    partial void OnShowCompletedChanged(bool value) { _notice = null; ApplyAll(); }

    /// <summary>Every row through the filter again.</summary>
    void ApplyAll() { foreach (var r in Root.Rows) r.Apply(); }

    // picks are each row's own, but the toolbar counts and acts on them all
    int PickedCount => Root.Rows.Sum(r => r._picked.Count);
    public bool HasPicked => PickedCount > 0;
    public string PickedText => PickedCount == 1 ? "1 picked" : $"{PickedCount} picked";
    IEnumerable<CardViewModel> Shown() => Lists.SelectMany(l => l.Cards);
    int ToStart => Root.Rows.Sum(r => r.Shown().Count(c => c.IsPicked && c.CanStart));
    /// <summary>"▶ Work on 3", counting only picked cards with no agent on them yet.</summary>
    public string WorkOnPickedText => $"▶ Work on {ToStart}";
    public bool CanWorkOnPicked => ToStart > 0;
    /// <summary>"✦ Improve 3": the same cards ▶ Work on would start (an agent at work on a card is left to it).</summary>
    public string ImprovePickedText => $"✦ Improve {ToStart}";

    /// <summary>A click on a card: with Ctrl or Shift held, or once anything is picked, it picks or unpicks
    /// the card; otherwise it opens the card.</summary>
    public void Click(CardViewModel card, Avalonia.Input.KeyModifiers keys)
    {
        const Avalonia.Input.KeyModifiers pick = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift | Avalonia.Input.KeyModifiers.Meta;
        if (!HasPicked && (keys & pick) == 0) { Open(card); return; }
        Root.Editor = null;
        _notice = null;
        card.IsPicked = !card.IsPicked;
        if (card.IsPicked) _picked.Add(card.Id); else _picked.Remove(card.Id);
        PickedChanged();
    }

    void PickedChanged()
    {
        foreach (var l in Lists) l.PickedChanged();
        var root = Root;
        if (root.ShowingAll && root._allRow is { } all && all != this) foreach (var l in all.Lists) l.PickedChanged();
        root.OnPropertyChanged(nameof(HasPicked));
        root.OnPropertyChanged(nameof(PickedText));
        root.OnPropertyChanged(nameof(WorkOnPickedText));
        root.OnPropertyChanged(nameof(CanWorkOnPicked));
        root.OnPropertyChanged(nameof(ImprovePickedText));
    }

    /// <summary>Pick every card showing, on every row (the filter, and Show completed, decide what "all" is).</summary>
    [RelayCommand]
    void PickAll()
    {
        Root.Editor = null;
        foreach (var r in Root.Rows)
        {
            r._notice = null;
            foreach (var c in r.Shown()) { c.IsPicked = true; r._picked.Add(c.Id); }
            r.PickedChanged();
        }
    }

    /// <summary>Pick every card showing in one column, or unpick them all if they already are.</summary>
    public void PickList(ListViewModel list)
    {
        if (list.Cards.Count == 0) return;
        Root.Editor = null;
        _notice = null;
        var pick = !list.AllPicked;
        foreach (var c in list.Cards)
        {
            c.IsPicked = pick;
            // a card's pick is its own row's (on the All board's lists, the row of the card's board)
            if (pick) c.Board._picked.Add(c.Id); else c.Board._picked.Remove(c.Id);
        }
        PickedChanged();
    }

    [RelayCommand]
    public void ClearPicked()
    {
        foreach (var r in Root.Rows)
        {
            foreach (var c in r.Shown()) c.IsPicked = false;
            r._picked.Clear();
            r.PickedChanged();
        }
    }

    /// <summary>Start an agent on every picked card that has none, on every row, all at once in the project's
    /// folder; each is told which other cards are being worked on beside it. The board stays showing, and each
    /// card shows its agent's state.</summary>
    [RelayCommand]
    void WorkOnPicked()
    {
        var picked = Root.Rows.Select(r => (Row: r, Cards: r.Shown().Where(c => c.IsPicked && c.CanStart).Select(c => c.Card).ToList()))
            .Where(p => p.Cards.Count > 0).ToList();
        if (picked.Count == 0) return;
        // a picked card with an open blocker is left out and named (WOR-0173); Work on alone on it asks instead
        BlockersStale();
        var split = picked.Select(p => { var (free, blocked) = p.Row.SplitBlocked(p.Cards, force: false); return (p.Row, Free: free, Blocked: blocked); }).ToList();
        var all = split.SelectMany(p => p.Free).Select(c => c.Id).ToList();
        foreach (var (row, free, blocked) in split)
        {
            row._picked.Clear();
            if (free.Count > 0) row.WorkOnAll(free, all, blocked: blocked);
            else { row._notice = "Started nothing; " + BlockedStart.LeftOut(blocked); row.Apply(); }
        }
        if (split.Count(p => p.Free.Count > 0) > 1)
        {
            var leftOut = split.SelectMany(p => p.Blocked).ToList();
            Root._notice = $"Started {all.Count} agents on {split.Count(p => p.Free.Count > 0)} boards" + (leftOut.Count > 0 ? "; " + BlockedStart.LeftOut(leftOut) : "");
            Root.Apply();
        }
    }

    /// <summary>✦ Improve N (WOR-0099): an Improve agent on every picked card with no agent at work on it, on
    /// every row, all at once, one per card. The picks are cleared and the board stays showing; the agents are in
    /// Agents, named "Improve WOR-… title", and any that asks a question notifies.</summary>
    [RelayCommand]
    void ImprovePicked()
    {
        var picked = Root.Rows.Select(r => (Row: r, Cards: r.Shown().Where(c => c.IsPicked && c.CanStart).Select(c => c.Card).ToList()))
            .Where(p => p.Cards.Count > 0).ToList();
        if (picked.Count == 0) return;
        var started = new List<string>();
        using (StallWatch.Shared.Section("Improve picked"))
            foreach (var (row, cards) in picked)
            {
                row._picked.Clear();
                foreach (var card in cards)
                {
                    try { row.StartImprove(card); }
                    catch (Exception e) { Root._notice = $"Improving {Ids(started)}; stopped at {card.Id}: {e.Message}"; ApplyAll(); return; }
                    started.Add(card.Id);
                }
            }
        Root._notice = started.Count == 1 ? $"Improving {started[0]}: its agent is in Agents" : $"Improving {started.Count} cards, an agent each in Agents: {Ids(started)}";
        ApplyAll();
        static string Ids(List<string> ids) => ids.Count == 0 ? "none" : string.Join(", ", ids);
    }

    /// <summary>The cards with these ids on the boards showing, by the row that shows each (WOR-0214: with All showing that
    /// is every board, so a card off the board picked underneath is found and started on its own board), leaving out
    /// those with an agent and those done.</summary>
    List<(BoardViewModel Row, List<Card> Cards)> CardsByRow(IReadOnlyList<string> ids) =>
        Root.Rows.Select(r => (Row: r, Cards: r._board is not { } board ? [] : ids
                .Select(id => board.AllCards.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).OfType<Card>()
                .Where(c => AgentFor?.Invoke(c.Id) is null && !r.IsDone(c.Id)).DistinctBy(c => c.Id).ToList()))
            .Where(p => p.Cards.Count > 0).ToList();

    /// <summary>Start agents on cards of the boards showing by id, together, as ▶ Work on N does (the API's work-on
    /// with several cards), each on its own board. Cards that already have an agent are left out. Says what was started.</summary>
    public string WorkOnCards(IReadOnlyList<string> ids, bool force = false)
    {
        if (Root._board is null) return "No board is showing.";
        var byRow = Root.CardsByRow(ids);
        if (byRow.Count == 0) return "None of those cards is on the board without an agent and outside Done (a card in Done must be moved out first).";
        BlockersStale(); // as things are now, not as of the last reload
        var split = byRow.Select(p => { var (free, blocked) = p.Row.SplitBlocked(p.Cards, force); return (p.Row, Free: free, Blocked: blocked); }).ToList();
        if (split.All(p => p.Free.Count == 0))
            throw new ApiError("Started nothing: " + string.Join(" ", split.SelectMany(p => p.Blocked).Select(b => BlockedStart.Refusal(b.Id, b.Open))));
        var all = split.SelectMany(p => p.Free).Select(c => c.Id).ToList();
        var said = new List<string>();
        foreach (var (row, free, blocked) in split)
            if (free.Count > 0) { row.WorkOnAll(free, all, blocked: blocked); said.Add(row._notice ?? ""); }
            else said.Add("Started nothing on " + row.SelectedBoard + "; " + BlockedStart.LeftOut(blocked));
        return string.Join(" ", said.Where(x => x.Length > 0));
    }

    /// <summary>Improve agents on cards of the boards showing by id, one each, as ✦ Improve N does (the API's
    /// improve-card with several cards), each on its own board. Cards with an agent at work are left out. Says what was started.</summary>
    public string ImproveCards(IReadOnlyList<string> ids)
    {
        if (Root._board is null) return "No board is showing.";
        var byRow = Root.CardsByRow(ids);
        if (byRow.Count == 0) return "None of those cards is on the board without an agent and outside Done (a card in Done must be moved out first).";
        foreach (var (row, cards) in byRow)
            foreach (var card in cards) row.StartImprove(card);
        var started = byRow.SelectMany(p => p.Cards).Select(c => c.Id).ToList();
        Root._notice = started.Count == 1 ? $"Improving {started[0]}: its agent is in Agents" : $"Improving {started.Count} cards, an agent each in Agents: {string.Join(", ", started)}";
        Root.ApplyAll();
        return Root._notice;
    }

    /// <summary>Start an agent on each card, all together; a card that fails to start is named in the status line and the
    /// rest still start (WOR-0125: one bad card shouldn't stop a Work All).</summary>
    void WorkOnAll(List<Card> cards, IReadOnlyList<string>? alongside = null, bool bulk = false,
        IReadOnlyList<(string Id, IReadOnlyList<BlockerState> Open)>? blocked = null)
    {
        using var timing = StallWatch.Shared.Section("Work on picked");
        var started = new List<string>();
        var failed = new List<(string Id, string Why)>();
        var together = alongside ?? cards.Select(c => c.Id).ToList();
        foreach (var card in cards)
        {
            try { StartWork(card, together.Where(id => id != card.Id).ToList(), show: false, reload: false); }
            catch (Exception e) { failed.Add((card.Id, e.Message)); continue; }
            if (AgentFor?.Invoke(card.Id) is null) failed.Add((card.Id, "no agent started"));
            else started.Add(card.Id);
        }
        _notice = bulk || failed.Count > 0 || blocked is { Count: > 0 } ? BulkStart.Summary(BulkStart.Kind.Work, started, failed, blocked)
            : started.Count == 1 ? $"Started an agent on {started[0]}" : $"Started {started.Count} agents: {string.Join(", ", started)}";
        Reload();
    }

    /// <summary>The cards a column's bulk button would start (WOR-0125), among those showing on this row: for Work All,
    /// every card in the backlog and the working column that its own ▶ Work on is offered on (no live agent, not done);
    /// for QA Run All, every card in the QA column its own 🧪 Run QA is offered on (no QA agent running). Work All leaves
    /// out a card with an open blocker (WOR-0173): <see cref="BulkBlocked"/> lists those.</summary>
    List<CardViewModel> BulkCards(BulkStart.Kind kind) =>
        kind == BulkStart.Kind.Work ? BulkCandidates(kind).Where(c => OpenBlockers(c.Card).Count == 0).ToList() : BulkCandidates(kind);

    /// <summary>The cards Work All would start but for an open blocker, each with what it waits on (WOR-0173).</summary>
    List<(string Id, IReadOnlyList<BlockerState> Open)> BulkBlocked(BulkStart.Kind kind) =>
        kind != BulkStart.Kind.Work ? [] : BulkCandidates(kind).Select(c => (c.Id, OpenBlockers(c.Card))).Where(b => b.Item2.Count > 0).ToList();

    List<CardViewModel> BulkCandidates(BulkStart.Kind kind)
    {
        if (_board is null) return [];
        IReadOnlyList<string> lists = kind switch
        {
            BulkStart.Kind.Work => BulkStart.WorkLists(_board.Columns),
            BulkStart.Kind.Qa when BulkStart.QaList(_board.Columns) is { } qa => [qa],
            BulkStart.Kind.Improve => [Core.Board.Backlog],
            _ => [],
        };
        return Lists.Where(l => lists.Contains(l.Id)).SelectMany(l => l.Cards)
            .Where(c => kind == BulkStart.Kind.Qa ? c.ShowRunQa : kind == BulkStart.Kind.Improve ? c.ShowImprove : c.CanStart).DistinctBy(c => c.Id).ToList();
    }

    public BulkStart.Kind BulkKindOf(string listId) => _board is null ? BulkStart.Kind.None : BulkStart.For(listId, _board.Columns);
    public int BulkCount(BulkStart.Kind kind) => kind == BulkStart.Kind.None ? 0 : BulkCards(kind).Count;
    /// <summary>How many cards Work All leaves out for an open blocker (WOR-0173): its label says so.</summary>
    public int BulkBlockedCount(BulkStart.Kind kind) => BulkBlocked(kind).Count;

    /// <summary>Work All / QA Run All (WOR-0125): start every card the button counts, at once, the board staying on
    /// screen. Work All moves backlog cards to In Progress as Work on does, and tells each agent the others beside it.
    /// What started and what didn't goes in the status line, with no dialog per card. Returns that line. Work All leaves
    /// out cards with an open blocker and names them (WOR-0173), unless <paramref name="force"/> (the API's).</summary>
    public string StartBulk(BulkStart.Kind kind, bool force = false)
    {
        BlockersStale(); // as things are now: a blocker may have reached QA on a board this one hasn't reloaded for
        var cards = (force ? BulkCandidates(kind) : BulkCards(kind)).Select(c => c.Card).ToList();
        List<(string Id, IReadOnlyList<BlockerState> Open)> blocked = force ? [] : BulkBlocked(kind);
        if (cards.Count == 0)
        {
            _notice = kind == BulkStart.Kind.Qa ? "Nothing to QA: every card in QA has a QA agent on it"
                : kind == BulkStart.Kind.Improve ? "Nothing to improve: every thin card here has an agent on it, or none is thin"
                : blocked.Count > 0 ? "Nothing to start: " + BlockedStart.LeftOut(blocked) + " (Work on one to start it anyway)"
                : "Nothing to start: every card here has an agent on it";
            Apply();
            return _notice;
        }
        Root.Editor = null; // its panel, left open, could save over what an agent writes into the card
        if (kind == BulkStart.Kind.Work) { WorkOnAll(cards, bulk: true, blocked: blocked); return _notice ?? ""; }
        using var timing = StallWatch.Shared.Section(kind == BulkStart.Kind.Improve ? "Improve all" : "QA run all");
        var started = new List<string>();
        var failed = new List<(string Id, string Why)>();
        foreach (var card in cards)
        {
            try { if (kind == BulkStart.Kind.Improve) StartImprove(card); else StartQa(card); }
            catch (Exception e) { failed.Add((card.Id, e.Message)); continue; }
            if (kind == BulkStart.Kind.Improve ? AgentFor?.Invoke(card.Id) is not null : QaRunning?.Invoke(card.Id) == true) started.Add(card.Id);
            else failed.Add((card.Id, kind == BulkStart.Kind.Improve ? "no agent started" : "no QA agent started"));
        }
        _notice = BulkStart.Summary(kind, started, failed);
        ReloadAll();
        return _notice;
    }

    public string? Folder => SelectedBoard is null || _project.Boards is null ? null : Path.Combine(_project.Boards, SelectedBoard);

    /// <summary>The list whose header is being edited (a rename, or a remove waiting to be confirmed).
    /// While it is, reloads wait: other agents change cards all the time, and a reload rebuilds the lists,
    /// which would throw away the half-typed name.</summary>
    ListViewModel? _held;
    bool _reloadWaiting;
    public void Hold(ListViewModel list)
    {
        if (_held is not null && _held != list) { _held.IsRenaming = false; _held.ConfirmingRemove = false; }
        _held = list;
    }
    public void Release()
    {
        _held = null;
        if (_reloadWaiting) Reload();
    }

    public void SetMinimized(ListViewModel list, bool minimized)
    {
        if (Folder is null) return;
        try { BoardStore.SetMinimized(Folder, list.Id, minimized); }
        catch (Exception e) { Status = $"Couldn't {(minimized ? "minimize" : "open")} {list.Name}: {e.Message}"; return; }
        Reload();
    }

    public void Rename(ListViewModel list, string name)
    {
        if (Folder is null) return;
        if (name.Trim() == list.Name) { list.CancelRenameCommand.Execute(null); return; }
        try { BoardStore.RenameColumn(Folder, list.Id, name); }
        catch (Exception e) { list.Error = e.Message; return; }
        _notice = null;
        Reload();
    }

    public void RemoveColumn(ListViewModel list)
    {
        if (Folder is null) return;
        int moved;
        try { moved = BoardStore.RemoveColumn(Folder, list.Id); }
        catch (Exception e) { list.Error = e.Message; return; }
        _notice = moved == 0 ? $"Removed {list.Name}" : $"Removed {list.Name}; {(moved == 1 ? "its card is" : $"its {moved} cards are")} in the backlog";
        Reload();
    }

    /// <summary>Add a column before the last one (the last column is the done column, for Tatty and for
    /// Mark done), named "New column" (or "New column 2"…), with its name open to type over.</summary>
    [RelayCommand]
    void AddColumn()
    {
        if (Folder is null || _board is null) return;
        var name = "New column";
        for (var n = 2; _board.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++) name = $"New column {n}";
        string id;
        try { id = BoardStore.AddColumn(Folder, name, _board.Columns.Count >= 2 ? _board.Columns.Count - 1 : null); }
        catch (Exception e) { Status = $"Couldn't add a column: {e.Message}"; return; }
        _notice = null;
        Reload();
        Lists.FirstOrDefault(l => l.Id == id)?.StartRenameCommand.Execute(null);
    }

    /// <summary>The board changed on disk: reload, unless a header is being edited (then once it's done).</summary>
    void Changed()
    {
        if (_held is not null) _reloadWaiting = true; else Reload();
    }

    /// <summary>A card's agent was forgotten: its card drops the link (Work on, flows, moving to another board
    /// come back), on the board and in the card's panel if it's open.</summary>
    public void AgentGone(string cardId)
    {
        foreach (var r in Root.Rows) r.Reload();
        if (Root.Editor is { } e && e.CardId == cardId) e.AgentChanged();
    }

    /// <summary>Reload now. A header being edited is given up (this is a click elsewhere: a move, another board).</summary>
    public void Reload()
    {
        if (_held is not null) { _held.IsRenaming = false; _held.ConfirmingRemove = false; _held = null; }
        _reloadWaiting = false;
        if (Folder is null) return;
        using var timing = StallWatch.Shared.Section("Board.Reload");
        try
        {
            using var loading = StallWatch.Shared.Section("BoardStore.Load");
            BoardStore.EnsureAttentionColumn(Folder);
            _board = BoardStore.Load(Folder);
            // a column change made outside Move (an agent editing the card) is stamped Moved, then read again
            if (_stamps.Read(Folder, _board).Count > 0) _board = BoardStore.Load(Folder);
        }
        catch (Exception e) { Status = $"Couldn't read the board: {e.Message}"; return; }
        BlockersStale();
        NoticeAttention();
        if (Root.SyncLabels()) ApplyAll(); else { Apply(); RedrawBlockedRows(); }
        Root.Reloaded?.Invoke();
    }

    /// <summary>The label chips, one per label on any row's cards; false when they were already those (the chips stay).</summary>
    bool SyncLabels()
    {
        var names = Rows.Where(r => r._board is not null).SelectMany(r => r._board!.AllCards).SelectMany(c => c.Labels)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.SequenceEqual(Labels.Select(l => l.Name))) return false;
        var was = Labels.ToDictionary(l => l.Name, l => l.Mode, StringComparer.OrdinalIgnoreCase);
        foreach (var chip in Labels) chip.Changed -= ApplyAll;
        Labels.Clear();
        foreach (var name in names)
        {
            var chip = new LabelChip(name) { Mode = was.GetValueOrDefault(name) };
            chip.Changed += ApplyAll;
            Labels.Add(chip);
        }
        return true;
    }

    /// <summary>Raised after each reload of the board from disk.</summary>
    public event Action? Reloaded;

    /// <summary>Cards at work on the board on screen: those in a column after the first and before the last (the
    /// done one), so In Progress, Review and the like, column by column in board order. None on a board of two.</summary>
    public IReadOnlyList<(string Column, Card Card)> InProgress()
    {
        if (_board is null || _board.Columns.Count < 3) return [];
        return _board.Columns.Skip(1).SkipLast(1)
            .SelectMany(col => _board.Lists.TryGetValue(col.Id, out var cards) ? cards.Select(c => (col.Name, c)) : [])
            .ToList();
    }

    /// <summary>How many cards each list of the board on screen holds, in display order (backlog, the columns,
    /// completed), for the API's overview (WOR-0141). None before it loads.</summary>
    public IReadOnlyList<(string Name, int Count)> ColumnCounts() => _board is null ? []
        : _board.ListOrder().Select(l => (l.Name, _board.Lists.TryGetValue(l.Id, out var cards) ? cards.Count : 0)).ToList();

    /// <summary>The shown board's columns (none before it loads).</summary>
    public IReadOnlyList<Column> Columns => _board?.Columns ?? [];

    /// <summary>Every card in the done (last) column of each of the project's boards, with its card file (WOR-0112).</summary>
    /// <param name="completedToo">Cards whose Status is Completed count as well (they are done; IsDone says so).</param>
    public List<(Card Card, string File)> DoneCards(bool completedToo = false)
    {
        var found = new List<(Card, string)>();
        foreach (var b in BoardStore.List(_project.Boards))
        {
            var folder = Path.Combine(_project.Boards!, b);
            try
            {
                var board = BoardStore.Load(folder);
                if (board.Columns.LastOrDefault()?.Name is not { } last) continue;
                var place = BoardStore.PlaceOf(last, board.Columns);
                foreach (var c in board.AllCards.Where(c => BoardStore.PlaceOf(c.Status, board.Columns) is var p && (p == place || completedToo && p == Board.Completed)))
                    found.Add((c, Path.GetFullPath(Path.Combine(folder, "items", c.File))));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }

    /// <summary>The id of every card done on any of the project's boards (WOR-0153: an agent's card may sit on a board that
    /// isn't the one shown, so <see cref="IsDone"/> alone missed it). Read from the boards on disk, like <see cref="CardBoards"/>.</summary>
    public HashSet<string> DoneCardIds() =>
        _project.Boards is null ? [] : DoneCards(completedToo: true).Select(d => d.Card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A card is done when it's in Completed or the board's last column (its done column).</summary>
    public bool IsDone(string cardId)
    {
        if (_board is null) return false;
        var lists = new[] { Board.Completed, _board.Columns.LastOrDefault()?.Id };
        return lists.Any(list => list is not null && _board.Lists.TryGetValue(list, out var cards) && cards.Any(c => c.Id == cardId));
    }

    /// <summary>What every card's menu was made from when the lists were last built (see <see cref="Apply"/>).</summary>
    string? _shape;

    /// <summary>Bring the lists up to date with the board through the filter: text matches the ID, title or
    /// description; labels: every must-have one (double-clicked) and, if any are clicked, one of those.</summary>
    void Apply()
    {
        if (_board is null) return;
        using var timing = StallWatch.Shared.Section("Board.Apply");
        var root = Root;
        var words = root.Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (any, all) = LabelChip.Picked(root.Labels);
        bool Keep(Card c) =>
            LabelFilter.Matches(c.Labels, any, all) &&
            words.All(w => c.Id.Contains(w, StringComparison.OrdinalIgnoreCase) || c.Title.Contains(w, StringComparison.OrdinalIgnoreCase)
                           || c.Description.Contains(w, StringComparison.OrdinalIgnoreCase));
        var targets = _board.ListOrder().Where(l => l.Id != Board.Unsorted).Select(l => (l.Id, l.Name)).ToList();
        // Cards and columns that haven't changed keep their view models, and so their controls: rebuilding every
        // one on each change (an agent editing its card) froze the window for half a second (WOR-0076). A card's
        // menu is made with it from the columns, the other boards and the flows: when those change, all are new.
        var shape = string.Join("\n", targets.Select(t => $"{t.Id}={t.Name}")) + "\n" + string.Join(",", OtherBoards) + "\n"
                    + string.Join(",", FlowList().Select(f => f.Name)) + "\n" + _board.Columns.LastOrDefault()?.Id;
        var reuse = shape == _shape;
        _shape = shape;
        var oldCards = new Dictionary<string, CardViewModel>();
        var oldLists = new Dictionary<string, ListViewModel>();
        if (reuse)
        {
            foreach (var l in Lists) { oldLists.TryAdd(l.Id, l); foreach (var c in l.Cards) oldCards.TryAdd(c.Id, c); }
        }
        var used = new HashSet<CardViewModel>();
        CardViewModel ViewOf(Card c)
        {
            var agent = AgentFor?.Invoke(c.Id);
            var blockers = BlockersFor(c); // WOR-0172: a blocker clearing on any board changes the tile, the card unchanged
            var vm = oldCards.TryGetValue(c.Id, out var old) && old.Agent == agent && old.QaWasRunning == (QaRunning?.Invoke(c.Id) == true) && old.Card.SameAs(c)
                     && old.BlockersKey == CardViewModel.KeyOf(blockers) && old.QueueKey == string.Join(",", QueueChipsFor(c.Id)) && used.Add(old)
                ? old : new CardViewModel(c, targets, this, agent, OtherBoards, blockers);
            vm.IsPicked = _picked.Contains(c.Id);
            vm.ShowsBoard = root.ShowingAll;
            vm.BlockedAsk = _asking is { } ask && ask.Id == c.Id ? ask.Text : null;
            return vm;
        }
        var lists = new List<ListViewModel>();
        var total = 0;
        foreach (var (id, name) in _board.ListOrder())
        {
            if (id == Board.Completed && !root.ShowCompleted) continue;
            var cards = SortOf(SelectedBoard + "/" + id).Apply(_board.Lists[id].Where(Keep)).Select(ViewOf).ToList();
            total += cards.Count;
            var minimized = _board.Minimized.Contains(id);
            if (oldLists.TryGetValue(id, out var list) && list.Name == name && list.IsMinimized == minimized) list.Show(cards, _board.Lists[id].Count);
            else list = new ListViewModel(this, id, name, cards, _board.Lists[id].Count, minimized);
            list.Kind = BoardLayouts.KindOf(_board, id);
            lists.Add(list);
        }
        ListSync.Sync(Lists, lists);
        _picked.IntersectWith(Shown().Select(c => c.Id));
        PickedChanged();
        foreach (var l in Lists) { l.BulkChanged(); l.SortChanged(); l.Regroup(); } // Work All counts across two columns, so every count is read again
        CountText = $"{total} of {_board.AllCards.Count()} cards";
        Status = _notice ?? _board.DuplicatesText ?? CountText;
        BuildMerged();
    }

    /// <summary>Start an agent on a card: it goes to the board's working column ("In Progress", else
    /// the first column) if it was waiting in the backlog or the first column, and the agent opens. A card with an open
    /// blocker (WOR-0173) asks on its tile first, "Blocked by WOR-0172, In Progress. Start anyway?", unless
    /// <paramref name="force"/> (its Start anyway).</summary>
    public void StartWork(CardViewModel card, bool force = false)
    {
        // in QA, Work on only gets the result running to try: nothing new is built, so a blocker doesn't hold it
        if (!force && !InQa(card.Card) && OpenBlockers(card.Card, fresh: true) is { Count: > 0 } open)
        {
            _asking = (card.Id, BlockedStart.Question(open));
            card.BlockedAsk = _asking.Value.Text;
            return;
        }
        if (_asking?.Id == card.Id) _asking = null;
        card.BlockedAsk = null;
        StartWork(card.Card, [], show: false, reload: true);
    }

    /// <summary>The tile asking whether to start a blocked card (WOR-0173): its id and the question; kept here so a
    /// reload, which may build the tile again, keeps asking.</summary>
    (string Id, string Text)? _asking;
    public void CancelBlocked(CardViewModel card)
    {
        if (_asking?.Id == card.Id) _asking = null;
        card.BlockedAsk = null;
    }

    /// <summary>The card's blockers still holding it up (WOR-0173): on any of the project's boards and not yet in QA or done.
    /// <paramref name="fresh"/> reads the boards again now (before starting anything), rather than the kept lookup
    /// (<see cref="BlockIndex"/>, which a board changing makes stale only after a short wait).</summary>
    public IReadOnlyList<BlockerState> OpenBlockers(Card card, bool fresh = false)
    {
        if (card.BlockedBy.Count == 0) return [];
        if (fresh) BlockersStale();
        return Blockers.Open(card, BlockIndex());
    }

    /// <summary>Split cards into those free to start and those an open blocker holds (WOR-0173); with
    /// <paramref name="force"/>, all are free.</summary>
    (List<Card> Free, List<(string Id, IReadOnlyList<BlockerState> Open)> Blocked) SplitBlocked(IEnumerable<Card> cards, bool force)
    {
        var free = new List<Card>();
        var blocked = new List<(string Id, IReadOnlyList<BlockerState> Open)>();
        foreach (var card in cards)
            if (!force && OpenBlockers(card) is { Count: > 0 } open) blocked.Add((card.Id, open));
            else free.Add(card);
        return (free, blocked);
    }

    /// <summary>Cards that entered the attention, QA or done column since the last read of this board's folder, whoever
    /// moved them (an agent editing board.json, a QA check, a drag), are handed to the project, which writes or archives
    /// their reports (WOR-0092, WOR-0102; <see cref="ColumnWatch"/>). The first read of a board only learns who is there.</summary>
    void NoticeAttention()
    {
        if (_board is null || Folder is null) return;
        var moves = _moves.Read(Folder, _board);
        string File(Card c) => Path.GetFullPath(Path.Combine(Folder, "items", c.File));
        foreach (var c in moves.Attention) NeedsAttention?.Invoke(c, File(c));
        foreach (var c in moves.Qa) EnteredQa?.Invoke(c, File(c));
        foreach (var c in moves.Done) EnteredDone?.Invoke(c, File(c));
    }

    /// <summary>Whether a card sits in the board's needs-attention column.</summary>
    public bool InAttention(Card card) =>
        _board is not null && AttentionColumn.Find(_board.Columns) is { } a && BoardStore.PlaceOf(card.Status, _board.Columns) == BoardStore.PlaceOf(a, _board.Columns);

    public bool HasAttentionColumn => _board is not null && AttentionColumn.Find(_board.Columns) is not null;

    /// <summary>Move a card to the board's needs-attention column. Returns what went wrong, or null.</summary>
    public string? MoveToAttention(string cardId, string why = "")
    {
        if (_board is null || AttentionColumn.Find(_board.Columns) is not { } name || _board.Columns.FirstOrDefault(c => c.Name == name) is not { } col) return "This board has no For Human Attention column.";
        // the reason goes into the card first, so the decision report written on landing can quote it (WOR-0111)
        if (why.Trim().Length > 0 && Folder is not null && _board.AllCards.FirstOrDefault(c => c.Id == cardId) is { } card)
        {
            try { QaNotes.Add(Path.GetFullPath(Path.Combine(Folder, "items", card.File)), why, DateTime.Now, QaNotes.AttentionHeading); }
            catch (Exception e) { return $"Couldn't write the reason into the card: {e.Message}"; }
        }
        Move(cardId, col.Id);
        return null;
    }

    /// <summary>Set by the project (WOR-0105): a card approved from its panel, so its report can be marked approved too.</summary>
    public Action<string>? Approved { get; set; }

    /// <summary>Approve a card from its decision report: it goes to the done column. Returns what went wrong, or null.</summary>
    public string? Approve(string cardId)
    {
        foreach (var row in Root.Rows)
            if (row._board?.AllCards.FirstOrDefault(c => c.Id == cardId) is { } card && row._board.Columns.LastOrDefault() is { } done)
            {
                row.Move(card.Id, done.Id);
                return null;
            }
        return $"No card {cardId} on this project's boards.";
    }

    /// <summary>Send a card back from its decision report (the same path as from QA), by id.</summary>
    public string? SendBackById(string cardId, string notes)
    {
        foreach (var row in Root.Rows)
            if (row._board?.AllCards.Any(c => c.Id == cardId) == true && row.Lists.SelectMany(l => l.Cards).FirstOrDefault(c => c.Id == cardId) is { } vm)
                return row.SendBack(vm, notes);
        return $"{cardId} isn't on a board that's showing; open its board first.";
    }

    /// <summary>Set by the project (WOR-0124): a decision was answered with no done, back or stay action (or sent back
    /// while an agent is at work on the card), so the answer goes to the agent that asked, else to a new card agent.
    /// Passed the card, its file, the decision, the answer and the notes.</summary>
    public Action<Card, string, CardDecision, string, string>? DecisionToAgent { get => _parent is null ? field : _parent.DecisionToAgent; set; }

    /// <summary>Set by the project (WOR-0124): a card's decision was settled from the board (the card, the decision
    /// report's new state, the notes), so its report and its notification follow.</summary>
    public Action<string, string, string>? DecisionSettled { get => _parent is null ? field : _parent.DecisionSettled; set; }

    /// <summary>The card's open decision (WOR-0124), read from its file now; null when it has none it could be answered by.</summary>
    public CardDecision? DecisionOf(Card card)
    {
        if (Folder is null) return null;
        try { return CardDecision.Read(File.ReadAllText(Path.GetFullPath(Path.Combine(Folder, "items", card.File)))) is { Valid: true } d ? d : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Every card in an attention column, on any row, with the open decision it waits on (WOR-0169: what its
    /// agent shows too). Read from the card files now.</summary>
    public IReadOnlyList<(Card Card, CardDecision Decision)> OpenDecisions() =>
        Root.Rows.Where(r => r._board is not null).SelectMany(r => r._board!.AllCards.Where(r.InAttention)
                .Select(c => (Card: c, Decision: r.DecisionOf(c))))
            .Where(x => x.Decision is not null).Select(x => (x.Card, x.Decision!)).DistinctBy(x => x.Card.Id).ToList();

    /// <summary>What a card in For Human Attention with no decision recorded asks (WOR-0124): is it right as it stands?</summary>
    public static CardDecision Fallback(Card card) => new($"Is {card.Id} right as it stands?",
        [new("Approve", "Move it to done.", DecisionAction.Done, false), new("Send back", "Say what's wrong in the notes.", DecisionAction.Back, false)], "", "");

    /// <summary>Answer a card's decision (WOR-0124), by id, on whichever row shows it: the answer goes into the card,
    /// then the picked option's action runs (done, send back, stay), or with none the answer goes to the agent.
    /// A card with no decision recorded answers the fallback, Approve or Send back. Returns what went wrong, or null.</summary>
    public string? Decide(string cardId, string answer, string notes)
    {
        foreach (var row in Root.Rows)
            if (row._board?.AllCards.FirstOrDefault(c => c.Id == cardId) is { } card && row.Folder is { } folder)
                return row.DecideOn(card, Path.GetFullPath(Path.Combine(folder, "items", card.File)), answer, notes);
        return $"No card {cardId} on the boards showing.";
    }

    string? DecideOn(Card card, string file, string answer, string notes)
    {
        if (_board is null) return "The board isn't loaded.";
        if (!InAttention(card)) return $"{card.Id} isn't in {AttentionColumn.Find(_board.Columns) ?? BoardStore.AttentionName} ({card.Status}).";
        if (answer.Trim().Length == 0) return "Pick an option or type an answer.";
        var recorded = DecisionOf(card);
        var decision = recorded ?? Fallback(card);
        var option = decision.OptionFor(answer);
        var label = option?.Label ?? answer.Trim();
        var action = option?.Action ?? DecisionAction.Agent;
        // no decision recorded and an answer in my own words: all the fallback can do with it is send the card back
        if (recorded is null && option is null) { action = DecisionAction.Back; notes = (answer.Trim() + Environment.NewLine + notes).Trim(); label = "Send back"; }
        if (action == DecisionAction.Stay)
        {
            _notice = $"{card.Id} stays in {card.Status}: {label}";
            Apply();
            return null;
        }
        // an agent at work on the card holds it: sending back goes to that agent rather than starting another
        if (action == DecisionAction.Back && AgentFor?.Invoke(card.Id) is not null) action = DecisionAction.Agent;
        if (action == DecisionAction.Back && recorded is null && notes.Trim().Length == 0) return "Say what's wrong first: the agent works from your notes.";
        if ((action == DecisionAction.Back && Rework is null) || (action == DecisionAction.Agent && DecisionToAgent is null)) return "This board can't start agents.";
        // a decision shown but not answered in the file would stay open and come back with the card (WOR-0124 QA)
        try { if (recorded is not null && CardDecision.Answer(file, label, notes, DateTime.Now) is null) return $"Couldn't find {card.Id}'s Decision section to write the answer into, so nothing moved."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"Couldn't write the answer into the card: {e.Message}"; }
        switch (action)
        {
            case DecisionAction.Done:
                if (Approve(card.Id) is { } error) return error;
                DecisionSettled?.Invoke(card.Id, DecisionReport.Approved, notes);
                return null;
            case DecisionAction.Back:
                var why = recorded is null ? notes : $"{decision.Question} I picked: {label}." + (notes.Trim().Length > 0 ? " " + notes.Trim() : "");
                if (SendBackCard(card, why) is { } sendError) return sendError;
                DecisionSettled?.Invoke(card.Id, DecisionReport.SentBack, why);
                return null;
            default:
                DecisionToAgent!(card, file, decision, label, notes);
                DecisionSettled?.Invoke(card.Id, DecisionReport.Answered, (label + ". " + notes.Trim()).Trim());
                _notice = $"{card.Id}: you picked {label}";
                var working = _board.Columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? _board.Columns.FirstOrDefault();
                if (working is not null && BoardStore.PlaceOf(card.Status, _board.Columns) != working.Id) Move(card.Id, working.Id);
                else Reload();
                return null;
        }
    }

    /// <summary>Whether a card sits in the board's QA column: Work on then gets the work running to try (WOR-0075),
    /// and it can be sent back with notes (WOR-0079).</summary>
    public bool InQa(Card card) =>
        _board is not null && BoardColumns.Qa(_board.Columns) is { } qa && BoardStore.PlaceOf(card.Status, _board.Columns) == BoardStore.PlaceOf(qa, _board.Columns);

    /// <summary>Whether a card sits in the board's needs-more-work column (<see cref="BoardColumns.Rework"/>): QA failed it.</summary>
    public bool InRework(Card card) =>
        _board is not null && BoardColumns.Rework(_board.Columns) is { } r && BoardStore.PlaceOf(card.Status, _board.Columns) == BoardStore.PlaceOf(r, _board.Columns);

    /// <summary>Whether the card with this id is in its board's QA column now, on whichever row shows it.</summary>
    public bool CardInQa(string cardId) =>
        Root.Rows.Any(r => r._board?.AllCards.FirstOrDefault(c => c.Id == cardId) is { } c && r.InQa(c));

    /// <summary>Whether the card with this id is in its board's attention column now, on whichever row shows it.</summary>
    public bool CardInAttention(string cardId) =>
        Root.Rows.Any(r => r._board?.AllCards.FirstOrDefault(c => c.Id == cardId) is { } c && r.InAttention(c));

    /// <summary>Send a card in QA back (WOR-0079): the notes go into the card under QA notes, an agent starts on
    /// them, and the card goes back to the working column. Returns what went wrong, or null.</summary>
    public string? SendBack(CardViewModel card, string notes) => SendBackCard(card.Card, notes);

    string? SendBackCard(Card card, string notes)
    {
        if (Folder is null || _board is null || Rework is null) return "This board can't start agents.";
        if (notes.Trim().Length == 0) return "Say what's wrong first: the agent works from your notes.";
        if (AgentFor?.Invoke(card.Id) is not null) return $"{card.Id} already has an agent.";
        var file = Path.GetFullPath(Path.Combine(Folder, "items", card.File));
        try { QaNotes.Add(file, notes, DateTime.Now); }
        catch (Exception e) { return $"Couldn't write the notes into the card: {e.Message}"; }
        Rework(card, file, notes);
        _notice = $"Sent {card.Id} back with your notes";
        var working = _board.Columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? _board.Columns.FirstOrDefault();
        if (working is not null && BoardStore.PlaceOf(card.Status, _board.Columns) != working.Id) Move(card.Id, working.Id);
        else Reload();
        return null;
    }

    /// <summary>Make a card for work that starts without one (a drafted README) and start an agent on it, so
    /// the work is on the board. False when the project has no board to put it on.</summary>
    public bool StartNewCard(string title, string description, string repo)
    {
        if (Folder is null || !File.Exists(Path.Combine(Folder, "board.json"))) return false;
        Card card;
        try { card = BoardStore.Create(Folder, title, description, [$"repo:{Path.GetFileName(repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}"]); }
        catch (Exception e) { Status = $"Couldn't make the card: {e.Message}"; return false; }
        Reload();
        StartWork(card, [], show: false, reload: true);
        return true;
    }

    void StartWork(Card card, IReadOnlyList<string> alongside, bool show, bool reload)
    {
        if (Folder is null || _board is null || WorkOn is null) return;
        using var timing = StallWatch.Shared.Section("StartWork");
        var working = _board.Columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? _board.Columns.FirstOrDefault();
        var place = BoardStore.PlaceOf(card.Status, _board.Columns);
        WorkOn(card, Path.GetFullPath(Path.Combine(Folder, "items", card.File)), alongside, show);
        if (working is not null && (place == Board.Backlog || place == _board.Columns[0].Id) && place != working.Id)
            Move(card.Id, working.Id, reload: reload);
        else if (reload) Reload();
    }

    /// <summary>Run QA on a card in QA: an agent checks it from the How to check section. The card stays where it is.</summary>
    public void StartQa(CardViewModel card) => StartQa(card.Card);

    void StartQa(Card card)
    {
        if (Folder is null || RunQa is null) return;
        if (Root.Editor?.CardId == card.Id) Root.Editor = null;
        RunQa(card, Path.GetFullPath(Path.Combine(Folder, "items", card.File)));
    }

    /// <summary>Improve a card (WOR-0073): an agent fills its description out, asking first if the words are
    /// unclear. The card stays where it is and free for Work on once that's done.</summary>
    public void StartImprove(CardViewModel card) => StartImprove(card.Card);

    void StartImprove(Card card)
    {
        if (Folder is null || Improve is null) return;
        if (AgentFor?.Invoke(card.Id) is not null) return; // an agent is on it already (WOR-0155)
        // the card's panel, left open, would save its old text over what the agent writes
        if (Root.Editor?.CardId == card.Id) Root.Editor = null;
        Improve(card, Path.GetFullPath(Path.Combine(Folder, "items", card.File)));
    }

    /// <summary>Run a flow on a card: like Work on, it goes to the working column if it was waiting.</summary>
    public void StartFlow(CardViewModel card, Flow flow)
    {
        if (Folder is null || _board is null || RunFlow is null) return;
        var working = _board.Columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? _board.Columns.FirstOrDefault();
        var place = BoardStore.PlaceOf(card.Card.Status, _board.Columns);
        RunFlow(card.Card, Path.GetFullPath(Path.Combine(Folder, "items", card.Card.File)), flow);
        if (working is not null && (place == Board.Backlog || place == _board.Columns[0].Id) && place != working.Id)
            Move(card.Card.Id, working.Id);
        else Reload();
    }

    internal IReadOnlyList<Flow> FlowList() => Flows?.Invoke() ?? [];

    public void Move(CardViewModel card, string toList, string? beforeId = null) => Move(card.Card.Id, toList, beforeId);

    /// <summary>What a drag of this card carries: every picked card showing, in board order, if it is
    /// picked; otherwise just it.</summary>
    public IReadOnlyList<CardViewModel> Carried(CardViewModel card) =>
        !card.IsPicked ? [card] : (Root.ShowingAll ? Root.Rows.SelectMany(r => r.Shown()) : Shown()).Where(c => c.IsPicked).ToList();

    /// <summary>Cards dropped on one of this row's lists: moved there, those from another row's board brought over to
    /// this board first (they keep their ids). A card with a live agent stays on its board: the agent holds its file's path.</summary>
    public void Drop(IReadOnlyList<CardViewModel> cards, ListViewModel list, string? beforeId)
    {
        if (IsAll) { DropMerged(cards, list, beforeId); return; }
        // a sorted column puts its cards in its own order: a drag within it would change nothing visible, so say so (WOR-0146)
        if (!list.Sort.IsManual && cards.All(c => c.Board == this && BoardStore.PlaceOf(c.Card.Status, _board!.Columns) == list.Id))
        {
            _notice = $"{list.Name} is sorted ({list.SortText}): choose Manual in its menu to drag cards into an order.";
            Apply();
            return;
        }
        if (cards.All(c => c.Board == this)) { Move(cards, list.Id, beforeId); return; }
        if (Folder is null) return;
        var moved = new List<string>();
        var stayed = new List<string>();
        foreach (var c in cards)
        {
            try
            {
                if (c.Board != this)
                {
                    if (c.Board.Folder is not { } from || AgentFor?.Invoke(c.Id) is not null) { stayed.Add(c.Id); continue; }
                    BoardStore.Transfer(from, Folder, c.Id);
                    moved.Add(c.Id);
                }
                BoardStore.Move(Folder, c.Id, list.Id, beforeId);
            }
            catch (Exception e) { stayed.Add($"{c.Id} ({e.Message})"); }
        }
        foreach (var from in cards.Select(c => c.Board).Distinct().Where(b => b != this)) from.Reload();
        _notice = stayed.Count > 0 ? $"Moved {moved.Count} to {SelectedBoard}; left {string.Join(", ", stayed)} where it was (an agent holds it)"
            : moved.Count == 1 ? $"Moved {moved[0]} to {SelectedBoard}" : $"Moved {moved.Count} cards to {SelectedBoard}";
        Reload();
    }

    /// <summary>Move several cards to a list together, in the order given, all landing before
    /// <paramref name="beforeId"/> (or at the end); one reload after.</summary>
    public void Move(IReadOnlyList<CardViewModel> cards, string toList, string? beforeId = null)
    {
        if (cards.Count == 1) { Move(cards[0], toList, beforeId); return; }
        if (Folder is null || cards.Count == 0) return;
        foreach (var c in cards)
        {
            try { BoardStore.Move(Folder, c.Card.Id, toList, beforeId); }
            catch (Exception e) { _notice = $"Couldn't move {c.Card.Id}: {e.Message}"; break; }
        }
        Reload();
        _notice = null;
    }

    void Move(string id, string toList, string? beforeId = null, bool reload = true)
    {
        if (Folder is null) return;
        try { using var timing = StallWatch.Shared.Section("BoardStore.Move"); BoardStore.Move(Folder, id, toList, beforeId); }
        catch (Exception e) { Status = $"Couldn't move {id}: {e.Message}"; return; }
        if (reload) Reload();
    }

    void Watch()
    {
        _watch?.Dispose();
        if (Folder is null || !Directory.Exists(Folder)) return;
        _debounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); Changed(); });
        _watch = new FileSystemWatcher(Folder) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        void Poke(object? s, FileSystemEventArgs e) { StallWatch.Shared.Count("Watch.Board"); Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); }); }
        _watch.Changed += Poke; _watch.Created += Poke; _watch.Deleted += Poke; _watch.Renamed += (s, e) => Poke(s, e);
    }

    /// <summary>Reload every row (an agent ended: its card may be on any of them).</summary>
    public void ReloadAll() { foreach (var r in Root.Rows) r.Reload(); }

    public void Dispose()
    {
        _watch?.Dispose(); _watchBoards?.Dispose();
        foreach (var r in Rows.Where(r => r != this)) r.Dispose();
    }
}

public enum ChipMode { Off, Any, All }

/// <summary>A label (or tag, or colour) to filter by. Click: "any of these" (OR) on or off.
/// Double-click: "must have" (AND), or off again.</summary>
public partial class LabelChip(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAny), nameof(IsAll), nameof(Shown))] public partial ChipMode Mode { get; set; }
    public bool IsAny => Mode == ChipMode.Any;
    public bool IsAll => Mode == ChipMode.All;
    public bool IsOn => Mode != ChipMode.Off;
    /// <summary>The chip's text: a must-have label reads "+name".</summary>
    public string Shown => Mode == ChipMode.All ? "+" + Name : Name;
    public event Action? Changed;
    partial void OnModeChanged(ChipMode value) => Changed?.Invoke();

    // A double-click is: press, release (a Click), press again (seen by the view with a click count of
    // two: DoubleClick), release (a second Click, which must not undo it). So remember the mode before the
    // first click, let DoubleClick set "must have" from there (or clear it if it was already), and
    // swallow the Click that follows.
    // Windows keeps counting while the pointer stays put: a second double-click is clicks 3 and 4. So
    // every odd press (1, 3, 5…) starts a pair and notes the mode; every even one (2, 4…) is a double.
    ChipMode _before;
    bool _swallow;

    /// <summary>An odd-numbered press: the start of a click or of a double-click.</summary>
    public void PressStarts() => _before = Mode;

    [RelayCommand]
    void Click()
    {
        if (_swallow) { _swallow = false; return; }
        Mode = Mode == ChipMode.Off ? ChipMode.Any : ChipMode.Off;
    }

    public void DoubleClick()
    {
        _swallow = true;
        Mode = _before == ChipMode.All ? ChipMode.Off : ChipMode.All;
    }

    public static (HashSet<string> Any, HashSet<string> All) Picked(IEnumerable<LabelChip> chips) => (
        chips.Where(c => c.IsAny).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
        chips.Where(c => c.IsAll).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
}

/// <summary>A list on the board. Every list can be minimized to a narrow strip; a column (not Backlog,
/// Completed or Unsorted, which are Tatty's own lists) can also be renamed and removed.</summary>
public sealed partial class ListViewModel(BoardViewModel board, string id, string name, IEnumerable<CardViewModel> cards, int all,
    bool minimized = false) : ObservableObject
{
    public BoardViewModel Board { get; } = board;
    public string Id { get; } = id;
    /// <summary>The list among every row's (each board has its own c1, c2…): the board's name and the list's id.</summary>
    public string Key => $"{Board.KeyName}/{Id}";
    public string Name { get; } = name;
    public ObservableCollection<CardViewModel> Cards { get; } = new(cards);
    int all = all;

    /// <summary>A reload kept this column: show these cards (changing only what differs) out of <paramref name="count"/> in it.</summary>
    public void Show(IReadOnlyList<CardViewModel> cards, int count)
    {
        all = count;
        ListSync.Sync(Cards, cards);
        Error = "";
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(All));
        OnPropertyChanged(nameof(RemoveQuestion));
        PickedChanged();
    }

    // WOR-0146: the column's sort, chosen in its menu; a view order over board.json's, which Manual brings back
    public ColumnSort Sort => Board.SortOf(Key);
    public string SortText => Sort.IsManual ? "Manual" : ColumnSort.Label(Sort.By) + (Sort.Reversed ? ", reversed" : "");
    public string ManualText => Mark(Sort.IsManual, "Manual");
    public string TitleSortText => Mark(Sort.By == SortBy.Title, "A-Z");
    public string NewestSortText => Mark(Sort.By == SortBy.Newest, "Newest");
    public string LongestSortText => Mark(Sort.By == SortBy.Longest, "Longest in column");
    public string ReverseText => Mark(Sort.Reversed && !Sort.IsManual, "Reverse order");
    public bool IsSorted => !Sort.IsManual;
    static string Mark(bool on, string text) => on ? "✓ " + text : text;
    public void SortChanged()
    {
        foreach (var n in new[] { nameof(Sort), nameof(SortText), nameof(ManualText), nameof(TitleSortText), nameof(NewestSortText),
                     nameof(LongestSortText), nameof(ReverseText), nameof(IsSorted) }) OnPropertyChanged(n);
    }
    [RelayCommand] void SortManual() => Board.SetSort(this, ColumnSort.Manual);
    [RelayCommand] void SortTitle() => Board.SetSort(this, new(SortBy.Title, Sort.By == SortBy.Title && Sort.Reversed));
    [RelayCommand] void SortNewest() => Board.SetSort(this, new(SortBy.Newest, Sort.By == SortBy.Newest && Sort.Reversed));
    [RelayCommand] void SortLongest() => Board.SetSort(this, new(SortBy.Longest, Sort.By == SortBy.Longest && Sort.Reversed));
    [RelayCommand] void ReverseSort() { if (!Sort.IsManual) Board.SetSort(this, Sort with { Reversed = !Sort.Reversed }); }

    public string Count => Cards.Count == all ? $"{all}" : $"{Cards.Count} / {all}";
    public bool HasCards => Cards.Count > 0;
    /// <summary>Every card showing in this column is picked.</summary>
    public bool AllPicked => Cards.Count > 0 && Cards.All(c => c.IsPicked);
    /// <summary>The column's own pick button: "Pick all" here, or "Unpick" once they all are.</summary>
    public string PickText => AllPicked ? "Unpick" : "Pick all";
    [RelayCommand] void Pick() => Board.PickList(this);
    public void PickedChanged() { OnPropertyChanged(nameof(AllPicked)); OnPropertyChanged(nameof(PickText)); }

    // WOR-0125: ▶ Work All at the top of the backlog and the working column, 🧪 QA Run All at the top of the QA column
    BulkStart.Kind BulkKind => Board.BulkKindOf(Id);
    int BulkCount => Board.BulkCount(BulkKind);
    public bool HasBulk => BulkKind != BulkStart.Kind.None;
    public string BulkText => BulkStart.Label(BulkKind, BulkCount, Board.BulkBlockedCount(BulkKind));
    public string BulkTip => BulkKind == BulkStart.Kind.Improve
        ? "Start an improve agent on every thin card (under 3 stars) showing in the backlog with no agent on it: each fills its description out (what's wanted, where, done when). The work itself isn't done. More than " + BulkStart.AskAbove + " asks first."
        : BulkKind == BulkStart.Kind.Qa
        ? "Run QA on every card showing in this column with no QA agent on it, an agent each. More than " + BulkStart.AskAbove + " asks first."
        : "Start an agent on every card showing in In Progress (and a To Do) with none on it, all at once; each is told which others run beside it, " +
          "and cards move to In Progress. A card blocked by one not yet in QA or done is left out (Work on it to start it anyway). " +
          "More than " + BulkStart.AskAbove + " asks first.";
    bool CanBulk() => BulkCount > 0;
    /// <summary>More than a few: the column asks "Start 7 agents?" before any start.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowBulk))] public partial bool ConfirmingBulk { get; set; }
    public bool ShowBulk => HasBulk && !ConfirmingBulk;
    public string BulkQuestion => BulkStart.Question(BulkKind, BulkCount);
    [RelayCommand(CanExecute = nameof(CanBulk))]
    void Bulk()
    {
        if (BulkStart.NeedsAsking(BulkCount)) ConfirmingBulk = true;
        else Board.StartBulk(BulkKind);
    }
    [RelayCommand] void ConfirmBulk() { ConfirmingBulk = false; Board.StartBulk(BulkKind); }
    [RelayCommand] void CancelBulk() => ConfirmingBulk = false;
    public void BulkChanged()
    {
        OnPropertyChanged(nameof(HasBulk)); OnPropertyChanged(nameof(ShowBulk)); OnPropertyChanged(nameof(BulkText)); OnPropertyChanged(nameof(BulkQuestion));
        BulkCommand.NotifyCanExecuteChanged();
    }

    public int All => all;
    /// <summary>A column of its board's own: renamed and removed from its menu. Not on the All board, whose lists are many boards'.</summary>
    public bool IsColumn => !Board.IsAll && Id is not (Core.Board.Backlog or Core.Board.Completed or Core.Board.Unsorted);
    /// <summary>Minimizing is kept in board.json: the All board's lists can't be.</summary>
    public bool CanMinimize => !Board.IsAll;

    // WOR-0214: split by state, a narrow sub-column per state the list's cards are in
    /// <summary>What kind of list it is on its board (<see cref="BoardLayouts.KindOf"/>): the done column and Completed don't split.</summary>
    public string Kind { get; set; } = "";
    public ObservableCollection<StateGroup> Groups { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Unsplit))] public partial bool IsSplit { get; set; }
    public bool Unsplit => !IsSplit;
    /// <summary>The list's width: one column's, or a sub-column's for each state it shows.</summary>
    public double Width => IsSplit ? Math.Max(240, Math.Max(1, Groups.Count) * (StateGroup.Width + 6) + 8) : 290;

    /// <summary>The sub-columns made again from the cards' states now (or none, when the board isn't split).</summary>
    public void Regroup()
    {
        if (!Board.SplitByState || !CardStates.Splits(Kind))
        {
            if (Groups.Count > 0) Groups.Clear();
            IsSplit = false;
        }
        else
        {
            var want = CardStates.Group(Cards, c => c.State).Select(g =>
            {
                var group = Groups.FirstOrDefault(x => x.State == g.State) ?? new StateGroup(this, g.State);
                group.Show(g.Items);
                return group;
            }).ToList();
            ListSync.Sync(Groups, want);
            IsSplit = true;
        }
        if (Width != _width) { _width = Width; OnPropertyChanged(nameof(Width)); }
    }
    double _width = 290;
    /// <summary>The board's last column, its done column: the one that can be wrapped up into stories.</summary>
    public bool IsDoneColumn => IsColumn && Board.Columns.LastOrDefault()?.Id == Id;
    [RelayCommand] void WrapUp() => Board.WrapUp();
    public bool IsMinimized { get; } = minimized;
    public bool IsOpen => !IsMinimized;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsHeaderPlain))] public partial bool IsRenaming { get; set; }
    [ObservableProperty] public partial string EditName { get; set; } = name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsHeaderPlain))] public partial bool ConfirmingRemove { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public bool IsHeaderPlain => !IsRenaming && !ConfirmingRemove;
    public string RemoveQuestion => all == 0 ? $"Remove {Name}?" : $"Remove {Name}? Its {(all == 1 ? "card goes" : $"{all} cards go")} to the backlog.";

    [RelayCommand] void ToggleMinimized() => Board.SetMinimized(this, !IsMinimized);
    [RelayCommand] void StartRename() { if (!IsColumn) return; EditName = Name; Error = ""; IsRenaming = true; Board.Hold(this); }
    [RelayCommand] void CommitRename() => Board.Rename(this, EditName);
    [RelayCommand] void CancelRename() { IsRenaming = false; Error = ""; Board.Release(); }
    [RelayCommand] void AskRemove() { if (!IsColumn) return; Error = ""; ConfirmingRemove = true; Board.Hold(this); }
    [RelayCommand] void Remove() => Board.RemoveColumn(this);
    [RelayCommand] void KeepIt() { ConfirmingRemove = false; Error = ""; Board.Release(); }
}

public sealed partial class CardViewModel : ObservableObject
{
    /// <summary>Picked for a batch (a white frame).</summary>
    [ObservableProperty] public partial bool IsPicked { get; set; }
    public Card Card { get; }
    /// <summary>The row (board) it's on.</summary>
    public BoardViewModel Board { get; }
    public string Id => Card.Id;
    public string Title => Card.Title;
    public string Priority => Card.Priority;
    public bool HasPriority => Card.Priority.Length > 0;
    /// <summary>Its type, shown on the card unless it's a plain Task (most cards are).</summary>
    public string Kind => Card.Kind;
    public bool IsBug => Card.IsBug;
    public bool ShowsKind => !Card.Kind.Equals(CardTypes.Task, StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<string> Labels => Card.Labels;
    public string Description => Card.Description;
    /// <summary>The ticket quality star beside its id (WOR-0156): read from the card file as it is now.</summary>
    public TicketScore Quality => Card.Quality;
    public IReadOnlyList<MoveTarget> MoveTargets { get; }
    /// <summary>The agent working on this card (its state shows on the card, live), if any.</summary>
    public AgentViewModel? Agent { get; }
    public bool HasAgent => Agent is not null;
    /// <summary>The card is in Done (WOR-0116): nothing starts on it; move it out first to redo it.</summary>
    public bool InDone { get; }
    /// <summary>Work on and Improve are offered: no agent on it, and it isn't in Done.</summary>
    public bool CanStart => !HasAgent && !InDone;
    /// <summary>WOR-0162: an Improve button beside a thin card's score (under 3 stars), when Improve could start.</summary>
    public bool ShowImprove => CanStart && Quality.Stars < TicketScore.Max;
    /// <summary>Where it stands for the split view (WOR-0214), from its agent, a QA run, its column and its blockers as they are now.</summary>
    public CardState State => CardStates.Of(Agent?.Working == true, Agent?.NeedsYou == true, Agent?.IsFailed == true,
        Board.QaRunning?.Invoke(Id) == true, Board.InAttention(Card), HasBlockers);
    /// <summary>Its board's name, on its tile on the All board.</summary>
    public string BoardName => Board.SelectedBoard ?? "";
    [ObservableProperty] public partial bool ShowsBoard { get; set; }
    public IRelayCommand WorkOnCommand { get; }
    /// <summary>Work on was pressed on a blocked card (WOR-0173): the tile asks "Blocked by WOR-0172, In Progress. Start
    /// anyway?" with Start anyway and Cancel. Null when it isn't asking.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AskingBlocked))] public partial string? BlockedAsk { get; set; }
    public bool AskingBlocked => BlockedAsk is not null;
    public IRelayCommand StartAnywayCommand { get; }
    public IRelayCommand CancelBlockedCommand { get; }
    public IRelayCommand OpenAgentCommand { get; }
    public IRelayCommand ImproveCommand { get; }
    public IRelayCommand RunQaCommand { get; }

    /// <summary>A QA agent was running on the card when this was built (its context menu's Run QA depends on it).</summary>
    public bool QaWasRunning { get; }
    /// <summary>The card is in QA and no QA agent is running on it: its tile shows Run QA, as the panel and menu do.</summary>
    public bool ShowRunQa { get; }
    /// <summary>WOR-0172: the chips under its title, one per blocker still open (or an unknown id, or a cycle), in the Warn colour.</summary>
    public IReadOnlyList<BlockerState> BlockerChips { get; }
    public bool HasBlockers => BlockerChips.Count > 0;
    /// <summary>What the chips were made from, so a tile is made again when a blocker clears though the card didn't change.</summary>
    public string BlockersKey { get; }
    public static string KeyOf(IReadOnlyList<BlockerState> blockers) =>
        string.Join(",", blockers.Select(b => $"{b.Id}:{b.Found}:{b.Cleared}:{b.Cycle}:{b.Card?.Card.Title}:{b.Card?.Column}"));

    public CardViewModel(Card card, IEnumerable<(string Id, string Name)> lists, BoardViewModel board, AgentViewModel? agent = null,
        IEnumerable<string>? otherBoards = null, IReadOnlyList<BlockerState>? blockers = null)
    {
        BlockerChips = blockers ?? [];
        BlockersKey = KeyOf(BlockerChips);
        Card = card;
        Board = board;
        Agent = agent;
        QaWasRunning = board.QaRunning?.Invoke(card.Id) == true;
        WorkOnCommand = new RelayCommand(() => board.StartWork(this));
        StartAnywayCommand = new RelayCommand(() => board.StartWork(this, force: true));
        CancelBlockedCommand = new RelayCommand(() => board.CancelBlocked(this));
        OpenAgentCommand = new RelayCommand(() => { if (Agent is not null) board.OpenAgent?.Invoke(Agent); });
        ImproveCommand = new RelayCommand(() => board.StartImprove(this));
        RunQaCommand = new RelayCommand(() => board.StartQa(this));
        InDone = board.IsDone(card.Id);
        var inQa = board.InQa(card);
        ShowRunQa = inQa && !QaWasRunning && !InDone;
        MoveTargets = [
            .. InDone && agent is null ? Array.Empty<MoveTarget>() : [new MoveTarget(agent is not null ? "Open its agent" : inQa ? "▶ Get it running for me to try" : "Work on this with an agent", agent is null ? WorkOnCommand : OpenAgentCommand)],
            .. inQa && !InDone && !QaWasRunning ? [new MoveTarget("🧪 Run QA (an agent checks it)", RunQaCommand)] : Array.Empty<MoveTarget>(),
            // sending back takes notes, typed in the card's panel
            .. agent is null && (inQa || board.InAttention(card)) ? [
                new MoveTarget("✓ Approve: move to done", new RelayCommand(() => { if (board.Approve(card.Id) is null) board.Root.Approved?.Invoke(card.Id); })),
                new MoveTarget("↩ Send back with notes…", new RelayCommand(() => board.Open(this)))] : Array.Empty<MoveTarget>(),
            .. agent is null && !InDone ? [new MoveTarget("✦ Improve this card (an agent fills it out)", ImproveCommand)] : Array.Empty<MoveTarget>(),
            .. (agent is null && !InDone ? board.FlowList() : []).Select(f => new MoveTarget("Run flow: " + f.Name, new RelayCommand(() => board.StartFlow(this, f)))),
            .. board.QueueTargets(this), // WOR-0212: add to or take out of a queue
            .. lists.Select(l => new MoveTarget("Move to " + l.Name, new RelayCommand(() => board.Move(this, l.Id)))),
            // a card with a live agent stays put: the agent holds its file's path
            .. agent is null ? (otherBoards ?? []).Select(b => new MoveTarget("Move to board " + b, new RelayCommand(() => board.MoveToBoard(this, b)))) : [],
        ];
    }
}

public sealed record MoveTarget(string Label, IRelayCommand Command);

/// <summary>One option of a card's decision (WOR-0124), as a button: its label, what it means, what picking it does.</summary>
public sealed record DecisionChoice(DecisionOption Option, IRelayCommand Command)
{
    public string Label => Option.Label;
    public string Description => Option.Description;
    public bool HasDescription => Option.Description.Length > 0;
    public bool Recommended => Option.Recommended;
    public string ActionText => Option.ActionText;
}

/// <summary>A picture in a card's decision box (WOR-0147): the copy drawn there and the file it came from, which a
/// click shows at full size (WOR-0167).</summary>
public sealed record DecisionPicture(Avalonia.Media.Imaging.Bitmap Picture, string File);

/// <summary>A board's tab above the board; a plain click picks it alone, Ctrl+click adds it or takes it off.</summary>
public sealed partial class BoardTab(BoardViewModel board, string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] public partial bool IsShown { get; set; }
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    void Pick() { var ctrl = board.TabCtrl; board.TabCtrl = false; board.PickBoard(Name, ctrl); }
}

/// <summary>The side panel for making boards: a new one by hand, or an agent sorting the cards into several.</summary>
public partial class BoardMaker(BoardViewModel board, IEnumerable<string> columns) : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "";
    /// <summary>Its columns, comma separated (the shown board's to start with).</summary>
    [ObservableProperty] public partial string Columns { get; set; } = string.Join(", ", columns);
    /// <summary>Optional words for the sorting agent ("by area of the app", "art apart from code").</summary>
    [ObservableProperty] public partial string Hint { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";

    [RelayCommand]
    void Create()
    {
        try { board.CreateBoard(Name, Columns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)); }
        catch (Exception e) { Error = e.Message; return; }
        board.Root.Maker = null;
    }

    [RelayCommand] void Sort() => board.SortIntoBoards(Hint.Trim());
    [RelayCommand] void Cancel() => board.Root.Maker = null;
}

/// <summary>The side panel for a new card, or for reading and editing one.</summary>
public partial class CardEditor : ObservableObject
{
    public static readonly IReadOnlyList<string> Priorities = ["", "Highest", "High", "Medium", "Low", "Lowest"];
    readonly BoardViewModel _board;
    readonly CardViewModel? _card;
    public bool IsNew => _card is null;
    public string Heading => _card is null ? "New card" : _card.Id;
    public string Status => _card?.Card.Status is { Length: > 0 } s ? s : "Backlog";
    public IReadOnlyList<string> PriorityChoices => Priorities;
    /// <summary>Task and Bug, plus the card's own type if its file names another.</summary>
    public IReadOnlyList<string> TypeChoices { get; }
    [ObservableProperty] public partial string Type { get; set; }
    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Pictures), nameof(Quality), nameof(ShowImprove))] public partial string Description { get; set; }
    /// <summary>The ticket quality star by the heading (WOR-0156), from the description as typed and the card's
    /// later sections (## Where, ## Done when), so it moves as the box is filled in.</summary>
    public TicketScore Quality => TicketQuality.Score(TicketQuality.TicketText(Description ?? "", _card?.Card.Sections ?? ""));
    /// <summary>The pictures the description shows (markdown images, files under the board's folder), for a preview under the box.</summary>
    public IReadOnlyList<Avalonia.Media.Imaging.Bitmap> Pictures => _board.Folder is null ? [] :
        Darg.Core.Pictures.InDescription(_board.Folder, Description).Select(p =>
        {
            try { using var f = File.OpenRead(p); return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(f, 720); }
            catch (Exception) { return null; }
        }).OfType<Avalonia.Media.Imaging.Bitmap>().ToList();
    public bool HasPictures => Pictures.Count > 0;
    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(HasPictures));

    /// <summary>A picture was chosen: copy it into the board's images folder and add it to the end of the description.</summary>
    public void AddPicture(string path)
    {
        if (_board.Folder is null) return;
        try
        {
            var rel = Darg.Core.Pictures.Add(_board.Folder, path);
            Description = (Description.TrimEnd() + $"\n\n![]({rel})").TrimStart();
            Error = "";
        }
        catch (Exception e) { Error = e.Message; }
    }
    [ObservableProperty] public partial string LabelsText { get; set; }
    [ObservableProperty] public partial string Priority { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public string? CardId => _card?.Id;
    /// <summary>The card's file on disk (WOR-0142), so an agent told which card is open needn't hunt for it.</summary>
    public string? FilePath => _card is null || _board.Folder is null ? null : Path.Combine(_board.Folder, "items", _card.Card.File).Replace('\\', '/');
    /// <summary>The card's live agent, looked up now: the panel can outlast the agent (forgotten while it's open).</summary>
    bool HasAgent => _card is not null && (_board.AgentFor?.Invoke(_card.Id) is not null);
    public bool CanWorkOn => _card is not null && !HasAgent && !_card.InDone;
    /// <summary>WOR-0162: Improve beside the panel's score while it is under 3 stars (follows the description as typed).</summary>
    public bool ShowImprove => CanWorkOn && Quality.Stars < TicketScore.Max;
    /// <summary>The card is in the board's QA column: Work on gets it running to try, and it can be sent back.</summary>
    public bool InQa => _card is not null && _board.InQa(_card.Card);
    public bool InAttention => _card is not null && _board.InAttention(_card.Card);
    public bool CanRunQa => InQa && _card is not null && !_card.InDone && _board.QaRunning?.Invoke(_card.Id) != true;
    [RelayCommand]
    void RunQa()
    {
        if (_card is null) return;
        _board.Root.Editor = null;
        _board.StartQa(_card);
    }
    public bool CanSendBack => (InQa || InAttention) && CanWorkOn && !ShowDecision;
    /// <summary>A report for this card exists (its decision report, once it has landed in needs attention).</summary>
    public bool HasReport => _card is not null && _board.ReportFor?.Invoke(_card.Id) is not null;
    [RelayCommand]
    void OpenReport() { if (_card is not null && _board.ReportFor?.Invoke(_card.Id) is { } f) { _board.Root.Editor = null; _board.OpenReport?.Invoke(f); } }
    public string WorkOnText => InQa ? "▶ Get it running to try" : "▶ Work on this";
    /// <summary>What's wrong, typed while trying a card in QA: it goes into the card and to the agent that reworks it.</summary>
    [ObservableProperty] public partial string SendBackNotes { get; set; } = "";
    /// <summary>Optional: why the card needs the person's attention (WOR-0111); goes into the card and its decision report.</summary>
    [ObservableProperty] public partial string AttentionWhy { get; set; } = "";
    /// <summary>A button per flow, to run it on this card.</summary>
    public IReadOnlyList<MoveTarget> FlowTargets { get; private set; } = [];

    /// <summary>The decision the card waits on (WOR-0124), read when the panel opened: shown at the top in place of
    /// the description while the card is in For Human Attention.</summary>
    public CardDecision? Decision { get; }
    public bool ShowDecision => Decision is not null && InAttention;
    /// <summary>In For Human Attention with no decision recorded: the fallback question, and word that Darg is asking for one.</summary>
    public bool MissingDecision => _card is not null && Decision is null && InAttention;
    public string DecisionQuestion => Decision?.Question ?? "";
    public string DecisionContext => Decision?.Context ?? "";
    public bool HasDecisionContext => DecisionContext.Length > 0;
    /// <summary>WOR-0147: the pictures anywhere in the card file (decision, How to check, description), shown in the
    /// decision box so a question about how something looks has the thing beside it.</summary>
    public IReadOnlyList<DecisionPicture> DecisionPictures { get; } = [];
    public bool HasDecisionPictures => DecisionPictures.Count > 0;
    /// <summary>WOR-0167: the decision picture clicked, read at full size and drawn over the board as big as it will
    /// go; a click on it or Escape closes it, the arrow keys step through the others.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEnlarged), nameof(EnlargedCaption))]
    public partial Avalonia.Media.Imaging.Bitmap? Enlarged { get; private set; }
    public bool IsEnlarged => Enlarged is not null;
    int _enlargedAt = -1;
    public string EnlargedCaption => !IsEnlarged ? "" : (DecisionPictures.Count > 1 ? $"{_enlargedAt + 1} of {DecisionPictures.Count} · ← → for the others · " : "")
        + "click or Esc to close";
    [RelayCommand] void Enlarge(DecisionPicture? picture)
    {
        var at = picture is null ? -1 : DecisionPictures.ToList().IndexOf(picture);
        if (at >= 0) ShowEnlarged(at);
    }
    [RelayCommand] void CloseEnlarged() { Enlarged = null; _enlargedAt = -1; }
    /// <summary>The next (+1) or previous (-1) picture while one is enlarged, wrapping round.</summary>
    public void StepEnlarged(int by)
    {
        if (!IsEnlarged || DecisionPictures.Count < 2) return;
        ShowEnlarged(((_enlargedAt + by) % DecisionPictures.Count + DecisionPictures.Count) % DecisionPictures.Count);
    }
    void ShowEnlarged(int at)
    {
        var picture = DecisionPictures[at];
        Avalonia.Media.Imaging.Bitmap full;
        // the file at its own size (the box's copy is cut to 720 wide); gone or unreadable since, the box's copy
        try { using var f = File.OpenRead(picture.File); full = new Avalonia.Media.Imaging.Bitmap(f); }
        catch (Exception) { full = picture.Picture; }
        _enlargedAt = at;
        Enlarged = full;
    }
    public string DecisionAskedBy => Decision is null ? "" : Decision.Drafted ? "Drafted by Darg from the card: check it fits."
        : Decision.AskedBy.Length > 0 ? "Asked by " + Decision.AskedBy : "";
    public IReadOnlyList<DecisionChoice> DecisionChoices { get; } = [];
    /// <summary>Notes to go with a pick, or an answer in my own words (it goes to the agent).</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AnswerOwnCommand))] public partial string DecisionNotes { get; set; } = "";
    /// <summary>Why the card is there, for the no-decision box: its newest reason, else the start of How to check.</summary>
    public string WhyHere { get; } = "";
    public bool HasWhyHere => WhyHere.Length > 0;
    /// <summary>The description box is folded under the decision, and opens on a click.</summary>
    [ObservableProperty] public partial bool DescriptionOpen { get; set; } = true;
    [RelayCommand] void OpenDescription() => DescriptionOpen = true;

    void Answer(string answer)
    {
        if (_card is null) return;
        if (_board.Decide(_card.Id, answer, DecisionNotes) is { } error) { Error = error; return; }
        _board.Root.Editor = null;
    }
    bool CanAnswerOwn => DecisionNotes.Trim().Length > 0;
    [RelayCommand(CanExecute = nameof(CanAnswerOwn))]
    void AnswerOwn()
    {
        var own = DecisionNotes.Trim();
        DecisionNotes = "";
        Answer(own);
    }

    public CardEditor(BoardViewModel board, CardViewModel? card)
    {
        _board = board; _card = card;
        if (card is not null && board.InAttention(card.Card) && board.Folder is { } folder)
        {
            string text;
            try { text = File.ReadAllText(Path.GetFullPath(Path.Combine(folder, "items", card.Card.File))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { text = ""; }
            Decision = CardDecision.Read(text) is { Valid: true } d ? d : null;
            WhyHere = CardDecision.WhyHere(text);
            if (Decision is not null)
                DecisionPictures = Darg.Core.Pictures.InDescription(Path.Combine(folder, "items"), text).Select(p =>
                {
                    try { using var f = File.OpenRead(p); return new DecisionPicture(Avalonia.Media.Imaging.Bitmap.DecodeToWidth(f, 720), p); }
                    catch (Exception) { return null; }
                }).OfType<DecisionPicture>().ToList();
            // recommended first, as AskUserQuestion puts it
            DecisionChoices = (Decision?.Options ?? []).OrderByDescending(o => o.Recommended)
                .Select(o => new DecisionChoice(o, new RelayCommand(() => Answer(o.Label)))).ToList();
            DescriptionOpen = Decision is null;
        }
        Title = card?.Title ?? "";
        Description = card?.Description ?? "";
        LabelsText = card is null ? "" : string.Join(", ", card.Labels);
        Priority = card?.Priority ?? "";
        Type = CardTypes.All.FirstOrDefault(t => t.Equals(card?.Card.Kind, StringComparison.OrdinalIgnoreCase)) ?? card?.Card.Kind ?? CardTypes.Task;
        TypeChoices = CardTypes.All.Contains(Type) ? CardTypes.All : [.. CardTypes.All, Type];
        FlowTargets = MakeFlowTargets();
        InitBlockers();
    }

    List<MoveTarget> MakeFlowTargets() => _card is not { } card || HasAgent || card.InDone ? [] : _board.FlowList().Select(f => new MoveTarget("▶ " + f.Name, new RelayCommand(() =>
    {
        _board.Root.Editor = null;
        _board.StartFlow(card, f);
    }))).ToList();

    /// <summary>The card's agent came or went: Work on and the flow buttons follow (what's typed in the panel stays).</summary>
    public void AgentChanged()
    {
        FlowTargets = MakeFlowTargets();
        OnPropertyChanged(nameof(FlowTargets));
        OnPropertyChanged(nameof(CanWorkOn));
        OnPropertyChanged(nameof(ShowImprove));
        OnPropertyChanged(nameof(CanSendBack)); OnPropertyChanged(nameof(CanRunQa)); OnPropertyChanged(nameof(CanQaVerdict)); OnPropertyChanged(nameof(CanToAttention)); OnPropertyChanged(nameof(HasReport)); OnPropertyChanged(nameof(InAttention));
    }

    IEnumerable<string> LabelList() => LabelsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    [RelayCommand]
    void Save()
    {
        if (_board.Folder is null) return;
        if (Title.Trim().Length == 0) { Error = "A card needs a title."; return; }
        if (BlockersError(_card?.Id) is { } blockError) { Error = blockError; return; }
        try
        {
            if (_card is null)
            {
                var made = BoardStore.Create(_board.Folder, Title, Description, LabelList(), Priority, Type);
                SaveBlockers(_board.Folder, made.Id, []);
                // a card made on the inbox goes on to the story board Haiku says it fits (WOR-0087)
                _board.PlaceLater(_board.Folder, made.Id);
            }
            else
            {
                BoardStore.Update(_board.Folder, _card.Id, Title, Description, LabelList(), Priority,
                    // a blank Type stays blank unless it was changed (blank already reads as Task)
                    _card.Card.Type.Length == 0 && Type == CardTypes.Task ? null : Type);
                SaveBlockers(_board.Folder, _card.Id, _card.Card.BlockedBy);
            }
        }
        catch (Exception e) { Error = e.Message; return; }
        _board.Root.Editor = null;
        _board.Reload();
    }

    [RelayCommand] void Cancel() => _board.Root.Editor = null;

    public bool CanDone => _card is not null;
    [ObservableProperty] public partial bool ConfirmingDelete { get; set; }
    public string DeleteLabel => ConfirmingDelete ? "Click again to delete" : "Delete";
    partial void OnConfirmingDeleteChanged(bool value) => OnPropertyChanged(nameof(DeleteLabel));

    /// <summary>Finish the card: to the board's last column (its done column), or Completed if it has none.</summary>
    [RelayCommand]
    void Done()
    {
        if (_card is null) return;
        _board.Root.Editor = null;
        _board.MarkDone(_card);
    }

    /// <summary>Delete, after a second click (the file goes to the board's .deleted folder, so it can come back).</summary>
    [RelayCommand]
    void Delete()
    {
        if (_card is null || _board.Folder is null) return;
        if (!ConfirmingDelete) { ConfirmingDelete = true; return; }
        try { BoardStore.Delete(_board.Folder, _card.Id); }
        catch (Exception e) { Error = e.Message; return; }
        _board.Root.Editor = null;
        _board.Reload();
        _board.Status = $"Deleted {_card.Id} (it's in the board's .deleted folder)";
    }

    [RelayCommand]
    void WorkOn()
    {
        if (_card is null) return;
        // a blocked card asks here first (WOR-0173); in QA, Work on only gets it running to try, so it doesn't
        if (!InQa && _board.OpenBlockers(_card.Card, fresh: true) is { Count: > 0 } open) { BlockedAsk = BlockedStart.Question(open); return; }
        _board.Root.Editor = null;
        _board.StartWork(_card, force: true);
    }

    /// <summary>Work on was pressed on a blocked card (WOR-0173): the panel asks "Blocked by WOR-0172, In Progress. Start
    /// anyway?" under its buttons. Null when it isn't asking.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AskingBlocked))] public partial string? BlockedAsk { get; set; }
    public bool AskingBlocked => BlockedAsk is not null;
    [RelayCommand]
    void StartAnyway()
    {
        if (_card is null) return;
        BlockedAsk = null;
        _board.Root.Editor = null;
        _board.StartWork(_card, force: true);
    }
    [RelayCommand] void CancelBlocked() => BlockedAsk = null;

    /// <summary>The QA verdict buttons (WOR-0095): a card in QA with no agent running can be sent to For Human Attention
    /// (its decision report is written by the board's reload), approved to done, or sent back with notes below.</summary>
    // a card with a decision (WOR-0124) is answered by its options instead
    public bool CanQaVerdict => (InQa || InAttention) && CanWorkOn && !ShowDecision;
    public bool CanToAttention => InQa && HasAttentionColumn;
    public bool HasAttentionColumn => _board.HasAttentionColumn;
    [RelayCommand]
    void QaToAttention()
    {
        if (_card is null) return;
        if (_board.MoveToAttention(_card.Id, AttentionWhy) is { } error) { Error = error; return; }
        _board.Root.Editor = null;
    }
    [RelayCommand]
    void QaDone()
    {
        if (_card is null) return;
        var id = _card.Id;
        if (_board.Approve(id) is { } error) { Error = error; return; }
        _board.Root.Editor = null;
        _board.Root.Approved?.Invoke(id);
    }

    /// <summary>Send a card in QA back (WOR-0079): the notes go into the card and an agent reworks it from them.
    /// The panel closes first, so a Save here can't write over what the agent writes.</summary>
    [RelayCommand]
    void SendBack()
    {
        if (_card is null) return;
        if (_board.SendBack(_card, SendBackNotes) is { } error) { Error = error; return; }
        _board.Root.Editor = null;
    }

    /// <summary>An agent rounds the card out. The panel closes first, so a Save here can't write over what it writes.</summary>
    [RelayCommand]
    void Improve()
    {
        if (_card is null) return;
        _board.Root.Editor = null;
        _board.StartImprove(_card);
    }
}

public static class Bools
{
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, string> CreateOrSave = new(isNew => isNew ? "Create" : "Save");
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, string> ChangeOrAddPicture = new(has => has ? "Change picture…" : "Add picture…");
}
