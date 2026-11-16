using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>The notes wall: post-its in columns, filtered by text, tag and colour, edited in a side
/// panel, kept live from disk (a note written in Obsidian shows up here).</summary>
public partial class NotesViewModel : ObservableObject
{
    static readonly Dictionary<string, Color> Paper = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yellow"] = Color.Parse("#f2dd7a"), ["pink"] = Color.Parse("#f2a7c3"), ["blue"] = Color.Parse("#9fd0f2"),
        ["green"] = Color.Parse("#b6e3a0"), ["orange"] = Color.Parse("#f5bf86"),
    };
    public static IBrush BrushFor(string colour) => new SolidColorBrush(Paper.GetValueOrDefault(colour, Paper["yellow"]));

    readonly FileSystemWatcher? _watch;
    readonly DispatcherTimer _debounce;
    List<Note> _all = [];

    public string Folder { get; }
    /// <summary>Set by the project: the board folder a note's ticket goes to (null when there's none).</summary>
    public Func<string?>? TicketBoard { get; set; }
    /// <summary>Set by the project: show a card on the board by its ID.</summary>
    public Action<string>? OpenTicket { get; set; }
    public ObservableCollection<NoteColumn> Columns { get; } = [];
    public ObservableCollection<LabelChip> Tags { get; } = [];
    public ObservableCollection<LabelChip> Colours { get; } = [];
    public IReadOnlyList<string> ColumnNames { get; private set; } = NoteStore.DefaultColumns;
    public IReadOnlyList<ColourChoice> ColourChoices { get; }

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditing))] public partial NoteEditor? Editor { get; set; }
    public bool IsEditing => Editor is not null;

    public NotesViewModel(string folder)
    {
        Folder = folder;
        ColourChoices = NoteStore.Colours.Select(c => new ColourChoice(c, BrushFor(c), new RelayCommand(() => { if (Editor is not null) Editor.Colour = c; }))).ToList();
        foreach (var c in NoteStore.Colours)
        {
            var chip = new LabelChip(c);
            chip.Changed += Apply;
            Colours.Add(chip);
        }
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); Reload(); });
        Reload();
        try
        {
            Directory.CreateDirectory(folder);
            _watch = new FileSystemWatcher(folder, "*.md") { EnableRaisingEvents = true };
            void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
            _watch.Changed += Poke; _watch.Created += Poke; _watch.Deleted += Poke; _watch.Renamed += (s, e) => Poke(s, e);
        }
        catch (Exception e) { Status = $"Can't watch {folder}: {e.Message}"; }
    }

    partial void OnFilterChanged(string value) => Apply();

    public void Reload()
    {
        try { _all = NoteStore.List(Folder); ColumnNames = NoteStore.Columns(Folder); }
        catch (Exception e) { Status = $"Couldn't read the notes: {e.Message}"; return; }
        var was = Tags.ToDictionary(t => t.Name, t => t.Mode, StringComparer.OrdinalIgnoreCase);
        foreach (var t in Tags) t.Changed -= Apply;
        Tags.Clear();
        foreach (var name in _all.SelectMany(n => n.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var chip = new LabelChip(name) { Mode = was.GetValueOrDefault(name) };
            chip.Changed += Apply;
            Tags.Add(chip);
        }
        Apply();
    }

    void Apply()
    {
        var words = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var (anyTag, allTags) = LabelChip.Picked(Tags);
        var (anyColour, allColours) = LabelChip.Picked(Colours); // a note has one colour, so "must" two colours keeps none
        bool Keep(Note n) => LabelFilter.Matches(n.Tags, anyTag, allTags) && LabelFilter.Matches([n.Colour], anyColour, allColours)
                             && words.All(w => n.Text.Contains(w, StringComparison.OrdinalIgnoreCase));
        var names = ColumnNames.ToList();
        // a note naming a column the wall doesn't have still shows, in a column of its own
        foreach (var extra in _all.Select(n => n.Column).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!names.Contains(extra, StringComparer.OrdinalIgnoreCase)) names.Add(extra);
        Columns.Clear();
        foreach (var name in names)
            Columns.Add(new NoteColumn(name, _all.Where(n => n.Column.Equals(name, StringComparison.OrdinalIgnoreCase) && Keep(n)).Select(n => new NoteViewModel(n, this))));
        var shown = Columns.Sum(c => c.Notes.Count);
        Status = shown == _all.Count ? $"{_all.Count} notes" : $"{shown} of {_all.Count} notes";
    }

    [RelayCommand] void NewNote() => Editor = new NoteEditor(this, null);
    public void Edit(Note n) => Editor = new NoteEditor(this, n);

    public void Move(string noteId, string column)
    {
        var n = _all.FirstOrDefault(x => x.Id == noteId);
        if (n is null || n.Column == column) return;
        NoteStore.Save(Folder, n with { Column = column });
        Reload();
    }

    internal void Saved(string? message = null)
    {
        Editor = null;
        Reload();
        if (message is not null) Status = message;
    }
}

