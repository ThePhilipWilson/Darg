using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

public enum HubTab { Overview, Actions, Pulls, Issues, Commits, Branches, Releases, Notifications }

/// <summary>A project's GitHub (WOR-0151), read with the project's own gh account, laid out as Reports is:
/// a tab per repo when there are several, then on the left a list per kind (CI runs, pull requests, issues,
/// commits, branches, releases, notifications) and on the right the picked one in full: a run's jobs, steps
/// and failed log lines, a pull request's checks, files and conversation, an issue's comments, a commit's
/// diff, a release's notes and assets. Overview lists what's worth a look across them, each opening in its
/// own tab, and says why the default branch is failing if it is. GitLab repos fill
/// the tabs they have. Issues, pull requests and failed runs can become cards.</summary>
public partial class GitHubViewModel(ProjectConfig project, Func<string?> boardFolder) : ObservableObject
{
    bool _loaded;
    int _picking;
    readonly Dictionary<string, HubDetail> _details = [];

    public ObservableCollection<HubRepoView> Repos { get; } = [];
    [ObservableProperty] public partial HubRepoView? Repo { get; set; }
    public bool ManyRepos => Repos.Count > 1;

    /// <summary>The tab labels, with counts, for the repo showing.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> Tabs { get; set; } = HubRepoView.Names;
    [ObservableProperty] public partial int TabIndex { get; set; }
    public HubTab Tab => (HubTab)Math.Max(0, TabIndex);

    public ObservableCollection<HubRow> Rows { get; } = [];
    [ObservableProperty] public partial HubRow? Selected { get; set; }
    [ObservableProperty] public partial HubDetailView? Detail { get; set; }
    [ObservableProperty] public partial string DetailStatus { get; set; } = "";
    public bool HasSelected => Selected is not null;
    public bool ShowSummary => Selected is null && Repo is not null;
    public bool CanCard => Selected?.Kind is HubKind.Issue or HubKind.Pull || Selected is { Kind: HubKind.Run, Bad: true }
                           || Selected?.Target?.Kind is HubKind.Issue or HubKind.Pull;
    public bool CanMarkRead => Selected is { Kind: HubKind.Notice, Unread: true };

    [ObservableProperty] public partial string Status { get; set; } = "";
    public string Account => project.GitHub is { Length: > 0 } a ? $"as {a}" : "as gh's active account";

