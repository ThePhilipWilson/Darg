using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>A project's boards: one shown at a time, filtered by text and labels, kept live from disk
/// (Tatty, Obsidian or an agent changing a card shows here within a moment).</summary>
public partial class BoardViewModel : ObservableObject, IDisposable
{
    readonly ProjectConfig _project;
    FileSystemWatcher? _watch;
    DispatcherTimer? _debounce;
    Board? _board;

    /// <summary>Set by the project: the live agent working on a card, if any.</summary>
    public Func<string, AgentViewModel?>? AgentFor { get; set; }
    /// <summary>Set by the project: start an agent on a card. Passed: the card's file path, the ids of cards
    /// other agents are being started on at the same time, and whether to show the agent.</summary>
    public Action<Card, string, IReadOnlyList<string>, bool>? WorkOn { get; set; }
    /// <summary>Picked card ids. Only ever cards that are showing: a filter that hides one unpicks it, so a
    /// batch never touches a card you can't see.</summary>
    readonly HashSet<string> _picked = [];
    /// <summary>What was just done ("Started 3 agents…"): it stays in the status line through the reload
    /// the file watcher does a moment later, until the filter changes or a card is picked.</summary>
    string? _notice;
    /// <summary>Set by the project: show an agent.</summary>
    public Action<AgentViewModel>? OpenAgent { get; set; }

    public ObservableCollection<string> Boards { get; } = [];
    public ObservableCollection<ListViewModel> Lists { get; } = [];
    public ObservableCollection<LabelChip> Labels { get; } = [];

