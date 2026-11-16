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
    /// <summary>Set by the project: start an agent on a card (the card's file path is passed).</summary>
    public Action<Card, string>? WorkOn { get; set; }
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
    partial void OnFilterChanged(string value) => Apply();
    partial void OnShowCompletedChanged(bool value) => Apply();

    public string? Folder => SelectedBoard is null || _project.Boards is null ? null : Path.Combine(_project.Boards, SelectedBoard);

    public void Reload()
    {
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
            Lists.Add(new ListViewModel(this, id, name, cards.Select(c => new CardViewModel(c, targets, this, AgentFor?.Invoke(c.Id))), _board.Lists[id].Count));
        }
        Status = $"{total} of {_board.AllCards.Count()} cards";
    }

    /// <summary>Start an agent on a card: it goes to the board's working column ("In Progress", else
    /// the first column) if it was waiting in the backlog or the first column, and the agent opens.</summary>
    public void StartWork(CardViewModel card)
    {
        if (Folder is null || _board is null || WorkOn is null) return;
        var working = _board.Columns.FirstOrDefault(c => c.Name.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) ?? _board.Columns.FirstOrDefault();
        var place = BoardStore.PlaceOf(card.Card.Status, _board.Columns);
        WorkOn(card.Card, Path.GetFullPath(Path.Combine(Folder, "items", card.Card.File)));
        if (working is not null && (place == Board.Backlog || place == _board.Columns[0].Id) && place != working.Id)
            Move(card, working.Id);
        else Reload();
    }

    public void Move(CardViewModel card, string toList, string? beforeId = null)
    {
        if (Folder is null) return;
        try { BoardStore.Move(Folder, card.Card.Id, toList, beforeId); }
        catch (Exception e) { Status = $"Couldn't move {card.Card.Id}: {e.Message}"; return; }
        Reload();
    }

    void Watch()
    {
        _watch?.Dispose();
        if (Folder is null || !Directory.Exists(Folder)) return;
        _debounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); Reload(); });
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

public sealed class ListViewModel(BoardViewModel board, string id, string name, IEnumerable<CardViewModel> cards, int all)
{
    public BoardViewModel Board { get; } = board;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public ObservableCollection<CardViewModel> Cards { get; } = new(cards);
    public string Count => Cards.Count == all ? $"{all}" : $"{Cards.Count} / {all}";
}

public sealed class CardViewModel
{
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