    public async void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Status = "Asking GitHub…";
        var repos = project.Repos.Where(Directory.Exists).ToList();
        var found = await Task.Run(() => repos.Select(r => GitHub.Load(r, project.GitHub)).ToList());
        var was = Repo?.Name;
        var tab = TabIndex;
        _details.Clear();
        Repos.Clear();
        foreach (var r in found) Repos.Add(new HubRepoView(r));
        OnPropertyChanged(nameof(ManyRepos));
        Repo = Repos.FirstOrDefault(r => r.Name == was) ?? Repos.FirstOrDefault();
        TabIndex = tab;
        Status = repos.Count == 0 ? "This project has no repos set." : $"Read {DateTime.Now:HH:mm} {Account}";
    }

    [RelayCommand] void Refresh() { _loaded = false; Load(); }

    partial void OnRepoChanged(HubRepoView? value)
    {
        Relabel();
        ShowRows();
        OnPropertyChanged(nameof(ShowSummary));
        _ = ReadMainFailure(value);
    }

    /// <summary>The default branch's latest run, when it failed: the summary says why without a click.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMainFailed), nameof(MainFailedTitle))] public partial HubRow? MainFailed { get; set; }
    [ObservableProperty] public partial string MainWhy { get; set; } = "";
    public bool HasMainFailed => MainFailed is not null;
    public string MainFailedTitle => MainFailed is { } m ? $"✗ {Repo?.Repo.DefaultBranch} is failing: {m.Title}" : "";

    async Task ReadMainFailure(HubRepoView? repo)
    {
        MainFailed = repo?.Main is { Bad: true } m && !repo.Repo.IsGitLab ? m : null;
        MainWhy = "";
        if (MainFailed is not { } row || repo is null) return;
        MainWhy = "Reading why…";
        var detail = await DetailFor(row, repo);
        if (Repo != repo) return;
        MainWhy = detail.Error.Length > 0 ? detail.Error : detail.Excerpt.Length > 0 ? detail.Excerpt : "GitHub says no more than that it failed.";
    }

    /// <summary>The picked row's tab with it picked there: a row on Overview opens in its own tab.</summary>
    [RelayCommand]
    void OpenInTab(HubRow? row)
    {
        if (row is null) return;
        ShowTab(TabFor(row.Kind));
        Selected = Rows.FirstOrDefault(r => r.Key == row.Key);
    }

    [RelayCommand] void OpenMainRun() => OpenInTab(MainFailed);

    static HubTab TabFor(HubKind kind) => kind switch
    {
        HubKind.Run => HubTab.Actions,
        HubKind.Pull => HubTab.Pulls,
        HubKind.Issue => HubTab.Issues,
        HubKind.Commit => HubTab.Commits,
        HubKind.Branch => HubTab.Branches,
        HubKind.Release => HubTab.Releases,
        _ => HubTab.Notifications,
    };

    bool _relabeling;

    /// <summary>New tab labels, keeping the picked tab and row: a new list (the TabStrip drops its pick while it
    /// swaps them, so that is put back), since a label changed in place was read by UI Automation but not redrawn.</summary>
    void Relabel()
    {
        var tab = TabIndex;
        _relabeling = true;
        try
        {
            Tabs = (IReadOnlyList<string>?)Repo?.TabLabels() ?? HubRepoView.Names;
            TabIndex = tab;
        }
        finally { _relabeling = false; }
    }

    partial void OnTabIndexChanged(int value) { if (value >= 0 && !_relabeling) ShowRows(); }

    void ShowRows()
    {
        Selected = null;
        Rows.Clear();
        if (Repo is null) return;
        foreach (var r in Repo.RowsFor(Tab)) Rows.Add(r);
    }

    /// <summary>Show a tab by name (for the API and tests).</summary>
    public void ShowTab(HubTab tab) => TabIndex = (int)tab;

    partial void OnSelectedChanged(HubRow? value)
    {
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(CanCard));
        OnPropertyChanged(nameof(CanMarkRead));
        Detail = null;
        DetailStatus = "";
        if (value is null || Repo is null) return;
        if (Tab == HubTab.Overview)
        {
            // after the list's own selection change is done, since this swaps the list under it
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenInTab(value));
            return;
        }
        _ = Pick(value, Repo);
    }

    async Task Pick(HubRow row, HubRepoView repo)
    {
        var picking = ++_picking;
        var target = row.Target ?? row;
        if (row.Kind == HubKind.Notice && row.Target is null)
        {
            Detail = new HubDetailView(new HubDetail(row.Title, row.Meta, repo.Repo.Url, [new HubPart("", HubPartKind.Markdown,
                "This notification is about something the page doesn't list. Open ↗ shows it on GitHub.")]));
            return;
        }
        DetailStatus = "Asking GitHub…";
        var detail = await DetailFor(target, repo);
        if (picking != _picking) return; // another row was picked meanwhile
        DetailStatus = detail.Error;
        Detail = new HubDetailView(detail);
    }

    /// <summary>A row's detail, read from GitHub once and kept until Refresh.</summary>
    async Task<HubDetail> DetailFor(HubRow target, HubRepoView repo)
    {
        var key = $"{repo.Name}|{target.Key}";
        if (_details.TryGetValue(key, out var detail)) return detail;
        var account = project.GitHub;
        detail = await Task.Run(() => Read(repo.Repo, account, target));
        if (detail.Error.Length == 0) _details[key] = detail;
        return detail;
    }

    static HubDetail Read(HubRepo repo, string? account, HubRow row) => row.Item switch
    {
        HubItem i when repo.IsGitLab => GitHubDetail.Plain(i),
        HubItem i when row.Kind == HubKind.Pull => GitHubDetail.Pull(repo, account, i.Number),
        HubItem i => GitHubDetail.Issue(repo, account, i.Number),
        HubRun r when repo.IsGitLab => new HubDetail(r.Name, $"{r.Branch} · {r.State}", r.Url, []),
        HubRun r => GitHubDetail.Run(repo, account, r),
        HubCommit c => GitHubDetail.Commit(repo, account, c.Sha),
        HubBranch { Tip: { } t } => GitHubDetail.Commit(repo, account, t.Sha),
        HubRelease r => GitHubDetail.Release(r),
        _ => new HubDetail(row.Title, row.Meta, "", []),
    };

    [RelayCommand]
    void Open()
    {
        if (Detail?.Url is { Length: > 0 } u) Browse(u);
        else if (Selected?.Url is { Length: > 0 } s) Browse(s);
    }

    [RelayCommand] void OpenRepo() { if (Repo?.Repo.Url is { Length: > 0 } u) Browse(u); }

    internal static void Browse(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>The picked issue, pull request or failed run as a card on the project's inbox board; a run's
    /// card carries the lines of its log that say why it failed.</summary>
    [RelayCommand]
    void ToCard()
    {
        var row = Selected?.Target ?? Selected;
        if (row is null || Repo is null) return;
        var board = boardFolder();
        if (board is null) { Status = "This project has no board to put a card on."; return; }
        var repo = Repo.Repo;
        string title, desc;
        IReadOnlyList<string> labels;
        switch (row.Item)
        {
            case HubItem i:
                title = i.Title;
                labels = i.Labels;
                var what = row.Kind == HubKind.Pull ? "pull request" : "issue";
                var body = GitHubDetail.Readable(i.Body);
                desc = (body.Length > 0 ? body + "\n\n" : "") + $"From GitHub {what} {repo.Full}#{i.Number}: {i.Url}";
                break;
            case HubRun r:
                title = $"CI failed: {r.Title}";
                labels = ["ci"];
                var excerpt = Detail?.Source.Excerpt is { Length: > 0 } e && Detail.Url == r.Url ? e + "\n\n" : "";
                desc = $"{r.Name} run {r.Number} on {r.Branch} ({(r.Sha.Length > 7 ? r.Sha[..7] : r.Sha)}) failed.\n\n{excerpt}Run: {r.Url}";
                break;
            default: return;
        }
        var card = BoardStore.Create(board, title, desc, labels, type: CardTypes.Guess(title, labels));
        Status = $"Made {card.Id} from {row.Tag}";
    }

    [RelayCommand]
    async Task MarkRead()
    {
        if (Selected is not { Kind: HubKind.Notice, Item: HubNotice n } row || Repo is null) return;
        var account = project.GitHub;
        var error = await Task.Run(() => GitHub.MarkRead(account, n.Id));
        if (error is not null) { Status = error; return; }
        Repo.MarkRead(n.Id);
        row.Unread = false;
        OnPropertyChanged(nameof(CanMarkRead));
        Relabel();
        Status = "Marked read";
    }
}