    [ObservableProperty] public partial string? SelectedBoard { get; set; }
    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial bool ShowCompleted { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditing))] public partial CardEditor? Editor { get; set; }
    public bool IsEditing => Editor is not null;
    [RelayCommand] void NewCard() { if (Folder is not null) Editor = new CardEditor(this, null); }
    public void Open(CardViewModel card) => Editor = new CardEditor(this, card);

    /// <summary>Open a card by its ID, on whichever board holds it (clearing filters that hide it).</summary>
    public bool OpenById(string id)
    {
        foreach (var b in Boards.ToList())
        {
            if (_project.Boards is null) break;
            if (!BoardStore.ReadCards(Path.Combine(_project.Boards, b)).Any(c => c.Id == id)) continue;
            if (SelectedBoard != b) SelectedBoard = b;
            Filter = "";
            foreach (var l in Labels) l.Mode = ChipMode.Off;
            if (!ShowCompleted && _board?.Lists[Board.Completed].Any(c => c.Id == id) == true) ShowCompleted = true;
            var card = Lists.SelectMany(l => l.Cards).FirstOrDefault(c => c.Id == id);
            if (card is null) return false;
            Open(card);
            return true;
        }
        Status = $"No card {id} on this project's boards.";
        return false;
    }

    public void MarkDone(CardViewModel card)
    {
        if (_board is null) return;
        Move(card, _board.Columns.LastOrDefault()?.Id ?? Board.Completed);
    }

    public BoardViewModel(ProjectConfig project)
    {
        _project = project;
        foreach (var b in BoardStore.List(project.Boards)) Boards.Add(b);
        SelectedBoard = Boards.FirstOrDefault();
        if (Boards.Count == 0) Status = project.Boards is null ? "This project has no boards folder set." : $"No boards in {project.Boards}.";
    }

    partial void OnSelectedBoardChanged(string? value) { Watch(); Reload(); }
    partial void OnFilterChanged(string value) { _notice = null; Apply(); }
    partial void OnShowCompletedChanged(bool value) { _notice = null; Apply(); }

    public bool HasPicked => _picked.Count > 0;
    public string PickedText => _picked.Count == 1 ? "1 picked" : $"{_picked.Count} picked";
    IEnumerable<CardViewModel> Shown() => Lists.SelectMany(l => l.Cards);
    int ToStart => Shown().Count(c => c.IsPicked && !c.HasAgent);
    /// <summary>"▶ Work on 3", counting only picked cards with no agent on them yet.</summary>
    public string WorkOnPickedText => $"▶ Work on {ToStart}";
    public bool CanWorkOnPicked => ToStart > 0;

    /// <summary>A click on a card: with Ctrl or Shift held, or once anything is picked, it picks or unpicks
    /// the card; otherwise it opens the card.</summary>
    public void Click(CardViewModel card, Avalonia.Input.KeyModifiers keys)
    {
        const Avalonia.Input.KeyModifiers pick = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift | Avalonia.Input.KeyModifiers.Meta;
        if (!HasPicked && (keys & pick) == 0) { Open(card); return; }
        Editor = null;
        _notice = null;
        card.IsPicked = !card.IsPicked;
        if (card.IsPicked) _picked.Add(card.Id); else _picked.Remove(card.Id);
        PickedChanged();
    }

    void PickedChanged()
    {
        OnPropertyChanged(nameof(HasPicked));
        OnPropertyChanged(nameof(PickedText));
        OnPropertyChanged(nameof(WorkOnPickedText));
        OnPropertyChanged(nameof(CanWorkOnPicked));
        foreach (var l in Lists) l.PickedChanged();
    }

    /// <summary>Pick every card showing (the filter, and Show completed, decide what "all" is).</summary>
    [RelayCommand]
    void PickAll()
    {
        Editor = null;
        _notice = null;
        foreach (var c in Shown()) { c.IsPicked = true; _picked.Add(c.Id); }
        PickedChanged();
    }

    /// <summary>Pick every card showing in one column, or unpick them all if they already are.</summary>
    public void PickList(ListViewModel list)
    {
        if (list.Cards.Count == 0) return;
        Editor = null;
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
        foreach (var c in Shown()) c.IsPicked = false;
        _picked.Clear();
        PickedChanged();
    }

    /// <summary>Start an agent on every picked card that has none, all at once in the project's folder;
    /// each is told which other cards are being worked on beside it. The board stays showing, and each
    /// card shows its agent's state.</summary>
    [RelayCommand]
    void WorkOnPicked()
    {
        var cards = Shown().Where(c => c.IsPicked && !c.HasAgent).Select(c => c.Card).ToList();
        if (cards.Count == 0) return;
        _picked.Clear();
        var started = new List<string>();
        foreach (var card in cards)
        {
            try { StartWork(card, cards.Where(c => c != card).Select(c => c.Id).ToList(), show: false, reload: false); }
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

    /// <summary>Reload now. A header being edited is given up (this is a click elsewhere: a move, another board).</summary>
    public void Reload()
    {
        if (_held is not null) { _held.IsRenaming = false; _held.ConfirmingRemove = false; _held = null; }
        _reloadWaiting = false;
        if (Folder is null) return;
        try { _board = BoardStore.Load(Folder); }
        catch (Exception e) { Status = $"Couldn't read the board: {e.Message}"; return; }
        var was = Labels.ToDictionary(l => l.Name, l => l.Mode, StringComparer.OrdinalIgnoreCase);
        foreach (var chip in Labels) chip.Changed -= Apply;
        Labels.Clear();
        foreach (var name in _board.AllCards.SelectMany(c => c.Labels).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var chip = new LabelChip(name) { Mode = was.GetValueOrDefault(name) };
            chip.Changed += Apply;
            Labels.Add(chip);
        }
        Apply();
    }

    /// <summary>Rebuild the lists from the board through the filter: text matches the ID, title or
    /// description; labels: every must-have one (double-clicked) and, if any are clicked, one of those.</summary>
    void Apply()
    {
        if (_board is null) return;
        var words = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (any, all) = LabelChip.Picked(Labels);
        bool Keep(Card c) =>
            LabelFilter.Matches(c.Labels, any, all) &&
            words.All(w => c.Id.Contains(w, StringComparison.OrdinalIgnoreCase) || c.Title.Contains(w, StringComparison.OrdinalIgnoreCase)
                           || c.Description.Contains(w, StringComparison.OrdinalIgnoreCase));
        var targets = _board.ListOrder().Where(l => l.Id != Board.Unsorted).Select(l => (l.Id, l.Name)).ToList();
        Lists.Clear();
        var total = 0;
        foreach (var (id, name) in _board.ListOrder())
        {
            if (id == Board.Completed && !ShowCompleted) continue;
            var cards = _board.Lists[id].Where(Keep).ToList();
            total += cards.Count;
            Lists.Add(new ListViewModel(this, id, name, cards.Select(c =>
                new CardViewModel(c, targets, this, AgentFor?.Invoke(c.Id)) { IsPicked = _picked.Contains(c.Id) }), _board.Lists[id].Count,
                _board.Minimized.Contains(id)));
        }
        _picked.IntersectWith(Shown().Select(c => c.Id));
        PickedChanged();
        Status = _notice ?? $"{total} of {_board.AllCards.Count()} cards";
    }

    /// <summary>Start an agent on a card: it goes to the board's working column ("In Progress", else
    /// the first column) if it was waiting in the backlog or the first column, and the agent opens.</summary>
    public void StartWork(CardViewModel card) => StartWork(card.Card, [], show: true, reload: true);

    void StartWork(Card card, IReadOnlyList<string> alongside, bool show, bool reload)
    {
        if (Folder is null || _board is null || WorkOn is null) return;
        var working = _board.Columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? _board.Columns.FirstOrDefault();
        var place = BoardStore.PlaceOf(card.Status, _board.Columns);
        WorkOn(card, Path.GetFullPath(Path.Combine(Folder, "items", card.File)), alongside, show);
        if (working is not null && (place == Board.Backlog || place == _board.Columns[0].Id) && place != working.Id)
            Move(card.Id, working.Id, reload: reload);
        else if (reload) Reload();
    }

    public void Move(CardViewModel card, string toList, string? beforeId = null) => Move(card.Card.Id, toList, beforeId);

    void Move(string id, string toList, string? beforeId = null, bool reload = true)
    {
        if (Folder is null) return;
        try { BoardStore.Move(Folder, id, toList, beforeId); }
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

    public void Dispose() => _watch?.Dispose();
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
    public string Name { get; } = name;
    public ObservableCollection<CardViewModel> Cards { get; } = new(cards);
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
    public string Id => Card.Id;
    public string Title => Card.Title;
    public string Priority => Card.Priority;
    public bool HasPriority => Card.Priority.Length > 0;
    public IReadOnlyList<string> Labels => Card.Labels;
    public string Description => Card.Description;
    public IReadOnlyList<MoveTarget> MoveTargets { get; }
    /// <summary>The agent working on this card (its state shows on the card, live), if any.</summary>
    public AgentViewModel? Agent { get; }
    public bool HasAgent => Agent is not null;
    public IRelayCommand WorkOnCommand { get; }
    public IRelayCommand OpenAgentCommand { get; }

    public CardViewModel(Card card, IEnumerable<(string Id, string Name)> lists, BoardViewModel board, AgentViewModel? agent = null)
    {
        Card = card;
        Agent = agent;
        WorkOnCommand = new RelayCommand(() => board.StartWork(this));
        OpenAgentCommand = new RelayCommand(() => { if (Agent is not null) board.OpenAgent?.Invoke(Agent); });
        MoveTargets = [
            new MoveTarget(agent is null ? "Work on this with an agent" : "Open its agent", agent is null ? WorkOnCommand : OpenAgentCommand),
            .. lists.Select(l => new MoveTarget("Move to " + l.Name, new RelayCommand(() => board.Move(this, l.Id)))),
        ];
    }
}

public sealed record MoveTarget(string Label, IRelayCommand Command);

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
    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial string Description { get; set; }
    [ObservableProperty] public partial string LabelsText { get; set; }
    [ObservableProperty] public partial string Priority { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public bool CanWorkOn => _card is not null && !_card.HasAgent;

    public CardEditor(BoardViewModel board, CardViewModel? card)
    {
        _board = board; _card = card;
        Title = card?.Title ?? "";
        Description = card?.Description ?? "";
        LabelsText = card is null ? "" : string.Join(", ", card.Labels);
        Priority = card?.Priority ?? "";
    }

    IEnumerable<string> LabelList() => LabelsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    [RelayCommand]
    void Save()
    {
        if (_board.Folder is null) return;
        if (Title.Trim().Length == 0) { Error = "A card needs a title."; return; }
        try
        {
            if (_card is null) BoardStore.Create(_board.Folder, Title, Description, LabelList(), Priority);
            else BoardStore.Update(_board.Folder, _card.Id, Title, Description, LabelList(), Priority);
        }
        catch (Exception e) { Error = e.Message; return; }
        _board.Editor = null;
        _board.Reload();
    }

    [RelayCommand] void Cancel() => _board.Editor = null;

    public bool CanDone => _card is not null;
    [ObservableProperty] public partial bool ConfirmingDelete { get; set; }
    public string DeleteLabel => ConfirmingDelete ? "Click again to delete" : "Delete";
    partial void OnConfirmingDeleteChanged(bool value) => OnPropertyChanged(nameof(DeleteLabel));

    /// <summary>Finish the card: to the board's last column (its done column), or Completed if it has none.</summary>
    [RelayCommand]
    void Done()
    {
        if (_card is null) return;
        _board.Editor = null;
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
        _board.Editor = null;
        _board.Reload();
        _board.Status = $"Deleted {_card.Id} (it's in the board's .deleted folder)";
    }

    [RelayCommand]
    void WorkOn()
    {
        if (_card is null) return;
        _board.Editor = null;
        _board.StartWork(_card);
    }
}

public static class Bools
{
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, string> CreateOrSave = new(isNew => isNew ? "Create" : "Save");
}
