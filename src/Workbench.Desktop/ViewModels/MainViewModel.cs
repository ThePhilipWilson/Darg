using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<ProjectViewModel> Projects { get; } = [];
    [ObservableProperty] public partial ProjectViewModel? Selected { get; set; }
    public string InstanceFile { get; }
    public bool Empty => Projects.Count == 0;

    public MainViewModel()
    {
        var config = InstanceConfig.Load();
        InstanceFile = InstanceConfig.FilePath();
        foreach (var p in config.Projects) Projects.Add(new ProjectViewModel(p));
        Selected = Projects.FirstOrDefault();
        DevHooks();
    }

    /// <summary>For checking the app without clicking: WORKBENCH_SECTION opens a section of the first
    /// project (Board, Agents, Terminal); WORKBENCH_AGENT starts an agent there with that prompt, in
    /// WORKBENCH_AGENT_CWD if set.</summary>
    void DevHooks()
    {
        if (Selected is null) return;
        if (Enum.TryParse<Section>(Environment.GetEnvironmentVariable("WORKBENCH_SECTION"), true, out var s)) Selected.OpenCommand.Execute(s.ToString());
        var prompt = Environment.GetEnvironmentVariable("WORKBENCH_AGENT");
        if (string.IsNullOrWhiteSpace(prompt) || Selected.Agents is null) return;
        Selected.Agents.Start(prompt, Environment.GetEnvironmentVariable("WORKBENCH_AGENT_CWD"));
    }

    partial void OnSelectedChanged(ProjectViewModel? value)
    {
        foreach (var p in Projects) p.IsActive = p == value;
    }
}

public enum Section { Board, Agents, Terminal }

public partial class ProjectViewModel : ObservableObject
{
    public ProjectConfig Config { get; }
    public string Name => Config.Name;
    public IBrush Brush { get; }
    public BoardViewModel? Board { get; }
    public AgentsViewModel? Agents { get; }
    public string TerminalFolder => Config.WorkingFolder();
    public string Shell => OperatingSystem.IsWindows() ? "pwsh.exe" : Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";
    public bool UsesTerminal => Config.Uses(Features.Terminal);

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OnBoard), nameof(OnAgents), nameof(OnTerminal))]
    public partial Section Section { get; set; }
    /// <summary>The terminal starts the first time its section is opened, not with the app.</summary>
    [ObservableProperty] public partial bool TerminalStarted { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    public string StatusLine => $"{Name} · {Config.WorkingFolder()}" + (Board?.SelectedBoard is { } b ? $" · board {b}" : "");

    public bool OnBoard => Section == Section.Board;
    public bool OnAgents => Section == Section.Agents;
    public bool OnTerminal => Section == Section.Terminal;

    public ProjectViewModel(ProjectConfig config)
    {
        Config = config;
        Brush = Color.TryParse(config.Color, out var c) ? new SolidColorBrush(c) : Brushes.Orange;
        if (config.Uses(Features.Board)) Board = new BoardViewModel(config);
        if (config.Uses(Features.Agents)) Agents = new AgentsViewModel(config);
        Section = Board is not null ? Section.Board : Agents is not null ? Section.Agents : Section.Terminal;
    }

    partial void OnSectionChanged(Section value)
    {
        if (value == Section.Terminal) TerminalStarted = true;
    }

    [RelayCommand] void Open(string section) => Section = Enum.Parse<Section>(section);
}
