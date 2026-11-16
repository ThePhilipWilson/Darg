using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The one artifact server for this run of the app, and the way a transcript's "Open artifact"
/// reaches the project that shows it.</summary>
public static class ArtifactHost
{
    static ArtifactServer? _server;
    static bool _failed;

    /// <summary>Started on first use; null if it couldn't start (then pages open from disk, without send()).</summary>
    public static ArtifactServer? Server
    {
        get
        {
            if (_server is null && !_failed)
                try { _server = ArtifactServer.Start(); }
                catch (Exception) { _failed = true; }
            return _server;
        }
    }

    /// <summary>Raised with an artifact's file when a transcript asks to open it.</summary>
    public static event Action<string>? OpenRequested;
    public static void Open(string file) => OpenRequested?.Invoke(file);
}

/// <summary>A project's artifacts: the HTML pages its agents made, one folder per agent, shown as that
/// tree; the open one runs in the web view, served by <see cref="ArtifactHost"/> so it can send back.</summary>
public partial class ArtifactsViewModel : ObservableObject
{
    readonly string _project;
    readonly Func<string, string?> _agentName;
    readonly FileSystemWatcher? _watch;
    readonly DispatcherTimer _debounce;
    readonly HashSet<string> _closed = new(StringComparer.OrdinalIgnoreCase);

    public string Folder { get; }
    public ObservableCollection<ArtifactGroup> Groups { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasOpen), nameof(Address), nameof(Owner))] public partial ArtifactEntry? Selected { get; set; }
    [ObservableProperty] public partial object? SelectedNode { get; set; }
    /// <summary>What the web view shows (changed to reload).</summary>
    [ObservableProperty] public partial Uri? Page { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    public bool HasOpen => Selected is not null;
    public bool Empty => Groups.Count == 0;
    public string Address => Selected is null ? "" : Url(Selected.Artifact);
    public string Owner => Selected is null ? "" : _agentName(Selected.Artifact.Agent) is { } n ? $"sends go to {n}" : "its agent is gone: sends go nowhere";

    /// <param name="agentName">An agent's name from its id (null when it's no longer in the list).</param>
    public ArtifactsViewModel(string projectId, Func<string, string?> agentName)
    {
        _project = projectId;
        _agentName = agentName;
        Folder = ArtifactStore.Folder(projectId);
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); Refresh(); });
        Refresh();
        try
        {
            Directory.CreateDirectory(Folder);
            _watch = new FileSystemWatcher(Folder) { IncludeSubdirectories = true, EnableRaisingEvents = true };
            void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
            _watch.Changed += Poke; _watch.Created += Poke; _watch.Deleted += Poke; _watch.Renamed += (s, e) => Poke(s, e);
        }
        catch (Exception) { }
    }

    /// <summary>Read the folder again, keeping what's open and which agents' folders are closed.</summary>
    public void Refresh()
    {
        var keep = Selected?.Artifact.Path;
        var all = ArtifactStore.List(_project);
        Groups.Clear();
        foreach (var g in all.GroupBy(a => a.Agent))
        {
            var group = new ArtifactGroup(g.Key, _agentName(g.Key) ?? "an agent no longer listed", !_closed.Contains(g.Key), this);
            foreach (var a in g) group.Children.Add(new ArtifactEntry(a));
            Groups.Add(group);
        }
        OnPropertyChanged(nameof(Empty));
        var again = Groups.SelectMany(g => g.Children).FirstOrDefault(e => string.Equals(e.Artifact.Path, keep, StringComparison.OrdinalIgnoreCase));
        if (again is not null) { Selected = again; SelectedNode = again; }
        Status = all.Count == 0 ? "No artifacts yet. Agents write them here when they have something to show." : $"{all.Count} {(all.Count == 1 ? "artifact" : "artifacts")} in {Folder}";
    }

    internal void Expanded(string agent, bool open) { if (open) _closed.Remove(agent); else _closed.Add(agent); }

    partial void OnSelectedNodeChanged(object? value)
    {
        if (value is ArtifactEntry e && e != Selected) Selected = e;
    }

    partial void OnSelectedChanged(ArtifactEntry? value) => Page = value is null ? null : new Uri(Url(value.Artifact));

    /// <summary>Open the artifact in a file, if it's one of this project's.</summary>
    public bool Show(string file)
    {
        Refresh();
        var e = Groups.SelectMany(g => g.Children).FirstOrDefault(x => string.Equals(x.Artifact.Path, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        if (e is null) return false;
        Groups.First(g => g.Children.Contains(e)).IsExpanded = true;
        Selected = e;
        SelectedNode = e;
        return true;
    }

    /// <summary>A line under the page (a send that had nowhere to go, say).</summary>
    public void Tell(string text) => Status = text;

    static string Url(Artifact a) => ArtifactHost.Server?.Url(a) ?? new Uri(a.Path).AbsoluteUri;

    [RelayCommand]
    void Reload()
    {
        if (Selected is null) return;
        // a new address each time, so the web view loads it again rather than seeing no change
        Page = new Uri(Url(Selected.Artifact) + $"?r={DateTime.Now.Ticks}");
    }

    [RelayCommand]
    void OpenInBrowser()
    {
        if (Selected is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Address) { UseShellExecute = true }); } catch (Exception) { }
    }

    [RelayCommand]
    void ShowFile()
    {
        if (Selected is null) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{Selected.Artifact.Path}\""); } catch (Exception) { }
    }
}

/// <summary>One agent's artifacts (its folder), named for the agent.</summary>
public sealed partial class ArtifactGroup(string agent, string name, bool expanded, ArtifactsViewModel owner) : ObservableObject
{
    public string Agent { get; } = agent;
    public string Name { get; } = name;
    public ObservableCollection<ArtifactEntry> Children { get; } = [];
    public int Count => Children.Count;
    [ObservableProperty] public partial bool IsExpanded { get; set; } = expanded;
    partial void OnIsExpandedChanged(bool value) => owner.Expanded(Agent, value);
}

public sealed class ArtifactEntry(Artifact artifact)
{
    public Artifact Artifact { get; } = artifact;
    public string Title => Artifact.Title;
    public string When => Artifact.Modified.Date == DateTime.Today ? Artifact.Modified.ToString("HH:mm") : Artifact.Modified.ToString("d MMM");
    public IEnumerable<ArtifactEntry> Children => [];
    public bool IsExpanded { get; set; }
}