public enum HubKind { Run, Pull, Issue, Commit, Branch, Release, Notice }

/// <summary>A repo's lists as rows, built once per read.</summary>
public sealed class HubRepoView
{
    public HubRepo Repo { get; }
    public string Name { get; }
    public string Error => Repo.Error;
    public bool HasError => Error.Length > 0;
    public string About { get; }
    public string Counts { get; }
    public bool HasDescription => Repo.Description.Length > 0;
    readonly Dictionary<HubTab, List<HubRow>> _rows = [];

    public HubRepoView(HubRepo r)
    {
        Repo = r;
        Name = r.Full.Length > 1 ? r.Full : r.Name;
        var src = r.IsGitLab ? "GitLab" : "GitHub";
        About = string.Join(" · ", new[] { src, r.IsGitLab ? "" : r.Private ? "private" : "public",
            r.DefaultBranch.Length > 0 ? $"default branch {r.DefaultBranch}" : "" }.Where(s => s.Length > 0));
        Counts = $"{Many(r.PullCount, "open pull request")} · {Many(r.IssueCount, "open issue")} · {Many(r.BranchCount, "branch", "branches")} · {Many(r.ReleaseCount, "release")}";

        var runs = r.Runs.Select(Row).ToList();
        var pulls = r.Pulls.Select(p => PullRow(p)).ToList();
        var issues = r.Issues.Select(IssueRow).ToList();
        var commits = r.Commits.Select(CommitRow).ToList();
        _rows[HubTab.Actions] = runs;
        _rows[HubTab.Pulls] = pulls;
        _rows[HubTab.Issues] = issues;
        _rows[HubTab.Commits] = commits;
        _rows[HubTab.Branches] = r.Branches.Select(BranchRow).ToList();
        _rows[HubTab.Releases] = r.Releases.Select(ReleaseRow).ToList();
        _rows[HubTab.Notifications] = r.Notices.Select(n => NoticeRow(n, runs, pulls, issues)).ToList();

        // what's worth a look: the default branch's CI, unread notifications, open work, the newest commit and release
        var overview = new List<HubRow>();
        var main = runs.FirstOrDefault(x => ((HubRun)x.Item).Branch == r.DefaultBranch) ?? runs.FirstOrDefault();
        Main = main;
        if (main is not null) overview.Add(main.As("CI"));
        overview.AddRange(runs.Where(x => x.Bad && x != main).Take(2).Select(x => x.As("CI")));
        overview.AddRange(_rows[HubTab.Notifications].Where(n => n.Unread).DistinctBy(n => n.Title).Take(5).Select(n => n.As("Notice")));
        overview.AddRange(pulls.Take(5).Select(p => p.As("PR")));
        overview.AddRange(issues.Take(5).Select(i => i.As("Issue")));
        if (commits.FirstOrDefault() is { } c) overview.Add(c.As("Commit"));
        if (_rows[HubTab.Releases].FirstOrDefault() is { } rel) overview.Add(rel.As("Release"));
        _rows[HubTab.Overview] = overview;
    }

