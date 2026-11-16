using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Darg.Desktop.ViewModels;

/// <summary>A project's docs: the markdown in its docs folder (the Obsidian vault's, usually), read
/// rendered, edited as text, saved back. [[Wikilinks]] open the doc they name. Changes made in
/// Obsidian show here too.</summary>
public partial class DocsViewModel : ObservableObject
{
    readonly List<string> _all = [];
    readonly FileSystemWatcher? _watch;
    readonly DispatcherTimer _debounce;

    public string Folder { get; }
    /// <summary>Search results (shown instead of the tree while Find has text).</summary>
    public ObservableCollection<DocEntry> Files { get; } = [];
    /// <summary>The folder tree: folders first (each with its doc count), then docs.</summary>
    public ObservableCollection<DocNode> Tree { get; } = [];
    readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
    [ObservableProperty] public partial DocNode? SelectedNode { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Searching), nameof(Browsing))] public partial string Find { get; set; } = "";
    public bool Searching => Find.Trim().Length > 0;
    public bool Browsing => !Searching;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DocFolder))] public partial DocEntry? Selected { get; set; }
    /// <summary>The open doc's folder: where its pictures' relative paths start.</summary>
    public string? DocFolder => Selected is null ? null : Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Folder, Selected.Path)));
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Reading))] public partial bool Editing { get; set; }
    [ObservableProperty] public partial string EditText { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    public bool Reading => !Editing;

    public DocsViewModel(string folder)
    {
        Folder = folder;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); Refresh(); });
        Scan();
        try
        {
            _watch = new FileSystemWatcher(folder, "*.md") { IncludeSubdirectories = true, EnableRaisingEvents = true };
            void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
            _watch.Changed += Poke; _watch.Created += Poke; _watch.Deleted += Poke; _watch.Renamed += (s, e) => Poke(s, e);
        }
        catch (Exception) { }
    }

    void Scan()
    {
        _all.Clear();
        if (Directory.Exists(Folder))
            _all.AddRange(Directory.EnumerateFiles(Folder, "*.md", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Folder, f).Replace('\\', '/'))
                .Where(f => !f.Split('/').Any(part => part.StartsWith('.')))
                .Order(StringComparer.OrdinalIgnoreCase));
        Filter();
        BuildTree();
        Status = Directory.Exists(Folder) ? $"{_all.Count} docs in {Folder}" : $"No folder at {Folder}";
    }

    /// <summary>The tree from the file list, with folders that were open still open.</summary>
    void BuildTree()
    {
        Tree.Clear();
        foreach (var n in Children("")) Tree.Add(n);
        if (Selected is not null) Reveal(Selected.Path);
    }

    IEnumerable<DocNode> Children(string dir)
    {
        var prefix = dir.Length == 0 ? "" : dir + "/";
        var under = _all.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(f => f[prefix.Length..]).ToList();
        foreach (var sub in under.Where(r => r.Contains('/')).Select(r => r[..r.IndexOf('/')]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var path = prefix + sub;
            var count = under.Count(r => r.StartsWith(sub + "/", StringComparison.OrdinalIgnoreCase));
            yield return new DocNode(this, sub, path, isDir: true, count) { IsExpanded = _open.Contains(path) };
        }
        foreach (var file in under.Where(r => !r.Contains('/')))
            yield return new DocNode(this, System.IO.Path.GetFileNameWithoutExtension(file), prefix + file, isDir: false, 0);
    }

    internal IEnumerable<DocNode> ChildrenOf(DocNode folder) => Children(folder.Path);
    internal void Opened(DocNode folder, bool open) { if (open) _open.Add(folder.Path); else _open.Remove(folder.Path); }

    /// <summary>Open the folders down to a doc and select it in the tree.</summary>
    void Reveal(string path)
    {
        var parts = path.Split('/');
        IList<DocNode> level = Tree;
        for (var i = 0; i < parts.Length; i++)
        {
            var sofar = string.Join('/', parts.Take(i + 1));
            var node = level.FirstOrDefault(n => n.Path.Equals(sofar, StringComparison.OrdinalIgnoreCase));
            if (node is null) return;
            if (i == parts.Length - 1) { _revealing = true; SelectedNode = node; _revealing = false; return; }
            node.IsExpanded = true;
            level = node.Children;
        }
    }

    bool _revealing;

    partial void OnSelectedNodeChanged(DocNode? value)
    {
        if (_revealing || value is null || value.IsDir) return;
        if (Selected?.Path != value.Path) Selected = new DocEntry(value.Path);
    }

    /// <summary>Files came or went, or one changed: list again, and show the open one again unless it's being edited.</summary>
    void Refresh()
    {
        var keep = Selected?.Path;
        Scan();
        if (keep is not null && !Editing) Load(keep);
    }

    partial void OnFindChanged(string value)
    {
        Filter();
        if (!Searching && Selected is not null) Reveal(Selected.Path); // back to the tree: show where the open doc lives
    }

    void Filter()
    {
        var words = Find.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keep = Selected?.Path;
        Files.Clear();
        foreach (var f in _all.Where(f => words.All(w => f.Contains(w, StringComparison.OrdinalIgnoreCase))))
            Files.Add(new DocEntry(f));
        if (keep is not null) Selected = Files.FirstOrDefault(f => f.Path == keep) ?? Selected;
    }

    partial void OnSelectedChanged(DocEntry? value)
    {
        if (value is null) return;
        if (Editing) Editing = false; // leaving a doc ends its edit (unsaved text is dropped, as the doc changed)
        Load(value.Path);
    }

    void Load(string path)
    {
        try { Text = File.ReadAllText(Path.Combine(Folder, path)); }
        catch (Exception e) { Text = $"Couldn't read {path}: {e.Message}"; }
    }

    [RelayCommand]
    void Edit()
    {
        if (Selected is null) return;
        EditText = Text.Replace("\r\n", "\n");
        Editing = true;
    }

    [RelayCommand]
    void Save()
    {
        if (Selected is null || !Editing) return;
        var path = Path.Combine(Folder, Selected.Path);
        try
        {
            var crlf = File.Exists(path) && File.ReadAllText(path).Contains("\r\n");
            File.WriteAllText(path, crlf ? EditText.Replace("\n", "\r\n") : EditText, new UTF8Encoding(false));
            Text = EditText;
            Editing = false;
            Status = $"Saved {Selected.Path} at {DateTime.Now:HH:mm}";
        }
        catch (Exception e) { Status = $"Couldn't save: {e.Message}"; }
    }

    [RelayCommand] void Cancel() => Editing = false;

    /// <summary>Set by the app: show a markdown doc outside this folder in the project whose docs hold it.
    /// False if no other project's do, and it opens here.</summary>
    public Func<string, bool>? OpenElsewhere { get; set; }

    /// <summary>Open a doc: one in the folder (relative path) selected in the tree, or one outside it (its full
    /// path, e.g. another repo's README or a doc elsewhere in the vault) read, edited and saved here all the same,
    /// its full path in the header. True if there was a doc to open.</summary>
    public bool Show(string doc)
    {
        Find = "";
        var full = Path.GetFullPath(Path.Combine(Folder, doc));
        var rel = Path.GetRelativePath(Folder, full).Replace('\\', '/');
        var inside = rel != ".." && !rel.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
        if (inside && _all.FirstOrDefault(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase)) is { } hit)
        {
            Selected = Files.FirstOrDefault(f => f.Path == hit);
            Reveal(hit);
            return true;
        }
        if (!File.Exists(full)) { Status = $"No doc at {full}"; return false; }
        var path = full.Replace('\\', '/');
        if (!path.Equals(Selected?.Path, StringComparison.OrdinalIgnoreCase)) Selected = new DocEntry(path);
        _revealing = true; SelectedNode = null; _revealing = false; // it isn't in the tree
        Status = $"Outside the docs folder: {path}";
        return true;
    }

    /// <summary>A link in a doc: a doc in the folder opens here (a [[wikilink]] or a relative markdown link, as
    /// DocLinks resolves it); a markdown doc outside the folder (another repo, elsewhere in the vault) opens in
    /// the project whose docs hold it, else here; a web link, mail link or other file opens in its own app; one
    /// that goes nowhere says why.</summary>
    public void OpenLink(string link)
    {
        switch (Core.DocLinks.Resolve(link, Selected?.Path, _all, Folder))
        {
            case Core.DocLink.Doc(var hit) when hit.Equals(Selected?.Path, StringComparison.OrdinalIgnoreCase):
                Status = "That link is to this doc.";
                break;
            case Core.DocLink.Doc(var hit):
                Show(hit);
                break;
            case Core.DocLink.Elsewhere(var full):
                if (OpenElsewhere?.Invoke(full) == true) break; // another project's docs hold it: shown there
                Show(full);
                break;
            case Core.DocLink.External(var target):
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); Status = $"Opened {target}"; }
                catch (Exception e) { Status = $"Couldn't open {target}: {e.Message}"; }
                break;
            case Core.DocLink.Missing(var reason):
                Status = reason;
                break;
        }
    }
}

/// <summary>A folder or a doc in the docs tree. A folder's children are made when it is first opened.</summary>
public sealed partial class DocNode : ObservableObject
{
    readonly DocsViewModel _docs;
    bool _filled;
    public string Name { get; }
    public string Path { get; }
    public bool IsDir { get; }
    public string Count { get; }
    public ObservableCollection<DocNode> Children { get; } = [];
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    public DocNode(DocsViewModel docs, string name, string path, bool isDir, int count)
    {
        _docs = docs; Name = name; Path = path; IsDir = isDir; Count = isDir ? count.ToString() : "";
        if (isDir) Children.Add(new DocNode(docs, "…", "", false, 0)); // a placeholder so it shows an arrow
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!IsDir) return;
        _docs.Opened(this, value);
        if (!value || _filled) return;
        _filled = true;
        Children.Clear();
        foreach (var c in _docs.ChildrenOf(this)) Children.Add(c);
    }

    public override string ToString() => Path; // what screen readers read for the row
}

public sealed class DocEntry(string path)
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(path);
    public string Where { get; } = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
    public override string ToString() => Path; // what screen readers (and UI Automation) read for the row
}
