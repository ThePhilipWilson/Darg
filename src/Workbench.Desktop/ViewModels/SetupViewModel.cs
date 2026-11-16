using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>The shopping list: which projects this instance has, and for each, its folders and which
/// features it uses. Edits a copy; Save writes instance.json and the app swaps in only the projects
/// that changed (agents and terminals in the others keep running).</summary>
public partial class SetupViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> Palette = ["#e0a84a", "#6aa5e8", "#a784e0", "#5fbf7f", "#e06464", "#e07ab8", "#4fc1c1", "#c9c9c9"];
    readonly MainViewModel _main;

    public ObservableCollection<ProjectSetup> Projects { get; } = [];
    [ObservableProperty] public partial ProjectSetup? Selected { get; set; }
    [ObservableProperty] public partial string InstanceName { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public string InstanceFile => InstanceConfig.FilePath();

    public SetupViewModel(MainViewModel main, InstanceConfig config)
    {
        _main = main;
        InstanceName = config.Name;
        foreach (var p in config.Projects) Projects.Add(new ProjectSetup(this, p));
        Selected = Projects.FirstOrDefault();
    }

    [RelayCommand]
    void Add()
    {
        var p = new ProjectSetup(this, new ProjectConfig { Name = "New project", Color = Palette[Projects.Count % Palette.Count] });
        Projects.Add(p);
        Selected = p;
    }

    internal void Remove(ProjectSetup p)
    {
        var i = Projects.IndexOf(p);
        Projects.Remove(p);
        Selected = Projects.Count == 0 ? null : Projects[Math.Min(i, Projects.Count - 1)];
    }

    [RelayCommand]
    void Save()
    {
        var configs = Projects.Select(p => p.ToConfig()).ToList();
        if (configs.FirstOrDefault(c => c.Name.Trim().Length == 0) is not null) { Error = "Every project needs a name."; return; }
        // ids: kept for existing projects, made from the name for new ones, never two the same
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in configs)
        {
            var id = c.Id.Length > 0 ? c.Id : Slug(c.Name);
            var n = 2;
            var baseId = id;
            while (!used.Add(id)) id = $"{baseId}-{n++}";
            c.Id = id;
        }
        var config = new InstanceConfig { Name = InstanceName.Trim().Length > 0 ? InstanceName.Trim() : "home", Projects = configs };
        try { config.Save(); }
        catch (Exception e) { Error = $"Couldn't save: {e.Message}"; return; }
        _main.Apply(config);
    }

    [RelayCommand] void Close() => _main.CloseSetup();

    static string Slug(string name)
    {
        var s = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Length > 0 ? s : "project";
    }

    /// <summary>Whether two project settings are the same (so the open project can stay as it is).</summary>
    public static bool Same(ProjectConfig a, ProjectConfig b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
}

/// <summary>One project's settings, being edited.</summary>
public partial class ProjectSetup : ObservableObject
{
    readonly SetupViewModel _setup;
    readonly string _id;
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Brush))] public partial string Color { get; set; }
    [ObservableProperty] public partial string Boards { get; set; }
    [ObservableProperty] public partial string Notes { get; set; }
    [ObservableProperty] public partial string ReposText { get; set; }
    [ObservableProperty] public partial string GitHubAccount { get; set; }
    /// <summary>The gh accounts this machine is logged in to, as a hint under the box.</summary>
    public string AccountsHint { get; } = GitHub.Accounts() is { Count: > 0 } a ? "Logged in to gh: " + string.Join(", ", a) : "gh isn't logged in to any account.";
    public ObservableCollection<FeatureTick> Features { get; } = [];
    public IReadOnlyList<SwatchChoice> Swatches { get; }
    public IBrush Brush => Avalonia.Media.Color.TryParse(Color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;
    public IRelayCommand RemoveCommand { get; }

    public ProjectSetup(SetupViewModel setup, ProjectConfig p)
    {
        _setup = setup;
        _id = p.Id;
        Name = p.Name; Color = p.Color; Boards = p.Boards ?? ""; Notes = p.Notes ?? "";
        ReposText = string.Join(Environment.NewLine, p.Repos);
        GitHubAccount = p.GitHub ?? "";
        foreach (var f in Core.Features.All) Features.Add(new FeatureTick(f, p.Uses(f)));
        Swatches = SetupViewModel.Palette.Select(c => new SwatchChoice(new SolidColorBrush(Avalonia.Media.Color.Parse(c)), new RelayCommand(() => Color = c))).ToList();
        RemoveCommand = new RelayCommand(() => _setup.Remove(this));
    }

    public ProjectConfig ToConfig()
    {
        var on = Features.Where(f => f.On).Select(f => f.Name).ToList();
        return new ProjectConfig
        {
            Id = _id, Name = Name.Trim(), Color = Color.Trim(),
            Boards = Boards.Trim().Length > 0 ? Boards.Trim() : null,
            Notes = Notes.Trim().Length > 0 ? Notes.Trim() : null,
            Repos = ReposText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
            GitHub = GitHubAccount.Trim().Length > 0 ? GitHubAccount.Trim() : null,
            // every feature ticked is stored as "all", so features added later are on by default
            Features = on.Count == Core.Features.All.Length ? [] : on,
        };
    }
}

public partial class FeatureTick(string name, bool on) : ObservableObject
{
    public string Name { get; } = name;
    public string Label { get; } = char.ToUpperInvariant(name[0]) + name[1..];
    [ObservableProperty] public partial bool On { get; set; } = on;
}

public sealed record SwatchChoice(IBrush Brush, IRelayCommand Command);
