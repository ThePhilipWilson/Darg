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

    string? Folder => SelectedBoard is null || _project.Boards is null ? null : Path.Combine(_project.Boards, SelectedBoard);

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
            Lists.Add(new ListViewModel(this, id, name, cards.Select(c => new CardViewModel(c, targets, this)), _board.Lists[id].Count));
        }
        Status = $"{total} of {_board.AllCards.Count()} cards";
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

    public CardViewModel(Card card, IEnumerable<(string Id, string Name)> lists, BoardViewModel board)
    {
        Card = card;
        MoveTargets = lists.Select(l => new MoveTarget("Move to " + l.Name, new RelayCommand(() => board.Move(this, l.Id)))).ToList();
    }
}

public sealed record MoveTarget(string Label, IRelayCommand Command);