public sealed record ColourChoice(string Name, IBrush Brush, IRelayCommand Command);

public sealed class NoteColumn(string name, IEnumerable<NoteViewModel> notes)
{
    public string Name { get; } = name;
    public ObservableCollection<NoteViewModel> Notes { get; } = new(notes);
}

public sealed class NoteViewModel(Note note, NotesViewModel wall)
{
    public Note Note { get; } = note;
    public string Id => Note.Id;
    public string Text => Note.Text;
    public IBrush Brush { get; } = NotesViewModel.BrushFor(note.Colour);
    public string Meta => string.Join(" ", Note.Tags.Select(t => "#" + t));
    public string Ticket => Note.Ticket.Length > 0 ? "→ " + Note.Ticket : "";
    public bool HasTicket => Note.Ticket.Length > 0;
    public IRelayCommand GoToTicketCommand { get; } = new RelayCommand(() => wall.OpenTicket?.Invoke(note.Ticket));
    /// <summary>A small tilt, the same for a note every time it is drawn.</summary>
    public double Angle { get; } = (Math.Abs(note.Id.GetHashCode() % 7) - 3) * 0.55;
    public void Edit() => wall.Edit(Note);
}

/// <summary>The side panel for a new or existing note.</summary>
public partial class NoteEditor : ObservableObject
{
    readonly NotesViewModel _wall;
    readonly Note? _note;
    public bool IsNew => _note is null;
    public string Heading => IsNew ? "New note" : "Note";
    public IReadOnlyList<string> ColumnNames => _wall.ColumnNames;
    [ObservableProperty] public partial string Text { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Brush))] public partial string Colour { get; set; }
    [ObservableProperty] public partial string TagsText { get; set; }
    [ObservableProperty] public partial string Column { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public IBrush Brush => NotesViewModel.BrushFor(Colour);
    public string Ticket => _note?.Ticket ?? "";
    public bool CanMakeTicket => _note is not null && _note.Ticket.Length == 0;
    public bool HasTicket => Ticket.Length > 0;

    [RelayCommand]
    void GoToTicket()
    {
        if (Ticket.Length == 0) return;
        _wall.Editor = null;
        _wall.OpenTicket?.Invoke(Ticket);
    }

    public NoteEditor(NotesViewModel wall, Note? note)
    {
        _wall = wall; _note = note;
        Text = note?.Text ?? "";
        Colour = note?.Colour ?? "yellow";
        TagsText = note is null ? "" : string.Join(", ", note.Tags);
        Column = note?.Column ?? wall.ColumnNames[0];
    }

    Note Current() => new(_note?.Id ?? NoteStore.NewId(), Text.Trim(), Column, Colour,
        TagsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).ToList(),
        _note?.Created ?? "", _note?.Ticket ?? "");

    [RelayCommand]
    void Save()
    {
        if (Text.Trim().Length == 0) { Error = "A note needs some text."; return; }
        NoteStore.Save(_wall.Folder, Current());
        _wall.Saved();
    }

    [RelayCommand] void Cancel() => _wall.Saved();

    [RelayCommand]
    void Delete()
    {
        if (_note is not null) NoteStore.Delete(_wall.Folder, _note.Id);
        _wall.Saved("Note moved to notes/.deleted");
    }

    [RelayCommand]
    void MakeTicket()
    {
        var board = _wall.TicketBoard?.Invoke();
        if (board is null) { Error = "This project has no board to put a ticket on."; return; }
        if (Text.Trim().Length == 0) { Error = "A note needs some text."; return; }
        try
        {
            var (_, card) = NoteStore.MakeTicket(_wall.Folder, Current(), board);
            _wall.Saved($"Made {card.Id}: {card.Title}");
        }
        catch (Exception e) { Error = e.Message; }
    }
}