    /// <summary>The default branch's latest run (else the latest of any).</summary>
    public HubRow? Main { get; }

    public IReadOnlyList<HubRow> RowsFor(HubTab tab) => _rows.TryGetValue(tab, out var rows) ? rows : [];

    public List<string> TabLabels()
    {
        var failing = Repo.Runs.FirstOrDefault(x => x.Branch == Repo.DefaultBranch) is { } m && Bad(m.State);
        var unread = Repo.Notices.Count(n => n.Unread && !_read.Contains(n.Id));
        return ["Overview", failing ? "Actions ✗" : "Actions", Count("Pull requests", Repo.PullCount), Count("Issues", Repo.IssueCount),
                "Commits", Count("Branches", Repo.BranchCount), Count("Releases", Repo.ReleaseCount),
                unread > 0 ? $"Notifications {unread}{(Repo.Notices.Count >= 50 ? "+" : "")}" : "Notifications"]; // GitHub sends 50 at most
    }

    readonly HashSet<string> _read = [];
    public void MarkRead(string id) => _read.Add(id);

    /// <summary>The tabs' names, in <see cref="HubTab"/>'s order.</summary>
    public static readonly string[] Names = ["Overview", "Actions", "Pull requests", "Issues", "Commits", "Branches", "Releases", "Notifications"];

