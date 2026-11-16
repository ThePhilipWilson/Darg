using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Workbench.Desktop.ViewModels;

/// <summary>A project's docs: the markdown in its docs folder (the Obsidian vault's, usually), read
/// rendered, edited as text, saved back. [[Wikilinks]] open the doc they name. Changes made in
/// Obsidian show here too.</summary>
public partial class DocsViewModel : ObservableObject
{
    readonly List<string> _all = [];
    readonly FileSystemWatcher? _watch;
    readonly DispatcherTimer _debounce;

    public string Folder { get; }
    public ObservableCollection<DocEntry> Files { get; } = [];
    [ObservableProperty] public partial string Find { get; set; } = "";
    [ObservableProperty] public partial DocEntry? Selected { get; set; }
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
        Status = Directory.Exists(Folder) ? $"{_all.Count} docs in {Folder}" : $"No folder at {Folder}";
    }

    /// <summary>Files came or went, or one changed: list again, and show the open one again unless it's being edited.</summary>
    void Refresh()
    {
        var keep = Selected?.Path;
        Scan();
        if (keep is not null && !Editing) Load(keep);
    }

    partial void OnFindChanged(string value) => Filter();

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

    /// <summary>A link in a doc: a wikilink opens the doc it names (anywhere in the folder), a web link the browser.</summary>
    public void OpenLink(string link)
    {
        if (link.StartsWith("wiki:"))
        {
            var name = link[5..].Split('#')[0].Trim();
            var hit = _all.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase))
                      ?? _all.FirstOrDefault(f => f.Equals(name + ".md", StringComparison.OrdinalIgnoreCase));
            if (hit is null) { Status = $"No doc called {name} in this folder."; return; }
            Find = "";
            Selected = Files.FirstOrDefault(f => f.Path == hit);
            return;
        }
        if (link.StartsWith("http", StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
    }
}

public sealed class DocEntry(string path)
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(path);
    public string Where { get; } = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
    public override string ToString() => Path; // what screen readers (and UI Automation) read for the row
}
