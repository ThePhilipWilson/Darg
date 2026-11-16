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
    public MascotViewModel Mascot { get; }
    const string MascotId = "~bench";

    public MainViewModel()
    {
        var config = InstanceConfig.Load();
        InstanceFile = InstanceConfig.FilePath();
        foreach (var p in config.Projects)
        {
            var vm = new ProjectViewModel(p);
            if (vm.Agents is { } agents)
                agents.Notify += (agent, what, detail) => Tell(vm, agent, what, detail);
            Projects.Add(vm);
        }
        Mascot = new MascotViewModel(config.Projects);
        Mascot.Notify += (agent, what, detail) =>
        {
            if (!(WindowActive && Mascot.Open)) Notifier.Show(MascotId, agent.Id, $"Bench: {what}", "", detail);
        };
        Notifier.Opened += (project, agent) => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenAgent(project, agent));
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

    /// <summary>Set by the window: whether it is the active window.</summary>
    public bool WindowActive { get; set; }
    /// <summary>Raised when the window should come to the front (a notification was clicked).</summary>
    public event Action? BringToFront;

    /// <summary>A notification, unless the person is already looking at that agent.</summary>
    void Tell(ProjectViewModel project, AgentViewModel agent, string what, string detail)
    {
        var looking = WindowActive && Selected == project && project.OnAgents && project.Agents?.Selected == agent;
        if (!looking) Notifier.Show(project.Config.Id, agent.Id, $"{project.Name}: {what}", agent.Name, detail);
    }

    /// <summary>Open a project's agent: its tab, the agents section, that agent, the window to the front.</summary>
    public void OpenAgent(string projectId, string agentId)
    {
        if (projectId == MascotId) { Mascot.Open = true; BringToFront?.Invoke(); return; }
        var project = Projects.FirstOrDefault(p => p.Config.Id == projectId);
        var agent = project?.Agents?.Find(agentId);
        if (project is null || agent is null) { BringToFront?.Invoke(); return; }
        Selected = project;
        project.OpenCommand.Execute(nameof(Section.Agents));
        project.Agents!.Selected = agent;
        BringToFront?.Invoke();
    }

    partial void OnSelectedChanged(ProjectViewModel? value)
    {
        foreach (var p in Projects) p.IsActive = p == value;
    }
}

public enum Section { Board, Notes, Agents, Terminal }

public partial class ProjectViewModel : ObservableObject
{
    public ProjectConfig Config { get; }
    public string Name => Config.Name;
    public IBrush Brush { get; }
    public BoardViewModel? Board { get; }
    public NotesViewModel? Notes { get; }
    public AgentsViewModel? Agents { get; }
    public string TerminalFolder => Config.WorkingFolder();
    public string Shell => OperatingSystem.IsWindows() ? "pwsh.exe" : Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";
    public bool UsesTerminal => Config.Uses(Features.Terminal);

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OnBoard), nameof(OnNotes), nameof(OnAgents), nameof(OnTerminal))]
    public partial Section Section { get; set; }
    /// <summary>The terminal starts the first time its section is opened, not with the app.</summary>
    [ObservableProperty] public partial bool TerminalStarted { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    public string StatusLine => $"{Name} · {Config.WorkingFolder()}" + (Board?.SelectedBoard is { } b ? $" · board {b}" : "");

    public bool OnBoard => Section == Section.Board;
    public bool OnNotes => Section == Section.Notes;
    public bool OnAgents => Section == Section.Agents;
    public bool OnTerminal => Section == Section.Terminal;

    public ProjectViewModel(ProjectConfig config)
    {
        Config = config;
        Brush = Color.TryParse(config.Color, out var c) ? new SolidColorBrush(c) : Brushes.Orange;
        if (config.Uses(Features.Board)) Board = new BoardViewModel(config);
        if (config.Uses(Features.Agents)) Agents = new AgentsViewModel(config);
        if (config.Uses(Features.Notes) && config.NotesFolder() is { } notes)
            Notes = new NotesViewModel(notes) { TicketBoard = () => Board?.Folder };
        Section = Board is not null ? Section.Board : Agents is not null ? Section.Agents : Section.Terminal;
        if (Board is not null && Agents is not null)
        {
            Board.AgentFor = Agents.ForCard;
            Board.OpenAgent = a => { Agents.Selected = a; Section = Section.Agents; };
            Board.WorkOn = (card, file) =>
            {
                var a = Agents.Start(AgentsViewModel.CardPrompt(Name, card, file), cardId: card.Id, name: $"{card.Id} {card.Title}");
                // the card shows the agent's state; when the agent ends, the card drops its link
                a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AgentViewModel.State) && a.State == AgentState.Exited) Board.Reload(); };
                Section = Section.Agents;
            };
        }
    }

    partial void OnSectionChanged(Section value)
    {
        if (value == Section.Terminal) TerminalStarted = true;
    }

    [RelayCommand] void Open(string section) => Section = Enum.Parse<Section>(section);
}
