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
        var on = Labels.Where(l => l.IsOn).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var chip in Labels) chip.Changed -= Apply;
        Labels.Clear();
        foreach (var name in _board.AllCards.SelectMany(c => c.Labels).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var chip = new LabelChip(name) { IsOn = on.Contains(name) };
            chip.Changed += Apply;
            Labels.Add(chip);
        }
        Apply();
    }

    /// <summary>Rebuild the lists from the board through the filter: text matches the ID, title or
    /// description; with labels ticked, a card needs at least one of them.</summary>
    void Apply()
    {
        if (_board is null) return;
        var words = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var labels = Labels.Where(l => l.IsOn).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Keep(Card c) =>
            (labels.Count == 0 || c.Labels.Any(labels.Contains)) &&
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

public partial class LabelChip(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] public partial bool IsOn { get; set; }
    public event Action? Changed;
    partial void OnIsOnChanged(bool value) => Changed?.Invoke();
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