    static string Many(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    static string Count(string name, int n) => n > 0 ? $"{name} {n}" : name;
    internal static bool Bad(string state) => state is "failure" or "timed_out" or "startup_failure" or "error";

    static HubRow Row(HubRun x) => new(HubKind.Run, $"run:{x.Id}:{x.Url}", GitHubDetail.Mark(x.State), x.Title.Length > 0 ? x.Title : x.Name,
        Join(x.Name, x.Branch, x.Event, x.Number > 0 ? $"#{x.Number}" : "", x.Took > TimeSpan.Zero ? GitHubDetail.Duration(x.Took) : "", Ago(x.Updated)),
        x.State, x.Url, x);

    static HubRow PullRow(HubItem p) => new(HubKind.Pull, $"pull:{p.Number}", $"#{p.Number}", (p.Draft ? "[draft] " : "") + p.Title,
        Join(p.Author, p.Head.Length > 0 ? $"{p.Head} → {p.Base}" : "", p.Additions + p.Deletions > 0 ? $"+{p.Additions} −{p.Deletions}" : "",
            p.Checks.Length > 0 ? $"checks {GitHubDetail.Mark(p.Checks)}" : "", p.Review.ToLowerInvariant().Replace('_', ' '),
            p.Comments > 0 ? Many(p.Comments, "comment") : "", Ago(p.Updated)),
        p.Checks.ToLowerInvariant(), p.Url, p);

    static HubRow IssueRow(HubItem i) => new(HubKind.Issue, $"issue:{i.Number}", $"#{i.Number}", i.Title,
        Join(i.Author, string.Join(", ", i.Labels), i.Assignees.Count > 0 ? "→ " + string.Join(", ", i.Assignees) : "",
            i.Comments > 0 ? Many(i.Comments, "comment") : "", Ago(i.Updated)), "", i.Url, i);

    static HubRow CommitRow(HubCommit c) => new(HubKind.Commit, $"commit:{c.Sha}", c.Short, c.Headline,
        Join(c.Author, Ago(c.Date), c.Checks.Length > 0 ? $"checks {GitHubDetail.Mark(c.Checks)}" : ""), c.Checks.ToLowerInvariant(), c.Url, c);

    static HubRow BranchRow(HubBranch b) => new(HubKind.Branch, $"commit:{b.Tip?.Sha}", b.Tip is { Checks.Length: > 0 } t ? GitHubDetail.Mark(t.Checks) : "⎇",
        b.Name + (b.Default ? " (default)" : ""), Join(b.Tip?.Headline ?? "", b.Tip?.Author ?? "", Ago(b.Tip?.Date ?? DateTime.MinValue), b.Pull > 0 ? $"PR #{b.Pull}" : ""),
        b.Tip?.Checks.ToLowerInvariant() ?? "", b.Tip?.Url ?? "", b);

    static HubRow ReleaseRow(HubRelease x) => new(HubKind.Release, $"release:{x.Tag}", x.Tag, x.Name.Length > 0 ? x.Name : x.Tag,
        Join(x.Draft ? "draft" : x.Prerelease ? "pre-release" : "", x.Author, Ago(x.Date), x.Assets.Count > 0 ? Many(x.Assets.Count, "asset") : ""), "", x.Url, x);

    /// <summary>A notification, pointing at the row it's about: its pull request or issue, or for a CI one
    /// ("CI workflow run failed for main branch") the newest run of that workflow.</summary>
    static HubRow NoticeRow(HubNotice n, List<HubRow> runs, List<HubRow> pulls, List<HubRow> issues)
    {
        HubRow? target = n.Type switch
        {
            "PullRequest" => pulls.FirstOrDefault(p => ((HubItem)p.Item).Number == n.Number) ?? (n.Number > 0 ? PullRow(new HubItem(n.Number, n.Title, "", "", n.Updated, [], "")) : null),
            "Issue" => issues.FirstOrDefault(p => ((HubItem)p.Item).Number == n.Number) ?? (n.Number > 0 ? IssueRow(new HubItem(n.Number, n.Title, "", "", n.Updated, [], "")) : null),
            "CheckSuite" => System.Text.RegularExpressions.Regex.Match(n.Title, @"^(.+?) workflow run") is { Success: true } m
                ? runs.FirstOrDefault(r => ((HubRun)r.Item).Name == m.Groups[1].Value && ((HubRun)r.Item).Updated <= n.Updated.AddMinutes(5)) : null,
            _ => null,
        };
        var kind = n.Type switch { "CheckSuite" => "CI", "PullRequest" => "PR", _ => n.Type };
        return new HubRow(HubKind.Notice, $"notice:{n.Id}", n.Unread ? "●" : "", n.Title, Join(kind, n.Reason.Replace('_', ' '), Ago(n.Updated)),
            "", target?.Url ?? "", n) { Unread = n.Unread, Target = target };
    }

    static string Join(params string[] bits) => string.Join(" · ", bits.Where(b => b.Length > 0));

    internal static string Ago(DateTime t)
    {
        if (t == DateTime.MinValue) return "";
        var d = DateTime.Now - t.ToLocalTime();
        return d.TotalMinutes < 60 ? $"{Math.Max(1, (int)d.TotalMinutes)}m ago" : d.TotalHours < 24 ? $"{(int)d.TotalHours}h ago"
            : d.TotalDays < 30 ? $"{(int)d.TotalDays}d ago" : t.ToLocalTime().ToString("d MMM yyyy");
    }
}

/// <summary>A row of a list: a tag (state mark, number, sha), its title and a line under it. <see cref="Item"/>
/// is what it shows (a HubRun, HubItem…); a notification's <see cref="Target"/> is the row it's about.</summary>
public sealed partial class HubRow(HubKind kind, string key, string tag, string title, string meta, string state, string url, object item) : ObservableObject
{
    public HubKind Kind { get; } = kind;
    public string Key { get; } = key;
    public string Tag { get; } = tag;
    public string Title { get; } = title;
    public string Meta { get; } = meta;
    public string State { get; } = state;
    public string Url { get; } = url;
    public object Item { get; } = item;
    public HubRow? Target { get; init; }
    [ObservableProperty] public partial bool Unread { get; set; }
    /// <summary>On Overview, what kind of thing the row is (CI, PR, Issue…).</summary>
    public string Group { get; private init; } = "";
    public bool HasGroup => Group.Length > 0;
    public bool Good => State is "success";
    public bool Bad => HubRepoView.Bad(State);
    public bool Running => State is "in_progress" or "pending" or "queued";

