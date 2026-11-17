using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A project's Home section (WOR-0054), where it opens: the project's README drawn with its pictures,
/// and above it what's going on: cards in progress, agents at work or waiting, the newest artifacts and the last
/// commits, each one a click away from where it lives.</summary>
public partial class HomeViewModel : ObservableObject
{
    readonly ProjectViewModel _project;
    readonly DispatcherTimer _tick;
    int _loading;

    public string Name => _project.Name;
    public Avalonia.Media.IBrush Brush => _project.Brush;
    /// <summary>The project's repos by name, else its working folder.</summary>
    public string Where { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasReadme), nameof(NoReadme), nameof(ReadmeName))] public partial string? ReadmePath { get; set; }
    [ObservableProperty] public partial string ReadmeText { get; set; } = "";
    public bool HasReadme => ReadmePath is not null;
    public bool NoReadme => ReadmePath is null && !Loading;
    /// <summary>Where the README is, shown over it: its repo and file name.</summary>
    public string ReadmeName => ReadmePath is null ? "" : $"{Path.GetFileName(Path.GetDirectoryName(ReadmePath))}/{Path.GetFileName(ReadmePath)}";
    public string? ReadmeFolder => ReadmePath is null ? null : Path.GetDirectoryName(ReadmePath);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NoReadme))] public partial bool Loading { get; set; } = true;
    public bool CanDraft => _project.Agents is not null && _project.Config.ExistingRepos().Count > 0;

    public bool ShowsCards => _project.Board is not null && _project.Config.Boards is not null;
    public ObservableCollection<HomeCard> Cards { get; } = [];
    [ObservableProperty] public partial string CardsTitle { get; set; } = "In progress";
    [ObservableProperty] public partial string CardsEmpty { get; set; } = "";

    public bool ShowsAgents => _project.Agents is not null;
    public ObservableCollection<AgentViewModel> Agents { get; } = [];
    [ObservableProperty] public partial string AgentsTitle { get; set; } = "Agents";
    [ObservableProperty] public partial string AgentsEmpty { get; set; } = "";

    public bool ShowsArtifacts => _project.Artifacts is not null;
    public ObservableCollection<HomeArtifact> Artifacts { get; } = [];
    [ObservableProperty] public partial string ArtifactsEmpty { get; set; } = "";

    public bool ShowsCommits => _project.Config.ExistingRepos().Any(Repo.IsGit);
    public ObservableCollection<HomeCommit> Commits { get; } = [];
    [ObservableProperty] public partial string CommitsEmpty { get; set; } = "";

    [ObservableProperty] public partial string Status { get; set; } = "";

    public HomeViewModel(ProjectViewModel project)
    {
        _project = project;
        var repos = project.Config.ExistingRepos();
        Where = repos.Count > 0 ? string.Join(" · ", repos) : project.Config.WorkingFolder();
        if (project.Board is not null) project.Board.Reloaded += () => { if (Shown) Glance(); };
        // agents change state on their own: while Home is on screen, look again every few seconds
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => { if (Shown) Glance(); });
        _tick.Start();
    }

    bool Shown => _project.IsActive && _project.OnHome;

    /// <summary>Read everything again: the README and commits off the UI thread, the rest from what the app holds.</summary>
    [RelayCommand]
    public async Task Load()
    {
        var mine = ++_loading;
        Glance();
        var config = _project.Config;
        var (readme, text, commits) = await Task.Run(() =>
        {
            var path = ProjectHome.Readme(config);
            string body = "";
            if (path is not null)
            {
                var (read, why) = Repo.Read(path);
                body = read ?? $"Couldn't show {Path.GetFileName(path)}: {why}";
            }
            var repos = config.ExistingRepos().Where(Repo.IsGit).ToList();
            var log = repos.SelectMany(r => Repo.Log(r, repos.Count > 1 ? 3 : 5).Select(c => (Repo: ProjectConfig.RepoName(r), Commit: c)))
                .OrderByDescending(x => x.Commit.When).ToList();
            return (path, body, log);
        });
        if (mine != _loading) return; // a newer load started meanwhile
        ReadmePath = readme;
        ReadmeText = text;
        OnPropertyChanged(nameof(ReadmeFolder));
        Loading = false;
        var now = DateTimeOffset.Now;
        var several = config.ExistingRepos().Count(Repo.IsGit) > 1;
        Commits.Clear();
        foreach (var (repo, c) in commits)
            Commits.Add(new HomeCommit(c.Subject, $"{(several ? repo + " · " : "")}{c.Hash} · {ProjectHome.Ago(c.When, now)} · {c.Author}"));
        CommitsEmpty = commits.Count == 0 ? "No commits yet." : "";
        Status = readme is null ? "" : $"README: {readme}";
    }

    /// <summary>The live part: cards, agents, artifacts. Cheap, so it runs on the timer while Home shows.</summary>
    void Glance()
    {
        if (_project.Board is { } board)
        {
            var cards = board.InProgress();
            var columns = cards.Select(c => c.Column).Distinct().ToList();
            Sync(Cards, cards.Select(c => new HomeCard(c.Card.Id, c.Card.Title, columns.Count > 1 ? c.Column : "")).ToList());
            CardsTitle = columns.Count == 1 ? columns[0] : "In progress";
            CardsEmpty = cards.Count == 0 ? (board.SelectedBoard is null ? "No board yet." : "Nothing in progress.") : "";
        }
        if (_project.Agents is { } agents)
        {
            // the ones to act on first, then the ones at work
            var live = agents.Agents.Where(a => a.NeedsYou || a.Working).OrderBy(a => a.NeedsYou ? 0 : 1).ToList();
            Sync(Agents, live);
            var needs = live.Count(a => a.NeedsYou);
            AgentsTitle = needs > 0 ? $"Agents · {needs} need{(needs == 1 ? "s" : "")} you" : "Agents";
            var rest = agents.Agents.Count - live.Count;
            AgentsEmpty = live.Count > 0 ? "" : rest == 0 ? "No agents yet." : $"None at work. {rest} finished or saved in Agents.";
        }
        if (_project.Artifacts is not null)
        {
            var now = DateTime.Now;
            var list = ArtifactStore.List(_project.Config.Id).Take(5)
                .Select(a => new HomeArtifact(a.Path, a.Title, ProjectHome.Ago(a.Modified, now))).ToList();
            Sync(Artifacts, list);
            ArtifactsEmpty = list.Count == 0 ? "No artifacts yet." : "";
        }
    }

    /// <summary>Make a list show these items, touching it only where it differs (no flicker on every tick).</summary>
    static void Sync<T>(ObservableCollection<T> shown, IReadOnlyList<T> items)
    {
        if (shown.SequenceEqual(items)) return;
        shown.Clear();
        foreach (var i in items) shown.Add(i);
    }

    [RelayCommand]
    void OpenCard(HomeCard card)
    {
        if (_project.Board?.OpenById(card.Id) == true) _project.OpenCommand.Execute(nameof(Section.Board));
    }

    [RelayCommand]
    void OpenAgent(AgentViewModel agent)
    {
        if (_project.Agents is null) return;
        _project.Agents.Selected = agent;
        _project.OpenCommand.Execute(nameof(Section.Agents));
    }

    [RelayCommand]
    void OpenArtifact(HomeArtifact artifact)
    {
        if (_project.Artifacts?.Show(artifact.Path) == true) _project.OpenCommand.Execute(nameof(Section.Artifacts));
    }

    /// <summary>Edit the README: in Docs, where it can be read, edited and saved like any doc; without Docs, in
    /// whatever edits markdown on this machine.</summary>
    [RelayCommand]
    void EditReadme()
    {
        if (ReadmePath is null) return;
        if (_project.Docs?.Show(ReadmePath) == true)
        {
            _project.OpenCommand.Execute(nameof(Section.Docs));
            _project.Docs.EditCommand.Execute(null);
            return;
        }
        Shell(ReadmePath);
    }

    /// <summary>No README: an agent writes a first one from the code, for Philip to judge rather than write.</summary>
    [RelayCommand]
    void DraftReadme()
    {
        if (_project.Agents is null || _project.Config.ExistingRepos().FirstOrDefault() is not { } repo) return;
        var others = _project.Config.ExistingRepos().Skip(1).ToList();
        var prompt = $"Write a README.md for the {Name} project at the top of {repo}." +
                     (others.Count > 0 ? $" The project also has {string.Join(", ", others)}; say what each is for." : "") +
                     " Read the code and any CLAUDE.md first. Say what the project is in a sentence or two, then how to build and run it and how it is laid out." +
                     " If the repo already has a screenshot or logo, show it near the top with a relative path. Keep it short and plain." +
                     " It is shown as the project's Home page in Darg.";
        // A card first, so the work shows on the board and the agent is a card agent that files its report.
        if (_project.Board?.StartNewCard($"README for {Name}", prompt, repo) != true)
            _project.Agents.Start(prompt, repo, name: $"README for {Name}");
        _project.OpenCommand.Execute(nameof(Section.Agents));
    }

    /// <summary>A link in the README: a web or mail link in the browser; a markdown file in Docs (as a doc
    /// outside the docs folder, if need be); any other file or folder in its own app.</summary>
    public void OpenLink(string link)
    {
        if (link.StartsWith("wiki:", StringComparison.Ordinal)) link = link[5..] + ".md";
        if (link.Contains("://", StringComparison.Ordinal) || link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) { Shell(link); return; }
        if (link.StartsWith('#')) return; // a heading on this page
        string full;
        try { full = Path.GetFullPath(Path.Combine(ReadmeFolder ?? _project.Config.WorkingFolder(), Uri.UnescapeDataString(link.Split('#')[0]).TrimStart('/', '\\'))); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { Status = $"Can't open {link}"; return; }
        if (!File.Exists(full) && !Directory.Exists(full)) { Status = $"Nothing at {full}"; return; }
        if (full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && _project.Docs?.Show(full) == true)
        {
            _project.OpenCommand.Execute(nameof(Section.Docs));
            return;
        }
        Shell(full);
    }

    void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Status = $"Couldn't open {target}: {e.Message}"; }
    }
}

public sealed record HomeCard(string Id, string Title, string Column);
public sealed record HomeArtifact(string Path, string Title, string When);
public sealed record HomeCommit(string Subject, string Meta);
