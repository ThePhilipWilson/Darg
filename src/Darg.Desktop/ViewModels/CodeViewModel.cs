using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A project's repos to read: each a tree of git's file list (built a folder at a time as it
/// is opened), a finder across all of them, changed files marked, and open files as tabs. Loaded the
/// first time the section is shown, off the UI thread, so a big repo doesn't hold the app up.</summary>
public partial class CodeViewModel(ProjectConfig project) : ObservableObject
{
    bool _loaded;
    readonly List<(RepoTree Repo, string Path)> _all = [];

    public ObservableCollection<TreeNode> Roots { get; } = [];
    public ObservableCollection<FileHit> Hits { get; } = [];
    public ObservableCollection<OpenFile> Tabs { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Finding))] public partial string Find { get; set; } = "";
    [ObservableProperty] public partial bool ChangedOnly { get; set; }
    [ObservableProperty] public partial OpenFile? Current { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    public bool Finding => Find.Trim().Length > 0 || ChangedOnly;

    public async void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Status = "Reading the repos…";
        var repos = project.Repos.Where(Directory.Exists).ToList();
        var built = await Task.Run(() => repos.Select(r => new RepoTree(r, Repo.Files(r), Repo.Status(r))).ToList());
        foreach (var r in built)
        {
            Roots.Add(r.Root);
            _all.AddRange(r.Files.Select(f => (r, f)));
        }
        Status = repos.Count == 0 ? "This project has no repos set." : string.Join(" · ", built.Select(r => r.Summary));
    }

    public void Refresh()
    {
        _loaded = false;
        Roots.Clear(); _all.Clear(); Hits.Clear();
        Load();
    }

    partial void OnFindChanged(string value) => Search();
    partial void OnChangedOnlyChanged(bool value) { OnPropertyChanged(nameof(Finding)); Search(); }

    /// <summary>The finder: every word must appear in the path; shorter paths and name matches first.</summary>
    void Search()
    {
        Hits.Clear();
        var words = Find.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = _all.Where(x => (!ChangedOnly || x.Repo.Status.Changed.ContainsKey(x.Path))
                                    && words.All(w => x.Path.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => words.Length > 0 && Path.GetFileName(x.Path).Contains(words[^1], StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Path.Length).Take(300);
        foreach (var (repo, path) in found) Hits.Add(new FileHit(this, repo, path));
    }

    public void Open(RepoTree repo, string path)
    {
        var existing = Tabs.FirstOrDefault(t => t.Repo == repo && t.Path == path);
        if (existing is null)
        {
            existing = new OpenFile(this, repo, path);
            Tabs.Add(existing);
        }
        Current = existing;
    }

    OpenFile? _warned;

    public void Close(OpenFile f)
    {
        if (f.Dirty && _warned != f)
        {
            _warned = f;
            Status = $"{f.Name} has unsaved changes: Ctrl+S to save, or × again to drop them.";
            Current = f;
            return;
        }
        _warned = null;
        var i = Tabs.IndexOf(f);
        Tabs.Remove(f);
        if (Current == f) Current = Tabs.Count == 0 ? null : Tabs[Math.Min(i, Tabs.Count - 1)];
    }

    [RelayCommand] void Reload() => Refresh();
}

/// <summary>One repo: its files, its status, and its tree's root.</summary>
public sealed class RepoTree
{
    public string Path { get; }
    public List<string> Files { get; }
    public RepoStatus Status { get; }
    public TreeNode Root { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('/', '\\'));
    public string Summary => Status.Branch.Length == 0 ? Name
        : $"{Name} on {Status.Branch}" + (Status.Changed.Count > 0 ? $", {Status.Changed.Count} changed" : "")
          + (Status.Ahead > 0 ? $", {Status.Ahead} to push" : "") + (Status.Behind > 0 ? $", {Status.Behind} behind" : "");
    readonly Dictionary<string, (SortedSet<string> Dirs, List<string> Files)> _byDir = new(StringComparer.Ordinal);
    internal CodeViewModel? Owner { get; set; }

    public RepoTree(string path, List<string> files, RepoStatus status)
    {
        Path = path; Files = files; Status = status;
        foreach (var f in files)
        {
            var slash = f.LastIndexOf('/');
            Entry(slash < 0 ? "" : f[..slash]).Files.Add(f);
            // register every folder on the way down with its parent
            for (var s = f.IndexOf('/'); s >= 0; s = f.IndexOf('/', s + 1))
            {
                var dir = f[..s];
                var up = dir.LastIndexOf('/');
                Entry(up < 0 ? "" : dir[..up]).Dirs.Add(dir);
            }
        }
        Root = new TreeNode(this, "", Summary, isDir: true) { IsExpanded = true };
    }

    (SortedSet<string> Dirs, List<string> Files) Entry(string dir)
    {
        if (!_byDir.TryGetValue(dir, out var e)) _byDir[dir] = e = (new SortedSet<string>(StringComparer.OrdinalIgnoreCase), []);
        return e;
    }

    public IEnumerable<TreeNode> Children(string dir)
    {
        if (!_byDir.TryGetValue(dir, out var e)) yield break;
        foreach (var d in e.Dirs) yield return new TreeNode(this, d, d[(d.LastIndexOf('/') + 1)..], isDir: true);
        foreach (var f in e.Files.Order(StringComparer.OrdinalIgnoreCase)) yield return new TreeNode(this, f, f[(f.LastIndexOf('/') + 1)..], isDir: false);
    }

    /// <summary>Whether a folder holds a changed file (its tree row is marked).</summary>
    public bool HasChanges(string dir) => Status.Changed.Keys.Any(k => dir.Length == 0 || k.StartsWith(dir + "/", StringComparison.Ordinal));
}

/// <summary>A row in a repo's tree: its children are made the first time it is expanded.</summary>
public sealed partial class TreeNode : ObservableObject
{
    static readonly TreeNode Placeholder = new(null, "", "…", false);
    readonly RepoTree? _repo;
    bool _filled;
    public string Path { get; }
    public string Name { get; }
    public bool IsDir { get; }
    public string Change { get; }
    public bool Changed => Change.Length > 0;
    public ObservableCollection<TreeNode> Children { get; } = [];
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    public TreeNode(RepoTree? repo, string path, string name, bool isDir)
    {
        _repo = repo; Path = path; Name = name; IsDir = isDir;
        Change = repo is null ? "" : isDir ? (repo.HasChanges(path) && path.Length > 0 ? "•" : "")
            : repo.Status.Changed.TryGetValue(path, out var c) ? c.Trim() : "";
        if (isDir && repo is not null) Children.Add(Placeholder);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _filled || _repo is null) return;
        _filled = true;
        Children.Clear();
        foreach (var c in _repo.Children(Path)) Children.Add(c);
    }

    public void Activate(CodeViewModel code)
    {
        if (_repo is null) return;
        if (IsDir) IsExpanded = !IsExpanded; else code.Open(_repo, Path);
    }
}