    public HubRow As(string group) => new(Kind, Key, Tag, Title, Meta, State, Url, Item) { Target = Target, Unread = Unread, Group = group };
}

public sealed class HubDetailView(HubDetail d)
{
    public HubDetail Source { get; } = d;
    public string Title { get; } = d.Title;
    public string Sub { get; } = d.Sub;
    public string Url { get; } = d.Url;
    public IReadOnlyList<HubPartView> Parts { get; } = d.Parts.Select(p => new HubPartView(p)).ToList();
}

public sealed partial class HubPartView : ObservableObject
{
    readonly HubPart _part;
    public HubPartView(HubPart p)
    {
        _part = p;
        Lines = (p.Lines ?? []).Select(l => new HubLineView(l)).ToList();
        Diff = p.Kind == HubPartKind.Diff ? p.Text.Split('\n').Select(l => new DiffLine(l)).ToList() : [];
    }
    public string Heading => _part.Heading;
    public bool HasHeading => Heading.Length > 0;
    public bool IsMarkdown => _part.Kind == HubPartKind.Markdown;
    public bool IsLines => _part.Kind == HubPartKind.Lines;
    public bool IsLog => _part.Kind == HubPartKind.Log;
    public bool IsDiff => _part.Kind == HubPartKind.Diff;
    public string Text => _part.Text;
    public IReadOnlyList<HubLineView> Lines { get; }
    public IReadOnlyList<DiffLine> Diff { get; }

    /// <summary>A log shows the lines that matter until asked for the whole of it.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LogText), nameof(LogToggle))] public partial bool WholeLog { get; set; }
    public string LogText => WholeLog && _part.Full.Length > 0 ? _part.Full : _part.Text;
    public string LogToggle => WholeLog ? "Just the lines that matter" : $"Whole log ({_part.Full.Split('\n').Length} lines)";
    public bool HasFull => _part.Full.Length > 0 && _part.Full != _part.Text;
    [RelayCommand] void ToggleLog() => WholeLog = !WholeLog;
}

public sealed class HubLineView(HubLine l)
{
    public string Tag { get; } = l.Tag;
    public string Text { get; } = l.Text;
    public string Meta { get; } = l.Meta;
    public bool Good => l.State is "success";
    public bool Bad => HubRepoView.Bad(l.State) || l.State is "changes_requested";
    public bool HasUrl => l.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => GitHubViewModel.Browse(l.Url));
}

public sealed class DiffLine(string text)
{
    public string Text { get; } = text;
    public bool Add => text.StartsWith('+');
    public bool Del => text.StartsWith('-');
    public bool Hunk => text.StartsWith("@@") || text.StartsWith("── ");
}
