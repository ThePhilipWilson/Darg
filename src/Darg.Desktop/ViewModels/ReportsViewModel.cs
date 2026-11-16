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
        : "No reports yet. A card agent that is sure its work is done moves the card to done, writes a short report here (what was asked, what it did, its commits) and is forgotten; work that needs you to check it goes to the board's QA column instead.";
    /// <summary>Only the unread ones in the list.</summary>
    [ObservableProperty] public partial bool UnreadOnly { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";

    /// <summary>Raised with every report after each reload: the agents that wrote them can go.</summary>
    public event Action<IReadOnlyList<AgentReport>>? Loaded;
    /// <summary>Show a card on the board (set by the project); false: no such card there.</summary>
    public Func<string, bool>? OpenCard { get; set; }

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
        _all = ReportStore.List(Folder);
        Apply();
        Loaded?.Invoke(_all);
    }

    partial void OnUnreadOnlyChanged(bool value) => Apply();

    void Apply()
    {
        var keep = Selected?.Report.File;
        var shown = _all.Where(r => !UnreadOnly || !r.Read || r.File == keep).ToList();
        Reports.Clear();
        foreach (var r in shown) Reports.Add(new ReportRow(r));
        Selected = Reports.FirstOrDefault(r => r.Report.File == keep);
        Count();
        HasReports = _all.Count > 0;
        Status = _all.Count == 0 ? $"No reports yet. Card agents file them in {Folder} when they finish."
            : $"{_all.Count} {(_all.Count == 1 ? "report" : "reports")}, {Unread} unread · {Folder}";
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
        Report.Date,
        Report.Commits.Count switch { 0 => "", 1 => "1 commit", var n => $"{n} commits" },
    }.Where(s => s.Length > 0));
    public string Body => Report.Body;
    public IReadOnlyList<string> Commits => Report.Commits;
    public bool HasCommits => Report.Commits.Count > 0;
    public bool HasCard => Report.Card.Length > 0;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ReadLabel))] public partial bool Read { get; set; } = r.Read;
    public string ReadLabel => Read ? "Mark unread" : "Mark read";
}