public sealed class FileHit(CodeViewModel code, RepoTree repo, string path)
{
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public string Where { get; } = $"{repo.Name}/{path}";
    public string Change { get; } = repo.Status.Changed.TryGetValue(path, out var c) ? c.Trim() : "";
    public IRelayCommand Command { get; } = new RelayCommand(() => code.Open(repo, path));
}

/// <summary>An open file: its text (or an image, or why it can't be shown).</summary>
public sealed partial class OpenFile : ObservableObject
{
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    public RepoTree Repo { get; }
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string FullPath => System.IO.Path.Combine(Repo.Path, Path);
    public string Extension => System.IO.Path.GetExtension(Path);
    public string? Text { get; }
    public Bitmap? Image { get; }
    public string Why { get; } = "";
    public bool IsText => Text is not null;
    public bool IsImage => Image is not null;
    public string Change => Repo.Status.Changed.TryGetValue(Path, out var c) ? c.Trim() : "";
    public IRelayCommand CloseCommand { get; }
    /// <summary>The text as edited and not yet saved (null: as on disk).</summary>
    public string? Edited { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] public partial bool Dirty { get; set; }
    [ObservableProperty] public partial string? Saved { get; set; }
    public string Title => Dirty ? Name + " ●" : Name;

    /// <summary>Write the edits to the file. Returns why not, or null when saved.</summary>
    public string? Save()
    {
        if (!Dirty || Edited is null) return null;
        try
        {
            File.WriteAllText(FullPath, Edited, new System.Text.UTF8Encoding(false));
            Dirty = false;
            Saved = $"Saved {Path} at {DateTime.Now:HH:mm:ss}";
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public OpenFile(CodeViewModel code, RepoTree repo, string path)
    {
        Repo = repo; Path = path;
        CloseCommand = new RelayCommand(() => code.Close(this));
        if (Images.Contains(Extension))
        {
            try { using var f = File.OpenRead(FullPath); Image = new Bitmap(f); }
            catch (Exception e) { Why = e.Message; }
            return;
        }
        (Text, Why) = Core.Repo.Read(FullPath);
    }
}
