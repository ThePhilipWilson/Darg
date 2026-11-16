using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A project's GitHub: per repo, open pull requests, open issues and recent CI runs, read with
/// the project's own gh account. Clicking anything opens it in the browser; an issue can become a card.</summary>
public partial class GitHubViewModel(ProjectConfig project, Func<string?> boardFolder) : ObservableObject
{
    bool _loaded;
    public ObservableCollection<HubRepoView> Repos { get; } = [];
    [ObservableProperty] public partial string Status { get; set; } = "";
    public string Account => project.GitHub is { Length: > 0 } a ? $"as {a}" : "as gh's active account";

    public async void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Status = "Asking GitHub…";
        var repos = project.Repos.Where(Directory.Exists).ToList();
        var found = await Task.Run(() => repos.Select(r => GitHub.Load(r, project.GitHub)).ToList());
        Repos.Clear();
        foreach (var r in found) Repos.Add(new HubRepoView(r, this));
        Status = repos.Count == 0 ? "This project has no repos set." : $"Read {DateTime.Now:HH:mm} {Account}";
    }

    [RelayCommand] void Refresh() { _loaded = false; Load(); }

    internal static void Browse(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    internal void Card(HubRepo repo, HubItem issue)
    {
        var board = boardFolder();
        if (board is null) { Status = "This project has no board to put a card on."; return; }
        var desc = (issue.Body.Trim().Length > 0 ? issue.Body.Trim() + "\n\n" : "") + $"From GitHub issue {repo.Full}#{issue.Number}: {issue.Url}";
        var card = BoardStore.Create(board, issue.Title, desc, issue.Labels, type: CardTypes.Guess(issue.Title, issue.Labels));
        Status = $"Made {card.Id} from #{issue.Number}";
    }
}

public sealed class HubRepoView
{
    public string Name { get; }
    public string Error { get; }
    public bool HasError => Error.Length > 0;
    public IReadOnlyList<HubRow> Pulls { get; }
    public IReadOnlyList<HubRow> Issues { get; }
    public IReadOnlyList<HubRow> Runs { get; }
    public string Counts { get; }

    public HubRepoView(HubRepo r, GitHubViewModel owner)
    {
        Name = r.Full.Length > 1 ? r.Full : r.Name;
        Error = r.Error;
        Pulls = r.Pulls.Select(p => new HubRow($"#{p.Number}", p.Title, $"{p.Author} · {Ago(p.Updated)}", "", p.Url, null)).ToList();
        Issues = r.Issues.Select(i => new HubRow($"#{i.Number}", i.Title, $"{i.Author} · {Ago(i.Updated)}" + (i.Labels.Count > 0 ? " · " + string.Join(", ", i.Labels) : ""),
            "", i.Url, new RelayCommand(() => owner.Card(r, i)))).ToList();
        Runs = r.Runs.Select(x => new HubRow(x.State, x.Name, $"{x.Branch} · {Ago(x.Updated)}", x.State, x.Url, null)).ToList();
        Counts = $"{Pulls.Count} pull requests · {Issues.Count} issues";
    }

    static string Ago(DateTime t)
    {
        if (t == DateTime.MinValue) return "";
        var d = DateTime.Now - t.ToLocalTime();
        return d.TotalMinutes < 60 ? $"{Math.Max(1, (int)d.TotalMinutes)}m ago" : d.TotalHours < 24 ? $"{(int)d.TotalHours}h ago"
            : d.TotalDays < 30 ? $"{(int)d.TotalDays}d ago" : t.ToLocalTime().ToString("d MMM yyyy");
    }
}

/// <summary>A pull request, issue or run: a tag (number or state), its title and a line under it.</summary>
public sealed class HubRow(string tag, string title, string meta, string state, string url, IRelayCommand? toCard)
{
    public string Tag { get; } = tag;
    public string Title { get; } = title;
    public string Meta { get; } = meta;
    public bool Good => state is "success";
    public bool Bad => state is "failure" or "timed_out" or "startup_failure";
    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => GitHubViewModel.Browse(url));
    public IRelayCommand? ToCardCommand { get; } = toCard;
    public bool CanCard => ToCardCommand is not null;
}
