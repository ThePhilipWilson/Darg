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
    /// <summary>The other boards, each with a tick for showing it as a row too.</summary>
    public ObservableCollection<BoardChoice> Choices { get; } = [];
    /// <summary>A row's header (its board's name) shows once there is more than one row.</summary>
    [ObservableProperty] public partial bool ShowsHeader { get; set; }
    public bool IsExtraRow => _parent is not null;
    /// <summary>Set by the view: a row's height, so several share the screen.</summary>
    [ObservableProperty] public partial double RowHeight { get; set; } = double.NaN;
    public string ShowBoardsText => Rows.Count > 1 ? $"⧉ {Rows.Count} boards" : "⧉ Boards";

    /// <summary>Set by the project: the live agent working on a card, if any.</summary>
    public Func<string, AgentViewModel?>? AgentFor { get => _parent is null ? field : _parent.AgentFor; set; }
    /// <summary>Set by the project: start an agent on a card. Passed: the card's file path, the ids of cards
    /// other agents are being started on at the same time, and whether to show the agent.</summary>
    public Action<Card, string, IReadOnlyList<string>, bool>? WorkOn { get => _parent is null ? field : _parent.WorkOn; set; }
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
    /// <summary>Set by the project: the newest report written for a card (its file), or null.</summary>
    public Func<string, string?>? ReportFor { get => _parent is null ? field : _parent.ReportFor; set; }
    /// <summary>Set by the project: show a report in Reports.</summary>
    public Action<string>? OpenReport { get => _parent is null ? field : _parent.OpenReport; set; }
    /// <summary>The ids in the needs-attention column at the last read of this board's folder; null before the first.</summary>
    HashSet<string>? _attention;
    string? _attentionFolder;

    public ObservableCollection<string> Boards { get; } = [];
    public ObservableCollection<ListViewModel> Lists { get; } = [];
    public ObservableCollection<LabelChip> Labels { get; } = [];

    [ObservableProperty] public partial string? SelectedBoard { get; set; }
    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial bool ShowCompleted { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditing))] public partial CardEditor? Editor { get; set; }
    public bool IsEditing => Editor is not null;
    [RelayCommand] void NewCard() { if (Folder is not null) Root.Editor = new CardEditor(this, null); }

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
        StartAgent(Stories.Prompt(_project.Name, folder, done, cards, inColumn.Where(c => c.IsStory).ToList(), busy, stories, reports,
                _project.ExistingRepos(), Core.Commits.For(_project, folder), stories is null ? null : Core.Commits.For(_project, stories)),
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
        var now = BoardStore.List(_project.Boards);
        var keep = SelectedBoard;
        if (!now.SequenceEqual(Boards))
        {
            foreach (var b in Boards.Except(now).Where(b => b != keep).ToList()) Boards.Remove(b);
            foreach (var b in now.Except(Boards).ToList())
            {
                var at = 0;
                while (at < Boards.Count && StringComparer.OrdinalIgnoreCase.Compare(Boards[at], b) < 0) at++;
                Boards.Insert(at, b);
            }
        }
        if (SelectedBoard is null && Boards.Count > 0) SelectedBoard = Boards[0];
        OnPropertyChanged(nameof(OtherBoards));
        SyncRows();
    }

    /// <summary>The project's other boards, for "Move to board".</summary>
    public IReadOnlyList<string> OtherBoards => Root.Boards.Where(b => b != SelectedBoard).ToList();

    /// <summary>The rows and the ticks brought up to date with the boards on disk, the picked one and the others shown.</summary>
    void SyncRows()
    {
        if (_parent is not null) return;
        var others = Boards.Where(b => b != SelectedBoard).ToList();
        if (!others.SequenceEqual(Choices.Select(c => c.Name)) || Choices.Any(c => c.IsShown != _also.Contains(c.Name)))
        {
            Choices.Clear();
            foreach (var b in others) Choices.Add(new BoardChoice(this, b, _also.Contains(b)));
        }
        var want = others.Where(_also.Contains).ToList();
        foreach (var row in Rows.Skip(1).Where(r => !want.Contains(r.SelectedBoard!)).ToList()) { Rows.Remove(row); row.Dispose(); }
        for (var i = 0; i < want.Count; i++)
        {
            var at = Rows.Skip(1).ToList().FindIndex(r => r.SelectedBoard == want[i]);
            if (at == i) continue;
            if (at >= 0) Rows.Move(at + 1, i + 1);
            else Rows.Insert(i + 1, new BoardViewModel(this, want[i]));
        }
        foreach (var r in Rows) r.ShowsHeader = Rows.Count > 1;
        OnPropertyChanged(nameof(ShowBoardsText));
        RowsChanged?.Invoke();
        SyncLabels();
        ApplyAll();
    }

    /// <summary>A list on any row, by its <see cref="ListViewModel.Key"/>.</summary>
    public ListViewModel? ListByKey(string key) => Root.Rows.SelectMany(r => r.Lists).FirstOrDefault(l => l.Key == key);

    /// <summary>Raised when a row comes or goes, so the view shares the height out again.</summary>
    public event Action? RowsChanged;

    /// <summary>Show a board as a row of its own beside the picked one, or stop showing it.</summary>
    public void SetShown(string board, bool shown)
    {
        if (shown ? !_also.Add(board) : !_also.Remove(board)) return;
        ShownBoards.Save(_project.Id, _also.Order(StringComparer.OrdinalIgnoreCase));
        SyncRows();
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
    public string? InboxFolder => _project.Boards is null || StoryBoards.Inbox(Root.Boards) is not { } inbox ? Folder : Path.Combine(_project.Boards, inbox);

    /// <summary>A card was just made on <paramref name="boardFolder"/>: if that's the inbox and the project has other
    /// boards, Haiku moves it to the story it fits once <paramref name="after"/> (its retitling) is done.</summary>
    public void PlaceLater(string boardFolder, string cardId, Task? after = null)
    {
        if (_project.Boards is not { } boards || Root.Boards.Count < 2 || StoryBoards.Inbox(Root.Boards) is not { } inbox) return;
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
        foreach (var b in BoardStore.List(project.Boards)) Boards.Add(b);
        SelectedBoard = Boards.FirstOrDefault();
        if (Boards.Count == 0) Status = project.Boards is null ? "This project has no boards folder set." : $"No boards in {project.Boards}. + Board makes one.";
        WatchBoards();
        SyncRows();
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
        void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { t.Stop(); t.Start(); });
        _watchBoards.Created += Poke; _watchBoards.Deleted += Poke; _watchBoards.Renamed += (s, e) => Poke(s, e);
    }

    partial void OnSelectedBoardChanged(string? value)
    {
        _notice = null;
        OnPropertyChanged(nameof(OtherBoards));
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
    int ToStart => Root.Rows.Sum(r => r.Shown().Count(c => c.IsPicked && !c.HasAgent));
    /// <summary>"▶ Work on 3", counting only picked cards with no agent on them yet.</summary>
    public string WorkOnPickedText => $"▶ Work on {ToStart}";
    public bool CanWorkOnPicked => ToStart > 0;

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
        root.OnPropertyChanged(nameof(HasPicked));
        root.OnPropertyChanged(nameof(PickedText));
        root.OnPropertyChanged(nameof(WorkOnPickedText));
        root.OnPropertyChanged(nameof(CanWorkOnPicked));
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
            if (pick) _picked.Add(c.Id); else _picked.Remove(c.Id);
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
        var picked = Root.Rows.Select(r => (Row: r, Cards: r.Shown().Where(c => c.IsPicked && !c.HasAgent).Select(c => c.Card).ToList()))
            .Where(p => p.Cards.Count > 0).ToList();
        if (picked.Count == 0) return;
        var all = picked.SelectMany(p => p.Cards).Select(c => c.Id).ToList();
        foreach (var (row, cards) in picked)
        {
            row._picked.Clear();
            row.WorkOnAll(cards, all);
        }
        if (picked.Count > 1) { Root._notice = $"Started {all.Count} agents on {picked.Count} boards"; Root.Apply(); }
    }

    /// <summary>Start agents on cards of the shown board by id, together, as ▶ Work on N does (the API's work-on
    /// with several cards). Cards that already have an agent are left out. Says what was started.</summary>
    public string WorkOnCards(IReadOnlyList<string> ids)
    {
        if (_board is null) return "No board is showing.";
        var cards = ids.Select(id => _board.AllCards.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .OfType<Card>().Where(c => AgentFor?.Invoke(c.Id) is null).DistinctBy(c => c.Id).ToList();
        if (cards.Count == 0) return "None of those cards is on the board without an agent.";
        WorkOnAll(cards);
        return _notice ?? "";
    }

    void WorkOnAll(List<Card> cards, IReadOnlyList<string>? alongside = null)
    {
        using var timing = StallWatch.Shared.Section("Work on picked");
        var started = new List<string>();
        var together = alongside ?? cards.Select(c => c.Id).ToList();
        foreach (var card in cards)
        {
            try { StartWork(card, together.Where(id => id != card.Id).ToList(), show: false, reload: false); }
            catch (Exception e) { _notice = $"Started {Ids(started)}; stopped at {card.Id}: {e.Message}"; Reload(); return; }
            started.Add(card.Id);
        }
        _notice = started.Count == 1 ? $"Started an agent on {started[0]}" : $"Started {started.Count} agents: {Ids(started)}";
        Reload();
        static string Ids(List<string> ids) => ids.Count == 0 ? "none" : string.Join(", ", ids);
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
        try { using var loading = StallWatch.Shared.Section("BoardStore.Load"); _board = BoardStore.Load(Folder); }
        catch (Exception e) { Status = $"Couldn't read the board: {e.Message}"; return; }
        NoticeAttention();
        if (Root.SyncLabels()) ApplyAll(); else Apply();
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

    /// <summary>The shown board's columns (none before it loads).</summary>
    public IReadOnlyList<Column> Columns => _board?.Columns ?? [];

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
            var vm = oldCards.TryGetValue(c.Id, out var old) && old.Agent == agent && old.Card.SameAs(c) && used.Add(old)
                ? old : new CardViewModel(c, targets, this, agent, OtherBoards);
            vm.IsPicked = _picked.Contains(c.Id);
            return vm;
        }
        var lists = new List<ListViewModel>();
        var total = 0;
        foreach (var (id, name) in _board.ListOrder())
        {
            if (id == Board.Completed && !root.ShowCompleted) continue;
            var cards = _board.Lists[id].Where(Keep).Select(ViewOf).ToList();
            total += cards.Count;
            var minimized = _board.Minimized.Contains(id);
            if (oldLists.TryGetValue(id, out var list) && list.Name == name && list.IsMinimized == minimized) list.Show(cards, _board.Lists[id].Count);
            else list = new ListViewModel(this, id, name, cards, _board.Lists[id].Count, minimized);
            lists.Add(list);
        }
        ListSync.Sync(Lists, lists);
        _picked.IntersectWith(Shown().Select(c => c.Id));
        PickedChanged();
        Status = _notice ?? $"{total} of {_board.AllCards.Count()} cards";
    }

    /// <summary>Start an agent on a card: it goes to the board's working column ("In Progress", else
    /// the first column) if it was waiting in the backlog or the first column, and the agent opens.</summary>
    public void StartWork(CardViewModel card) => StartWork(card.Card, [], show: false, reload: true);

    /// <summary>A card that wasn't in the needs-attention column at the last read but is now has just landed there,
    /// whoever moved it (an agent editing board.json, a QA check, a drag): its decision report is written (WOR-0092).
    /// The first read of a board only learns who is there already.</summary>
    void NoticeAttention()
    {
        if (_board is null || Folder is null) return;
        var column = AttentionColumn.Find(_board.Columns);
        var now = column is null ? [] : _board.AllCards.Where(c => BoardStore.PlaceOf(c.Status, _board.Columns) == BoardStore.PlaceOf(column, _board.Columns)).ToList();
        var fresh = _attention is not null && _attentionFolder == Folder ? now.Where(c => !_attention.Contains(c.Id)).ToList() : [];
        _attention = now.Select(c => c.Id).ToHashSet();
        _attentionFolder = Folder;
        foreach (var card in fresh) NeedsAttention?.Invoke(card, Path.GetFullPath(Path.Combine(Folder, "items", card.File)));
    }

    /// <summary>Whether a card sits in the board's needs-attention column.</summary>
    public bool InAttention(Card card) =>
        _board is not null && AttentionColumn.Find(_board.Columns) is { } a && BoardStore.PlaceOf(card.Status, _board.Columns) == BoardStore.PlaceOf(a, _board.Columns);

    public bool HasAttentionColumn => _board is not null && AttentionColumn.Find(_board.Columns) is not null;

    /// <summary>Move a card to the board's needs-attention column. Returns what went wrong, or null.</summary>
    public string? MoveToAttention(string cardId)
    {
        if (_board is null || AttentionColumn.Find(_board.Columns) is not { } name || _board.Columns.FirstOrDefault(c => c.Name == name) is not { } col) return "This board has no Needs Attention column.";
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

    /// <summary>Whether a card sits in the board's QA column: Work on then gets the work running to try (WOR-0075),
    /// and it can be sent back with notes (WOR-0079).</summary>
    public bool InQa(Card card) =>
        _board is not null && BoardColumns.Qa(_board.Columns) is { } qa && BoardStore.PlaceOf(card.Status, _board.Columns) == BoardStore.PlaceOf(qa, _board.Columns);

    /// <summary>Send a card in QA back (WOR-0079): the notes go into the card under QA notes, an agent starts on
    /// them, and the card goes back to the working column. Returns what went wrong, or null.</summary>
    public string? SendBack(CardViewModel card, string notes)
    {
        if (Folder is null || _board is null || Rework is null) return "This board can't start agents.";
        if (notes.Trim().Length == 0) return "Say what's wrong first: the agent works from your notes.";
        if (AgentFor?.Invoke(card.Id) is not null) return $"{card.Id} already has an agent.";
        var file = Path.GetFullPath(Path.Combine(Folder, "items", card.Card.File));
        try { QaNotes.Add(file, notes, DateTime.Now); }
        catch (Exception e) { return $"Couldn't write the notes into the card: {e.Message}"; }
        Rework(card.Card, file, notes);
        _notice = $"Sent {card.Id} back with your notes";
        var working = _board.Columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? _board.Columns.FirstOrDefault();
        if (working is not null && BoardStore.PlaceOf(card.Card.Status, _board.Columns) != working.Id) Move(card.Card.Id, working.Id);
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

    /// <summary>Improve a card (WOR-0073): an agent fills its description out, asking first if the words are
    /// unclear. The card stays where it is and free for Work on once that's done.</summary>
    public void StartImprove(CardViewModel card)
    {
        if (Folder is null || Improve is null) return;
        // the card's panel, left open, would save its old text over what the agent writes
        if (Root.Editor?.CardId == card.Id) Root.Editor = null;
        Improve(card.Card, Path.GetFullPath(Path.Combine(Folder, "items", card.Card.File)));
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
        card.IsPicked ? Shown().Where(c => c.IsPicked).ToList() : [card];

    /// <summary>Cards dropped on one of this row's lists: moved there, those from another row's board brought over to
    /// this board first (they keep their ids). A card with a live agent stays on its board: the agent holds its file's path.</summary>
    public void Drop(IReadOnlyList<CardViewModel> cards, ListViewModel list, string? beforeId)
    {
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
        void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
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
    public string Key => $"{Board.SelectedBoard}/{Id}";
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

    public string Count => Cards.Count == all ? $"{all}" : $"{Cards.Count} / {all}";
    public bool HasCards => Cards.Count > 0;
    /// <summary>Every card showing in this column is picked.</summary>
    public bool AllPicked => Cards.Count > 0 && Cards.All(c => c.IsPicked);
    /// <summary>The column's own pick button: "Pick all" here, or "Unpick" once they all are.</summary>
    public string PickText => AllPicked ? "Unpick" : "Pick all";
    [RelayCommand] void Pick() => Board.PickList(this);
    public void PickedChanged() { OnPropertyChanged(nameof(AllPicked)); OnPropertyChanged(nameof(PickText)); }

    public int All => all;
    public bool IsColumn => Id is not (Core.Board.Backlog or Core.Board.Completed or Core.Board.Unsorted);
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
    public IReadOnlyList<MoveTarget> MoveTargets { get; }
    /// <summary>The agent working on this card (its state shows on the card, live), if any.</summary>
    public AgentViewModel? Agent { get; }
    public bool HasAgent => Agent is not null;
    public IRelayCommand WorkOnCommand { get; }
    public IRelayCommand OpenAgentCommand { get; }
    public IRelayCommand ImproveCommand { get; }

    public CardViewModel(Card card, IEnumerable<(string Id, string Name)> lists, BoardViewModel board, AgentViewModel? agent = null,
        IEnumerable<string>? otherBoards = null)
    {
        Card = card;
        Board = board;
        Agent = agent;
        WorkOnCommand = new RelayCommand(() => board.StartWork(this));
        OpenAgentCommand = new RelayCommand(() => { if (Agent is not null) board.OpenAgent?.Invoke(Agent); });
        ImproveCommand = new RelayCommand(() => board.StartImprove(this));
        var inQa = board.InQa(card);
        MoveTargets = [
            new MoveTarget(agent is not null ? "Open its agent" : inQa ? "▶ Get it running for me to try" : "Work on this with an agent", agent is null ? WorkOnCommand : OpenAgentCommand),
            // sending back takes notes, typed in the card's panel
            .. agent is null && (inQa || board.InAttention(card)) ? [
                new MoveTarget("✓ Approve: move to done", new RelayCommand(() => { if (board.Approve(card.Id) is null) board.Root.Approved?.Invoke(card.Id); })),
                new MoveTarget("↩ Send back with notes…", new RelayCommand(() => board.Open(this)))] : Array.Empty<MoveTarget>(),
            .. agent is null ? [new MoveTarget("✦ Improve this card (an agent fills it out)", ImproveCommand)] : Array.Empty<MoveTarget>(),
            .. (agent is null ? board.FlowList() : []).Select(f => new MoveTarget("Run flow: " + f.Name, new RelayCommand(() => board.StartFlow(this, f)))),
            .. lists.Select(l => new MoveTarget("Move to " + l.Name, new RelayCommand(() => board.Move(this, l.Id)))),
            // a card with a live agent stays put: the agent holds its file's path
            .. agent is null ? (otherBoards ?? []).Select(b => new MoveTarget("Move to board " + b, new RelayCommand(() => board.MoveToBoard(this, b)))) : [],
        ];
    }
}

public sealed record MoveTarget(string Label, IRelayCommand Command);

/// <summary>One of the project's other boards, with a tick for showing it as a row beside the picked one.</summary>
public sealed partial class BoardChoice(BoardViewModel board, string name, bool shown) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] public partial bool IsShown { get; set; } = shown;
    partial void OnIsShownChanged(bool value) => board.SetShown(Name, value);
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Pictures))] public partial string Description { get; set; }
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
    /// <summary>The card's live agent, looked up now: the panel can outlast the agent (forgotten while it's open).</summary>
    bool HasAgent => _card is not null && (_board.AgentFor?.Invoke(_card.Id) is not null);
    public bool CanWorkOn => _card is not null && !HasAgent;
    /// <summary>The card is in the board's QA column: Work on gets it running to try, and it can be sent back.</summary>
    public bool InQa => _card is not null && _board.InQa(_card.Card);
    public bool InAttention => _card is not null && _board.InAttention(_card.Card);
    public bool CanSendBack => (InQa || InAttention) && CanWorkOn;
    /// <summary>A report for this card exists (its decision report, once it has landed in needs attention).</summary>
    public bool HasReport => _card is not null && _board.ReportFor?.Invoke(_card.Id) is not null;
    [RelayCommand]
    void OpenReport() { if (_card is not null && _board.ReportFor?.Invoke(_card.Id) is { } f) { _board.Root.Editor = null; _board.OpenReport?.Invoke(f); } }
    public string WorkOnText => InQa ? "▶ Get it running to try" : "▶ Work on this";
    /// <summary>What's wrong, typed while trying a card in QA: it goes into the card and to the agent that reworks it.</summary>
    [ObservableProperty] public partial string SendBackNotes { get; set; } = "";
    /// <summary>A button per flow, to run it on this card.</summary>
    public IReadOnlyList<MoveTarget> FlowTargets { get; private set; } = [];

    public CardEditor(BoardViewModel board, CardViewModel? card)
    {
        _board = board; _card = card;
        Title = card?.Title ?? "";
        Description = card?.Description ?? "";
        LabelsText = card is null ? "" : string.Join(", ", card.Labels);
        Priority = card?.Priority ?? "";
        Type = CardTypes.All.FirstOrDefault(t => t.Equals(card?.Card.Kind, StringComparison.OrdinalIgnoreCase)) ?? card?.Card.Kind ?? CardTypes.Task;
        TypeChoices = CardTypes.All.Contains(Type) ? CardTypes.All : [.. CardTypes.All, Type];
        FlowTargets = MakeFlowTargets();
    }

    List<MoveTarget> MakeFlowTargets() => _card is not { } card || HasAgent ? [] : _board.FlowList().Select(f => new MoveTarget("▶ " + f.Name, new RelayCommand(() =>
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
        OnPropertyChanged(nameof(CanSendBack)); OnPropertyChanged(nameof(CanQaVerdict)); OnPropertyChanged(nameof(CanToAttention)); OnPropertyChanged(nameof(HasReport)); OnPropertyChanged(nameof(InAttention));
    }

    IEnumerable<string> LabelList() => LabelsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    [RelayCommand]
    void Save()
    {
        if (_board.Folder is null) return;
        if (Title.Trim().Length == 0) { Error = "A card needs a title."; return; }
        try
        {
            // a card made on the inbox goes on to the story board Haiku says it fits (WOR-0087)
            if (_card is null) _board.PlaceLater(_board.Folder, BoardStore.Create(_board.Folder, Title, Description, LabelList(), Priority, Type).Id);
            else BoardStore.Update(_board.Folder, _card.Id, Title, Description, LabelList(), Priority,
                // a blank Type stays blank unless it was changed (blank already reads as Task)
                _card.Card.Type.Length == 0 && Type == CardTypes.Task ? null : Type);
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
        _board.Root.Editor = null;
        _board.StartWork(_card);
    }

    /// <summary>The QA verdict buttons (WOR-0095): a card in QA with no agent running can be sent to Needs Phil Attention
    /// (its decision report is written by the board's reload), approved to done, or sent back with notes below.</summary>
    public bool CanQaVerdict => (InQa || InAttention) && CanWorkOn;
    public bool CanToAttention => InQa && HasAttentionColumn;
    public bool HasAttentionColumn => _board.HasAttentionColumn;
    [RelayCommand]
    void QaToAttention()
    {
        if (_card is null) return;
        if (_board.MoveToAttention(_card.Id) is { } error) { Error = error; return; }
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
