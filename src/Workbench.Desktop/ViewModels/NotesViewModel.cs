using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>The notes wall: post-its in columns, filtered by text, tag and colour, edited in a side
/// panel, kept live from disk (a note written in Obsidian shows up here). Ctrl- or Shift-click picks
/// notes (after the first, a plain click picks too); picked notes can be made tickets in one go.</summary>
public partial class NotesViewModel : ObservableObject
{
    static readonly Dictionary<string, Color> Paper = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yellow"] = Color.Parse("#f2dd7a"), ["pink"] = Color.Parse("#f2a7c3"), ["blue"] = Color.Parse("#9fd0f2"),
        ["green"] = Color.Parse("#b6e3a0"), ["orange"] = Color.Parse("#f5bf86"),
    };
    public static IBrush BrushFor(string colour) => new SolidColorBrush(Paper.GetValueOrDefault(colour, Paper["yellow"]));

    readonly FileSystemWatcher? _watch, _watchBoards;
    readonly DispatcherTimer _debounce;
    List<Note> _all = [];
    List<Note> _archived = [];
    /// <summary>Every ticket's Status, by card ID, read from the project's boards on each reload.</summary>
    Dictionary<string, string> _statuses = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Picked note ids. Only ever notes that are showing: a filter that hides one unpicks it, so
    /// a batch never touches a note you can't see.</summary>
    readonly HashSet<string> _picked = [];
    /// <summary>What was just done ("Made 3 tickets…"): it stays in the status line through the reload the
    /// file watcher does a moment later, until the filter changes or a note is picked.</summary>
    string? _notice;

    public string Folder { get; }
    /// <summary>The project's boards folder, where the notes' tickets are looked up (null when there's none).</summary>
    public string? Boards { get; }
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
    public int PickedCount => _picked.Count;
    public bool HasPicked => _picked.Count > 0;
    public string PickedText => _picked.Count == 1 ? "1 picked" : $"{_picked.Count} picked";
    /// <summary>"Make 3 tickets", counting only picked notes that don't have one yet.</summary>
    public string MakeTicketsText
    {
        get
        {
            var n = Shown().Count(v => v.IsPicked && !v.HasTicket);
            return n == 1 ? "Make 1 ticket" : $"Make {n} tickets";
        }
    }
    public bool CanMakeTickets => Shown().Any(v => v.IsPicked && !v.HasTicket);

    /// <summary>Show the archive (notes whose ticket is completed) as a last column.</summary>
    [ObservableProperty] public partial bool ShowArchive { get; set; }
    public string ArchiveText => ShowArchive ? "Hide archive" : $"Archive ({_archived.Count})";
    public bool HasArchive => _archived.Count > 0;
    [RelayCommand] void ToggleArchive() => ShowArchive = !ShowArchive;

    public NotesViewModel(string folder, string? boards = null)
    {
        Folder = folder;
        Boards = boards;
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
        // a ticket moving on the board changes its note: completed sends the note to the archive
        try
        {
            if (boards is not null && Directory.Exists(boards))
            {
                _watchBoards = new FileSystemWatcher(boards, "*.md") { IncludeSubdirectories = true, EnableRaisingEvents = true };
                void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
                _watchBoards.Changed += Poke; _watchBoards.Created += Poke; _watchBoards.Deleted += Poke; _watchBoards.Renamed += (s, e) => Poke(s, e);
            }
        }
        catch (Exception e) { Status = $"Can't watch {boards}: {e.Message}"; }
    }

    partial void OnFilterChanged(string value) { _notice = null; Apply(); }
    partial void OnShowArchiveChanged(bool value) { OnPropertyChanged(nameof(ArchiveText)); Apply(); }

    public void Reload()
    {
        try
        {
            _statuses = NoteStore.TicketStatuses(Boards);
            var (archived, restored) = NoteStore.SyncArchive(Folder, _statuses);
            if (archived + restored > 0)
                _notice = (archived, restored) switch
                {
                    (1, 0) => "A note's ticket is completed: moved the note to the archive",
                    (_, 0) => $"{archived} notes' tickets are completed: moved them to the archive",
                    (0, 1) =>"An archived note's ticket is open again: put it back on the wall",
                    (0, _) => $"{restored} archived notes' tickets are open again: put them back on the wall",
                    _ => $"Archived {archived} notes with completed tickets, put back {restored} reopened",
                };
            _all = NoteStore.List(Folder);
            _archived = NoteStore.Archived(Folder);
            ColumnNames = NoteStore.Columns(Folder);
        }
        catch (Exception e) { Status = $"Couldn't read the notes: {e.Message}"; return; }
        if (_archived.Count == 0) ShowArchive = false;
        OnPropertyChanged(nameof(ArchiveText));
        OnPropertyChanged(nameof(HasArchive));
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
        var names = ColumnNames.Where(c => !c.Equals(NoteStore.Ticketed, StringComparison.OrdinalIgnoreCase)).ToList();
        var loose = _all.Where(n => n.Ticket.Length == 0).ToList();
        // a note naming a column the wall doesn't have still shows, in a column of its own
        foreach (var extra in loose.Select(n => n.Column).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!names.Contains(extra, StringComparer.OrdinalIgnoreCase)) names.Add(extra);
        Columns.Clear();
        foreach (var name in names)
            Columns.Add(new NoteColumn(name, loose.Where(n => n.Column.Equals(name, StringComparison.OrdinalIgnoreCase) && Keep(n))
                .Select(n => new NoteViewModel(n, this) { IsPicked = _picked.Contains(n.Id) })));
        // a note with a ticket leaves its column for Ticketed, where it shows how far its ticket has got
        if (_all.Any(n => n.Ticket.Length > 0))
            Columns.Add(new NoteColumn(NoteStore.Ticketed, _all.Where(n => n.Ticket.Length > 0 && Keep(n))
                .Select(n => new NoteViewModel(n, this, _statuses.GetValueOrDefault(n.Ticket)) { IsPicked = _picked.Contains(n.Id) })));
        if (ShowArchive)
            Columns.Add(new NoteColumn("Archive", _archived.Where(Keep)
                .Select(n => new NoteViewModel(n, this, _statuses.GetValueOrDefault(n.Ticket)) { IsArchived = true }), isArchive: true));
        _picked.IntersectWith(Shown().Select(v => v.Id));
        PickedChanged();
        var shown = Shown().Count();
        Status = _notice ?? (shown == _all.Count ? $"{_all.Count} notes" : $"{shown} of {_all.Count} notes");
    }

    /// <summary>The notes on the wall, not counting the archive (its notes can't be picked or edited).</summary>
    IEnumerable<NoteViewModel> Shown() => Columns.Where(c => !c.IsArchive).SelectMany(c => c.Notes);

    /// <summary>A click on a post-it: with Ctrl or Shift held, or once anything is picked, it picks or
    /// unpicks the note; otherwise it opens the note to edit. An archived note opens its ticket.</summary>
    public void Click(NoteViewModel n, KeyModifiers keys)
    {
        if (n.IsArchived) { OpenTicket?.Invoke(n.Note.Ticket); return; }
        if (!HasPicked && (keys & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) == 0) { n.Edit(); return; }
        Editor = null;
        _notice = null;
        n.IsPicked = !n.IsPicked;
        if (n.IsPicked) _picked.Add(n.Id); else _picked.Remove(n.Id);
        PickedChanged();
    }

    void PickedChanged()
    {
        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(HasPicked));
        OnPropertyChanged(nameof(PickedText));
        OnPropertyChanged(nameof(MakeTicketsText));
        OnPropertyChanged(nameof(CanMakeTickets));
    }

    /// <summary>Pick every note showing (the filter decides what "all" is).</summary>
    [RelayCommand]
    void PickAll()
    {
        Editor = null;
        foreach (var v in Shown()) { v.IsPicked = true; _picked.Add(v.Id); }
        PickedChanged();
        _notice = null;
        Apply();
    }

    [RelayCommand]
    public void ClearPicked()
    {
        foreach (var v in Shown()) v.IsPicked = false;
        _picked.Clear();
        PickedChanged();
    }

    [RelayCommand]
    void MakeTickets()
    {
        var board = TicketBoard?.Invoke();
        if (board is null) { Status = "This project has no board to put tickets on."; return; }
        var notes = _all.Where(n => _picked.Contains(n.Id)).ToList();
        List<Card> cards;
        try { cards = NoteStore.MakeTickets(Folder, notes, board); }
        catch (Exception e) { _notice = $"Stopped making tickets: {e.Message}"; Reload(); return; }
        _picked.Clear();
        _notice = cards.Count switch
        {
            0 => "Those notes already have tickets.",
            1 => $"Made {cards[0].Id}: {cards[0].Title}",
            _ => $"Made {cards.Count} tickets: {cards[0].Id} to {cards[^1].Id}",
        };
        Reload();
    }

    /// <summary>Set by the app: hand Fettle a job (the words shown in its chat, the full message). False
    /// when Fettle is busy and nothing was sent.</summary>
    public Func<string, string, bool>? Organise { get; set; }

    /// <summary>Fettle sorts the loose notes showing (the filter decides what "all" is; picked notes, if
    /// any, narrow it to those) into columns, with tags and colours. It edits the files; the wall follows.</summary>
    [RelayCommand]
    void OrganiseAll()
    {
        if (Organise is null) { Status = "Fettle isn't here to organise the notes."; return; }
        var shown = Shown().Where(v => !v.HasTicket && (!HasPicked || v.IsPicked)).Select(v => v.Note).ToList();
        var prompt = NoteStore.OrganisePrompt(Folder, shown, ColumnNames.Where(c => !c.Equals(NoteStore.Ticketed, StringComparison.OrdinalIgnoreCase)).ToList());
        if (prompt is null) { Status = "No loose notes to organise (notes with tickets stay as they are)."; return; }
        var what = HasPicked ? $"the {shown.Count} picked notes" : shown.Count == _all.Count(n => n.Ticket.Length == 0) ? "all the notes" : $"the {shown.Count} notes showing";
        if (!Organise($"Organise {what}", prompt)) { Status = "Fettle is busy with something else: try again when it's done."; return; }
        Editor = null;
        ClearPicked();
        _notice = $"Asked Fettle to organise {what}: its box says how it went";
        Apply();
    }

    [RelayCommand] void NewNote() => Editor = new NoteEditor(this, null);
    public void Edit(Note n) => Editor = new NoteEditor(this, n);

    public void Move(string noteId, string column)
    {
        // Ticketed and the archive follow the ticket, not where a note is dropped
        if (column.Equals(NoteStore.Ticketed, StringComparison.OrdinalIgnoreCase) || column == "Archive") return;
        var n = _all.FirstOrDefault(x => x.Id == noteId);
        if (n is null || n.Ticket.Length > 0 || n.Column == column) return;
        NoteStore.Save(Folder, n with { Column = column });
        Reload();
    }

    internal void Saved(string? message = null)
    {
        Editor = null;
        _notice = message;
        Reload();
    }
}

