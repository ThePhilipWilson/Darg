using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The Mistakes section: a project's mistake log, newest entry first, with each category counted on the
/// left. A category that has come up <see cref="MistakeLog.GateAt"/> times or more is marked: that is the point to
/// build a hook or gate instead of writing another rule down. Picking a category shows only its entries.</summary>
public partial class MistakesViewModel : ObservableObject, IDisposable
{
    public string File { get; }
    readonly string _projectName;
    readonly FileSystemWatcher? _watch;
    readonly DispatcherTimer _debounce;
    List<Mistake> _all = [];

    public ObservableCollection<MistakeCategory> Tally { get; } = [];
    public ObservableCollection<MistakeEntry> Entries { get; } = [];
    [ObservableProperty] public partial MistakeCategory? SelectedCategory { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool HasEntries { get; set; }

    public MistakesViewModel(string file, string projectName)
    {
        File = file;
        _projectName = projectName;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        try
        {
            // agents append to it while the app runs: show each entry as it lands
            var dir = Path.GetDirectoryName(file)!;
            Directory.CreateDirectory(dir);
            _watch = new FileSystemWatcher(dir, MistakeLog.FileName) { EnableRaisingEvents = true };
            void Poke(object? s, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });
            _watch.Changed += Poke; _watch.Created += Poke; _watch.Deleted += Poke; _watch.Renamed += (s, e) => Poke(s, e);
        }
        catch (Exception e) { Status = $"Can't watch {file}: {e.Message}"; }
        Reload();
    }

    public void Reload()
    {
        using var timing = StallWatch.Shared.Section("Mistakes.Reload");
        _all = MistakeLog.Read(File);
        var keep = SelectedCategory?.Name;
        Tally.Clear();
        Tally.Add(new MistakeCategory(null, _all.Count));
        foreach (var (name, count) in MistakeLog.Tally(_all)) Tally.Add(new MistakeCategory(name, count));
        SelectedCategory = Tally.FirstOrDefault(t => t.Name == keep) ?? Tally[0];
        Apply();
        var due = Tally.Where(t => t.Due).Select(t => t.Name).ToList();
        Status = !System.IO.File.Exists(File) ? $"No mistakes logged yet. Agents write them to {File}."
            : $"{_all.Count} {(_all.Count == 1 ? "entry" : "entries")} in {File}"
              + (due.Count > 0 ? $" · {string.Join(", ", due)} {(due.Count == 1 ? "has" : "have")} come up {MistakeLog.GateAt}+ times: time for a hook or gate" : "");
    }

    partial void OnSelectedCategoryChanged(MistakeCategory? value) => Apply();

    void Apply()
    {
        var pick = SelectedCategory?.Name;
        // newest first: by date, and among one day's entries the later-written one first
        var shown = _all.Select((m, i) => (m, i))
            .Where(x => pick is null || x.m.Categories.Contains(pick))
            .OrderByDescending(x => x.m.Date ?? DateOnly.MinValue).ThenByDescending(x => x.i)
            .Select(x => new MistakeEntry(x.m));
        Entries.Clear();
        foreach (var e in shown) Entries.Add(e);
        HasEntries = _all.Count > 0;
    }

    /// <summary>Open the log in the app markdown files open in (to read it whole, or fix a typo in a new entry).</summary>
    [RelayCommand]
    void OpenFile()
    {
        try
        {
            MistakeLog.Ensure(File, _projectName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(File) { UseShellExecute = true });
        }
        catch (Exception e) { Status = $"Can't open {File}: {e.Message}"; }
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _debounce.Stop();
    }
}

/// <summary>A row of the tally: a category and how many entries have it (null name: all entries).</summary>
public sealed class MistakeCategory(string? name, int count)
{
    public string? Name { get; } = name;
    public int Count { get; } = count;
    public string Label => Name ?? "All";
    /// <summary>Come up often enough that a gate is due (never for the All row).</summary>
    public bool Due => Name is not null && Count >= MistakeLog.GateAt;
    public string Tip => Name is null ? "Every entry in the log"
        : Due ? $"{Count} times: a rule written down hasn't held, so this wants a hook or gate" : $"{Count} {(Count == 1 ? "entry" : "entries")}";
}

/// <summary>An entry as shown: its title, a line of date, who caught it and its categories, and its body.</summary>
public sealed class MistakeEntry(Mistake m)
{
    public string Title { get; } = m.Title;
    public string Meta { get; } = string.Join(" · ", new[]
    {
        m.Date?.ToString("yyyy-MM-dd") ?? "",
        m.CaughtBy.Length > 0 ? $"caught by {m.CaughtBy}" : "",
        string.Join(", ", m.Categories),
    }.Where(s => s.Length > 0));
    public string Body { get; } = m.Body;
}
