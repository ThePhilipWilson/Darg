using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The Reports section (WOR-0061): what card agents filed when they finished, newest first, unread ones marked.
/// Opening one marks it read. Each names its card (Show card) and commits (opened on GitHub when the repo is there).</summary>
public partial class ReportsViewModel : ObservableObject, IDisposable
{
    public string Folder { get; }
    readonly FileSystemWatcher? _watch;
    readonly DispatcherTimer _debounce;

    public ObservableCollection<ReportRow> Reports { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelected))] public partial ReportRow? Selected { get; set; }
    public bool HasSelected => Selected is not null;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUnread), nameof(UnreadText))] public partial int Unread { get; set; }
    public bool HasUnread => Unread > 0;
    public string UnreadText => Unread > 99 ? "99+" : Unread.ToString();
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EmptyText))] public partial bool HasReports { get; set; }
    public string EmptyText => HasReports ? "Pick a report to read it."
        : "No reports yet. A card agent that is sure its work is done moves the card to done, writes a short report here (what was asked, what it did, its commits) and is forgotten; work that needs you to check it goes to the board's QA column with a short QA report here (marked to check), and its agent stays.";
    /// <summary>Only the unread ones in the list.</summary>
    [ObservableProperty] public partial bool UnreadOnly { get; set; }
    /// <summary>The kind filter (WOR-0091): all, finished tasks, investigations or QA reports, each label with its count.</summary>
    public ObservableCollection<string> Kinds { get; } = [AllLabel, FinishedLabel, InvestigationLabel, QaLabel];
    const string AllLabel = "All reports", FinishedLabel = "Finished tasks", InvestigationLabel = "Investigations", QaLabel = "QA reports";
    [ObservableProperty] public partial int KindIndex { get; set; }
    partial void OnKindIndexChanged(int value) { if (value >= 0 && !_relabel) Apply(); }
    ReportKind? KindShown => KindIndex switch { 1 => ReportKind.Finished, 2 => ReportKind.Investigation, 3 => ReportKind.Qa, _ => null };
    [ObservableProperty] public partial string Status { get; set; } = "";

    /// <summary>The story filter (WOR-0113): a story is the board a report's card is on. "All stories", then each with its count.</summary>
    public ObservableCollection<string> Stories { get; } = [AllStories];
    const string AllStories = "All stories";
    List<string> _storyNames = [];
    [ObservableProperty] public partial int StoryIndex { get; set; }
    partial void OnStoryIndexChanged(int value) { if (value >= 0 && !_relabel) Apply(); }
    string? StoryShown => StoryIndex > 0 && StoryIndex <= _storyNames.Count ? _storyNames[StoryIndex - 1] : null;
    /// <summary>Set by the project: each card's board by card id, read when the list loads.</summary>
    public Func<IReadOnlyDictionary<string, string>>? StoryMap { get; set; }
    Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    string StoryOf(AgentReport r) => r.Story.Length > 0 ? r.Story : _map.GetValueOrDefault(r.Card, "No story");

    /// <summary>Browse the archive instead of the main list: finished stories' reports, pulled out of the way.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ArchiveToggleText), nameof(CanArchive), nameof(CanRestore))] public partial bool ShowArchived { get; set; }
    partial void OnShowArchivedChanged(bool value) { StoryIndex = 0; Apply(); }
    public string ArchiveToggleText => ShowArchived ? "← Back to reports" : $"Archive ({_archived.Count})";
    public bool CanArchive => !ShowArchived && StoryShown is not null;
    public bool CanRestore => ShowArchived;
    List<AgentReport> _archived = [];
    [RelayCommand] void ToggleArchived() => ShowArchived = !ShowArchived;

    /// <summary>Move every finished report of the picked story into the archive.</summary>
    [RelayCommand]
    void ArchiveStory()
    {
        if (StoryShown is not { } story) { Status = "Pick a story first."; return; }
        try
        {
            var n = ReportArchive.Archive(Folder, story, _all.Where(r => StoryOf(r) == story));
            Status = n == 0 ? $"Nothing to archive in {story}: its reports are QA or waiting for a decision." : $"Archived {n} {story} {(n == 1 ? "report" : "reports")}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Can't archive: {e.Message}"; }
        StoryIndex = 0;
        Reload();
    }

    /// <summary>Bring the picked story's archived reports back (all of them with no story picked).</summary>
    [RelayCommand]
    void RestoreStory()
    {
        var story = StoryShown;
        try
        {
            var n = ReportArchive.Restore(Folder, _archived.Where(r => story is null || r.Story == story));
            Status = $"Restored {n} {(n == 1 ? "report" : "reports")}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Can't restore: {e.Message}"; }
        StoryIndex = 0;
        Reload();
    }

    /// <summary>Raised with every report after each reload: the agents that wrote them can go.</summary>
    public event Action<IReadOnlyList<AgentReport>>? Loaded;
    /// <summary>Show a card on the board (set by the project); false: no such card there.</summary>
    public Func<string, bool>? OpenCard { get; set; }

    /// <summary>Set by the project (WOR-0092): move a card to done; send one back with notes. Each returns what went wrong, or null.</summary>
    public Func<string, string?>? ApproveCard { get; set; }
    public Func<string, string, string?>? SendCardBack { get; set; }
    /// <summary>What's typed beside Approve and Send back: the notes for the report's Decision section (and the agent, when sent back).</summary>
    [ObservableProperty] public partial string DecisionNotes { get; set; } = "";

    /// <summary>The newest report for a card (its file), from the last load.</summary>
    public string? LatestFor(string card) => (_all.FirstOrDefault(r => r.Card == card) ?? _archived.FirstOrDefault(r => r.Card == card))?.File;

    /// <summary>Select a report by file, showing the list unfiltered if it was hidden.</summary>
    public void Show(string file)
    {
        Reload();
        UnreadOnly = false;
        ShowArchived = _archived.Any(r => r.File == file);
        KindIndex = 0;
        StoryIndex = 0;
        Selected = Reports.FirstOrDefault(r => r.Report.File == file);
    }

    /// <summary>Set by the project (WOR-0112): archive the QA reports of done cards and write any missing done report; says what it did.</summary>
    public Func<string>? CleanUpDone { get; set; }

    [RelayCommand]
    void CleanUp()
    {
        if (CleanUpDone is null) return;
        string said;
        try { said = CleanUpDone(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { said = $"Clean up stopped: {e.Message}"; }
        Reload();
        Status = said; // after the reload, which sets its own line: what Clean up did stays on screen
    }

    [RelayCommand]
    void Approve()
    {
        if (Selected is not { Pending: true } s) return;
        if (ApproveCard?.Invoke(s.Report.Card) is { } error) { Status = error; return; }
        Settle(s, DecisionReport.Approved);
    }

    /// <summary>The card was approved to done from its panel (WOR-0105): its newest unsettled report is marked approved, as Approve here would.</summary>
    public void ApprovedFromBoard(string card) => SettledFromBoard(card, DecisionReport.Approved, "");

    /// <summary>The card's decision was settled from its panel (WOR-0124): approved, sent back or answered. Its newest
    /// unsettled report says so, as Approve or Send back here would.</summary>
    public void SettledFromBoard(string card, string decision, string notes)
    {
        var r = _all.Where(x => x.Card == card).OrderByDescending(x => x.Date, StringComparer.Ordinal).FirstOrDefault();
        if (r is null || !(r.Decision == DecisionReport.Pending || (r.Qa && r.Decision.Length == 0))) return;
        try { DecisionReport.Settle(r.File, decision, notes); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Can't write the decision into {r.Id}: {e.Message}"; return; }
        Status = decision == DecisionReport.Approved ? $"{card} approved and moved to done." : decision == DecisionReport.SentBack ? $"{card} sent back." : $"{card} answered.";
        Reload();
    }

    [RelayCommand]
    void SendBack()
    {
        if (Selected is not { Pending: true } s) return;
        if (DecisionNotes.Trim().Length == 0) { Status = "Say what's wrong first: the agent works from your notes."; return; }
        if (SendCardBack?.Invoke(s.Report.Card, DecisionNotes) is { } error) { Status = error; return; }
        Settle(s, DecisionReport.SentBack);
    }

    void Settle(ReportRow row, string decision)
    {
        try { DecisionReport.Settle(row.Report.File, decision, DecisionNotes); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Can't write the decision into {row.Report.Id}: {e.Message}"; return; }
        DecisionNotes = "";
        Status = decision == DecisionReport.Approved ? $"{row.Report.Card} approved and moved to done." : $"{row.Report.Card} sent back.";
        Reload();
    }

    public ReportsViewModel(string folder)
    {
        Folder = folder;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        try
        {
            Directory.CreateDirectory(folder);
            _watch = new FileSystemWatcher(folder, "*.md") { EnableRaisingEvents = true };
            void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
            _watch.Changed += Poke; _watch.Created += Poke; _watch.Deleted += Poke; _watch.Renamed += (s, e) => Poke(s, e);
        }
        catch (Exception e) { Status = $"Can't watch {folder}: {e.Message}"; }
        Reload();
    }

    List<AgentReport> _all = [];

    public void Reload()
    {
        using var timing = StallWatch.Shared.Section("Reports.Reload");
        _all = ReportStore.List(Folder);
        _archived = ReportArchive.List(Folder);
        try { _map = new Dictionary<string, string>(StoryMap?.Invoke() ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase); } catch (IOException) { }
        OnPropertyChanged(nameof(ArchiveToggleText));
        if (ShowArchived && _archived.Count == 0) ShowArchived = false;
        Apply();
        Loaded?.Invoke(_all);
    }

    partial void OnUnreadOnlyChanged(bool value) => Apply();

    void Apply()
    {
        var keep = Selected?.Report.File;
        var source = ShowArchived ? _archived : _all;
        RelabelStories(source);
        RelabelKinds(source);
        OnPropertyChanged(nameof(CanArchive));
        var kind = KindShown;
        var story = StoryShown;
        var shown = source.Where(r => (kind is null || r.Kind == kind) && (story is null || StoryOf(r) == story) && (!UnreadOnly || !r.Read || r.File == keep)).ToList();
        Reports.Clear();
        foreach (var r in shown) Reports.Add(new ReportRow(r));
        Selected = Reports.FirstOrDefault(r => r.Report.File == keep);
        Count();
        HasReports = _all.Count > 0;
        // an action's message (Clean up, Archive, Approve, an error) stays a while: the files it wrote make the
        // folder watcher reload 400 ms later, and that reload put this line over it before it could be read (WOR-0131)
        if (DateTime.UtcNow < _heldUntil) return;
        _summary = true;
        Status = _all.Count == 0 ? $"No reports yet. Card agents file them in {Folder} when they finish."
            : $"{_all.Count} {(_all.Count == 1 ? "report" : "reports")}, {Unread} unread · {Folder}";
        _summary = false;
    }

    /// <summary>How long an action's message holds the status line against reloads.</summary>
    public static TimeSpan Hold { get; set; } = TimeSpan.FromSeconds(8);
    DateTime _heldUntil;
    bool _summary;
    partial void OnStatusChanged(string value) => _heldUntil = _summary ? default : DateTime.UtcNow + Hold;

    /// <summary>Put each kind's count in its label, keeping the pick (replacing an item can clear the selection).</summary>
    void RelabelKinds(List<AgentReport> source)
    {
        string[] labels = [$"{AllLabel} ({source.Count})", $"{FinishedLabel} ({source.Count(r => r.Kind == ReportKind.Finished)})",
            $"{InvestigationLabel} ({source.Count(r => r.Kind == ReportKind.Investigation)})", $"{QaLabel} ({source.Count(r => r.Kind == ReportKind.Qa)})"];
        var pick = KindIndex;
        _relabel = true;
        for (var i = 0; i < labels.Length; i++) if (Kinds[i] != labels[i]) Kinds[i] = labels[i];
        _relabel = false;
        if (KindIndex != pick) KindIndex = pick;
    }
    bool _relabel;

    /// <summary>The stories of the list shown, each with its count, keeping the pick by name.</summary>
    void RelabelStories(List<AgentReport> source)
    {
        var pick = StoryShown;
        var groups = source.GroupBy(StoryOf).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        _relabel = true;
        _storyNames = groups.Select(g => g.Key).ToList();
        Stories.Clear();
        Stories.Add($"{AllStories} ({source.Count})");
        foreach (var g in groups) Stories.Add($"{g.Key} ({g.Count()})");
        StoryIndex = pick is not null && _storyNames.IndexOf(pick) is var i and >= 0 ? i + 1 : 0;
        _relabel = false;
    }

    void Count() => Unread = _all.Count(r => !r.Read);

    /// <summary>Opening a report reads it.</summary>
    partial void OnSelectedChanged(ReportRow? value)
    {
        if (value is { Read: false }) SetRead(value, true);
    }

    void SetRead(ReportRow row, bool read)
    {
        try { ReportStore.SetRead(row.Report.File, read); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Can't mark {row.Report.Id}: {e.Message}"; return; }
        row.Read = read;
        // the file now says so too; keep the list's copy in step without reloading (the watcher will anyway)
        var i = _all.FindIndex(r => r.File == row.Report.File);
        if (i >= 0) _all[i] = _all[i] with { Read = read };
        Count();
    }

    [RelayCommand]
    void ToggleRead()
    {
        if (Selected is { } s) SetRead(s, !s.Read);
    }

    [RelayCommand]
    void MarkAllRead()
    {
        foreach (var r in Reports.Where(r => !r.Read).ToList()) SetRead(r, true);
    }

    [RelayCommand]
    void ShowCard()
    {
        if (Selected?.Report.Card is not { Length: > 0 } card) return;
        if (OpenCard?.Invoke(card) != true) Status = $"{card} isn't on the board shown now.";
    }

    /// <summary>A commit: on GitHub when the report's repo is there, else Status says so.</summary>
    [RelayCommand]
    void OpenCommit(string? sha)
    {
        if (string.IsNullOrEmpty(sha) || Selected is not { } s) return;
        var repo = s.Report.Repo;
        if (repo.Length == 0 || !Directory.Exists(repo)) { Status = $"The report doesn't say which repo {sha} is in."; return; }
        if (GitHub.RepoOf(repo) is not { } gh) { Status = $"{repo} isn't on GitHub; {sha} is in its history."; return; }
        Open($"https://github.com/{gh.Owner}/{gh.Name}/commit/{sha}");
    }

    [RelayCommand]
    void OpenFile()
    {
        if (Selected is { } s) Open(s.Report.File);
    }

    void Open(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Status = $"Can't open {target}: {e.Message}"; }
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _debounce.Stop();
    }
}

/// <summary>A report as listed: its heading, a line of date and card, whether it's been read, and its body.</summary>
public partial class ReportRow(AgentReport r) : ObservableObject
{
    public AgentReport Report { get; } = r;
    public string Heading => Report.Heading;
    public string Meta => string.Join(" · ", new[]
    {
        Report.Decision.Length > 0 ? "decision " + Report.Decision : Report.Cnr ? "could not reproduce" : Report.Kind switch { ReportKind.Qa => "QA · to check", ReportKind.Investigation => "investigation", _ => "" },
        Report.Date,
        Report.Commits.Count switch { 0 => "", 1 => "1 commit", var n => $"{n} commits" },
    }.Where(s => s.Length > 0));
    public string Body => Report.Body;
    /// <summary>The report's folder, where a picture named by a relative path is looked for.</summary>
    public string? Folder => System.IO.Path.GetDirectoryName(Report.File);
    public IReadOnlyList<string> Commits => Report.Commits;
    public bool HasCommits => Report.Commits.Count > 0;
    public bool HasCard => Report.Card.Length > 0;
    /// <summary>A decision report nobody has settled yet (WOR-0092): Approve and Send back show.</summary>
    // an agent's own QA report has no decision property yet, so any undecided QA report for a card can be settled here
    public bool Pending => Report.Decision == DecisionReport.Pending || (Report.Qa && Report.Card.Length > 0 && Report.Decision.Length == 0);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ReadLabel))] public partial bool Read { get; set; } = r.Read;
    public string ReadLabel => Read ? "Mark unread" : "Mark read";
}