public sealed record ColourChoice(string Name, IBrush Brush, IRelayCommand Command);

public sealed class NoteColumn(string name, IEnumerable<NoteViewModel> notes, bool isArchive = false)
{
    public string Name { get; } = name;
    public bool IsArchive { get; } = isArchive;
    public ObservableCollection<NoteViewModel> Notes { get; } = new(notes);
}

public sealed partial class NoteViewModel(Note note, NotesViewModel wall, string? ticketStatus = null) : ObservableObject
{
    [ObservableProperty] public partial bool IsPicked { get; set; }
    public bool IsArchived { get; init; }
    public double PaperOpacity => IsArchived ? 0.6 : 1;
    public Note Note { get; } = note;
    public string Id => Note.Id;
    public string Text => Note.Text;
    public IBrush Brush { get; } = NotesViewModel.BrushFor(note.Colour);
    public string Meta => string.Join(" ", Note.Tags.Select(t => "#" + t));
    /// <summary>"→ WOR-0015 · In Progress": the ticket and where it is on the board ("not found" once it's gone).</summary>
    public string Ticket => Note.Ticket.Length == 0 ? ""
        : $"→ {Note.Ticket} · " + (ticketStatus is null ? "not found" : ticketStatus.Length == 0 ? "Backlog" : ticketStatus);
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
