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
        foreach (var p in config.Projects) Projects.Add(Make(p));
        Mascot = new MascotViewModel(config.Projects) { Context = Where };
        Mascot.Restore();
        foreach (var p in Projects) if (p.Agents is { } a) a.Mascot = Mascot;
        Mascot.Notify += (agent, what, detail) =>
        {
            if (!(WindowActive && Mascot.Open)) Notifier.Show(MascotId, agent.Id, $"Fettle: {what}", "", detail);
        };
        Notifier.Opened += (project, agent) => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenAgent(project, agent));
        Selected = Projects.FirstOrDefault();
        if (ArtifactHost.Server is { } artifacts)
        {
            artifacts.Reply += r => Avalonia.Threading.Dispatcher.UIThread.Post(() => FromArtifact(r));
            artifacts.ListAgents = () => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ListAgents).GetTask();
            artifacts.CloseAgents = ids => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => CloseAgents(ids)).GetTask();
            RegisterApi(artifacts.Api);
        }
        ArtifactHost.OpenRequested += file => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenArtifact(file));
        Restart = new RestartViewModel(BusyAgents, SaveAll);
        DevHooks();
    }

    /// <summary>The header's ⟳ menu: restart, or update from the repo and restart.</summary>
    public RestartViewModel Restart { get; }

    /// <summary>Agents in the middle of a turn, by name (Fettle's chats as "Fettle: name"): what a restart would cut off.</summary>
    IReadOnlyList<string> BusyAgents()
    {
        static bool Busy(AgentViewModel a) => a.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou;
        return Projects.SelectMany(p => p.Agents?.Agents ?? []).Where(Busy).Select(a => $"\"{a.Name}\"")
            .Concat(Mascot.Live().Where(Busy).Select(a => $"Fettle's \"{a.Name}\""))
            .Distinct().ToList();
    }

    /// <summary>What an artifact page sent: to the agent that made it, as a message from the person.</summary>
    void FromArtifact(ArtifactReply r)
    {
        var project = Projects.FirstOrDefault(p => string.Equals(p.Config.Id, r.Project, StringComparison.OrdinalIgnoreCase));
        var file = Path.Combine(ArtifactStore.Folder(r.Project), r.Agent, r.File);
        var title = File.Exists(file) ? ArtifactStore.Title(file) : r.File;
        if (project?.Agents?.Find(r.Agent) is not { } agent)
        {
            project?.Artifacts?.Tell($"\"{title}\" sent something, but the agent that made it is no longer listed, so it went nowhere.");
            return;
        }
        agent.Say($"From artifact \"{title}\":\n{r.Text}", $"From artifact \"{title}\" ({file}):\n{r.Text}");
        project.Artifacts?.Tell($"Sent to {agent.Name}.");
    }

    /// <summary>Every project's agents, for Fettle: which project, id, name, state, card, and whether a session is open.</summary>
    string ListAgents()
    {
        var list = new System.Text.Json.Nodes.JsonArray();
        foreach (var p in Projects)
            foreach (var a in p.Agents?.Agents ?? [])
                list.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["project"] = p.Config.Name, ["projectId"] = p.Config.Id, ["id"] = a.Id, ["name"] = a.Name, ["state"] = a.State.ToString().ToLowerInvariant(),
                    ["doing"] = a.Activity, ["open"] = a.Open, ["card"] = a.CardId, ["flow"] = a.Flow?.Status,
                });
        return list.ToJsonString();
    }

    /// <summary>Fettle asked to close these agents' sessions: close them, and say what happened to each.</summary>
    string CloseAgents(IReadOnlyList<string> ids)
    {
        var all = Projects.SelectMany(p => p.Agents?.Agents ?? []).ToList();
        return string.Join(Environment.NewLine, ids.Select(id => all.FirstOrDefault(a => a.Id == id) is not { } a ? $"{id}: no such agent"
            : a.CloseSession("Fettle") ? $"{id} ({a.Name}): closed" : $"{id} ({a.Name}): wasn't open"));
    }

    /// <summary>Show an artifact: its project's tab, the Artifacts section, that page.</summary>
    void OpenArtifact(string file)
    {
        if (ArtifactStore.Owner(file) is not { } owner) return;
        var project = Projects.FirstOrDefault(p => string.Equals(p.Config.Id, owner.Project, StringComparison.OrdinalIgnoreCase));
        if (project?.Artifacts is null) return;
        Selected = project;
        project.OpenCommand.Execute(nameof(Section.Artifacts));
        project.Artifacts.Show(file);
    }

    ProjectViewModel Make(ProjectConfig p)
    {
        var vm = new ProjectViewModel(p);
        if (vm.Agents is { } agents)
        {
            agents.Notify += (agent, what, detail) => Tell(vm, agent, what, detail);
            agents.Mascot = Mascot; // null while the app starts; set just after Bench is made
        }
        if (vm.Notes is { } notes) notes.Organise = (shown, text) => Mascot?.Ask(shown, text) ?? false;
        return vm;
    }

    /// <summary>What Philip is looking at, in a line Bench reads before each message: the project, the
    /// section, and what's open there (board and card, docs folder and doc, file, agent).</summary>
    public string Where()
    {
        if (Setup is not null) return "the Setup page";
        if (ToolsPage is not null) return "the Tools page";
        if (Selected is not { } p) return "";
        var s = new System.Text.StringBuilder($"{p.Name} › {p.Section}");
        switch (p.Section)
        {
            case Section.Board when p.Board is not null:
                s.Append($", board {p.Board.SelectedBoard} in {p.Board.Folder}");
                if (p.Board.Filter.Length > 0) s.Append($", filtered by \"{p.Board.Filter}\"");
                if (p.Board.Editor is { IsNew: false } card) s.Append($", card {card.Heading} ({card.Title}) open");
                break;
            case Section.Notes when p.Notes is not null:
                s.Append($", notes wall in {p.Notes.Folder}");
                break;
            case Section.Docs when p.Docs is not null:
                s.Append($", docs folder {p.Docs.Folder}");
                if (p.Docs.Selected is { } doc) s.Append($", doc {doc.Path} open");
                break;
            case Section.Code when p.Code is not null:
                s.Append($", repos {string.Join(", ", p.Config.Repos)}");
                if (p.Code.Current is { } f) s.Append($", file {f.FullPath} open");
                break;
            case Section.Agents when p.Agents?.Shown is { } a:
                s.Append($", agent \"{a.Name}\" ({a.StateText}), its folder {a.Cwd}"); // "working in" read as its state
                break;
            case Section.GitHub:
                s.Append($", repos {string.Join(", ", p.Config.Repos)}");
                break;
            case Section.Preview when p.Preview is not null:
                s.Append($", showing {p.Preview.Address}");
                break;
            case Section.Terminal:
                s.Append($", consoles in {p.Config.WorkingFolder()}");
                break;
        }
        return s.ToString();
    }

    /// <summary>Save every agent (on closing, so nothing since the last turn is lost).</summary>
    public void SaveAll()
    {
        foreach (var p in Projects) p.Agents?.SaveAll();
        Mascot.SaveAll();
    }

    // ---------------------------------------------------------------- Setup
    [ObservableProperty] public partial SetupViewModel? Setup { get; set; }
    [RelayCommand] void OpenSetup() => Setup = new SetupViewModel(this, InstanceConfig.Load());
    public void CloseSetup() => Setup = null;

    // ---------------------------------------------------------------- Tools
    [ObservableProperty] public partial ToolsViewModel? ToolsPage { get; set; }
    [RelayCommand] void OpenTools() => ToolsPage = new ToolsViewModel(this, InstanceConfig.Load().ToolsOrDefaults());
    public void CloseTools() => ToolsPage = null;

    /// <summary>A project tab dragged to a new place: the order is saved, so it opens that way next time.</summary>
    public void MoveProject(int from, int to)
    {
        Projects.Move(from, to);
        var config = InstanceConfig.Load();
        var order = Projects.Select((p, i) => (p.Config.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.OrdinalIgnoreCase);
        config.Projects = config.Projects.OrderBy(c => order.GetValueOrDefault(c.Id, int.MaxValue)).ToList();
        try { config.Save(); } catch (IOException) { } // the order still holds for this run
        Mascot.Projects = config.Projects;
    }

    /// <summary>A saved instance config, applied: projects whose settings didn't change stay exactly as
    /// they are (their agents and terminals keep running); changed ones are rebuilt; removed ones close.</summary>
    public void Apply(InstanceConfig config)
    {
        var old = Projects.ToDictionary(p => p.Config.Id, StringComparer.OrdinalIgnoreCase);
        var keep = new List<ProjectViewModel>();
        foreach (var c in config.Projects)
            keep.Add(old.TryGetValue(c.Id, out var vm) && SetupViewModel.Same(vm.Config, c) ? vm : Make(c));
        foreach (var gone in Projects.Where(p => !keep.Contains(p)).ToList()) gone.Close();
        var selectedId = Selected?.Config.Id;
        Projects.Clear();
        foreach (var p in keep) Projects.Add(p);
        Selected = Projects.FirstOrDefault(p => p.Config.Id == selectedId) ?? Projects.FirstOrDefault();
        foreach (var p in Projects) p.IsActive = p == Selected;
        Mascot.Projects = config.Projects;
        OnPropertyChanged(nameof(Empty));
        Setup = null;
    }

    /// <summary>For checking the app without clicking: WORKBENCH_SECTION opens a section of the first
    /// project (Board, Agents, Terminal); WORKBENCH_AGENT starts an agent there with that prompt, in
    /// WORKBENCH_AGENT_CWD if set, or with WORKBENCH_FLOW runs it through that flow.</summary>
    void DevHooks()
    {
        // WORKBENCH_ADD_FOLDER: open Setup and add that folder as a project (unsaved), as the folder picker would
        if (Environment.GetEnvironmentVariable("WORKBENCH_ADD_FOLDER") is { Length: > 0 } add)
        {
            OpenSetupCommand.Execute(null);
            Setup?.AddFolder(add);
        }
        if (Selected is null) return;
        if (Enum.TryParse<Section>(Environment.GetEnvironmentVariable("WORKBENCH_SECTION"), true, out var s)) Selected.OpenCommand.Execute(s.ToString());
        var prompt = Environment.GetEnvironmentVariable("WORKBENCH_AGENT");
        if (string.IsNullOrWhiteSpace(prompt) || Selected.Agents is null) return;
        var cwd = Environment.GetEnvironmentVariable("WORKBENCH_AGENT_CWD");
        // WORKBENCH_FLOW: run the prompt through that flow (by name) instead of starting one agent
        if (Environment.GetEnvironmentVariable("WORKBENCH_FLOW") is { Length: > 0 } flowName)
        {
            if (Selected.Agents.Flows.FirstOrDefault(f => f.Name.Equals(flowName, StringComparison.OrdinalIgnoreCase)) is { } flow)
                Selected.Agents.RunFlow(flow, prompt, ChatTitle.Guess(prompt), cwd);
            return;
        }
        Selected.Agents.Start(prompt, cwd);
    }

    /// <summary>Set by the window: whether it is the active window.</summary>
    public bool WindowActive { get; set; }
    /// <summary>Raised when the window should come to the front (a notification was clicked).</summary>
    public event Action? BringToFront;

    /// <summary>A notification, unless the person is already looking at that agent.</summary>
    void Tell(ProjectViewModel project, AgentViewModel agent, string what, string detail)
    {
        var looking = WindowActive && Selected == project && project.OnAgents && project.Agents?.Shown == agent;
        if (!looking) Notifier.Show(project.Config.Id, agent.Id, $"{project.Name}: {what}", agent.Name, detail);
    }

    /// <summary>Open a project's agent: its tab, the agents section, that agent, the window to the front.</summary>
    public void OpenAgent(string projectId, string agentId)
    {
        if (projectId == MascotId)
        {
            // a Bench chat: its project's tab, then that chat in Bench's box
            if (Mascot.ProjectOf(agentId) is { } home && Projects.FirstOrDefault(p => p.Config.Id == home) is { } owner) Selected = owner;
            Mascot.OpenChat(agentId);
            Mascot.Open = true;
            BringToFront?.Invoke();
            return;
        }
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
        Mascot?.ShowProject(value?.Config.Id); // Bench lists the chats of the project on screen
    }
}

public enum Section { Board, Notes, Docs, Agents, Artifacts, Code, GitHub, Preview, Terminal }

public partial class ProjectViewModel : ObservableObject
{
    public ProjectConfig Config { get; }
    public string Name => Config.Name;
    public IBrush Brush { get; }
    public BoardViewModel? Board { get; }
    public NotesViewModel? Notes { get; }
    public DocsViewModel? Docs { get; }
    public CodeViewModel? Code { get; }
    public GitHubViewModel? Hub { get; }
    public PreviewViewModel? Preview { get; }
    public AgentsViewModel? Agents { get; }
    public ConsolesViewModel? Consoles { get; }
    /// <summary>Pages the project's agents made (there with the agents feature).</summary>
    public ArtifactsViewModel? Artifacts { get; }
    public bool UsesTerminal => Consoles is not null;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OnBoard), nameof(OnNotes), nameof(OnDocs), nameof(OnAgents), nameof(OnArtifacts), nameof(OnCode), nameof(OnHub), nameof(OnPreview), nameof(OnTerminal))]
    public partial Section Section { get; set; }
    /// <summary>The terminal starts the first time its section is opened, not with the app.</summary>
    [ObservableProperty] public partial bool TerminalStarted { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    public string StatusLine => $"{Name} · {Config.WorkingFolder()}" + (Board?.SelectedBoard is { } b ? $" · board {b}" : "");

    public bool OnBoard => Section == Section.Board;
    public bool OnNotes => Section == Section.Notes;
    public bool OnDocs => Section == Section.Docs;
    public bool OnCode => Section == Section.Code;
    public bool OnHub => Section == Section.GitHub;
    public bool OnPreview => Section == Section.Preview;
    public bool OnAgents => Section == Section.Agents;
    public bool OnArtifacts => Section == Section.Artifacts;
    public bool OnTerminal => Section == Section.Terminal;

    public ProjectViewModel(ProjectConfig config)
    {
        Config = config;
        Brush = Color.TryParse(config.Color, out var c) ? new SolidColorBrush(c) : Brushes.Orange;
        if (config.Uses(Features.Board)) Board = new BoardViewModel(config);
        if (config.Uses(Features.Agents))
        {
            Agents = new AgentsViewModel(config);
            Artifacts = new ArtifactsViewModel(config.Id, id => Agents.Find(id)?.Name);
        }
        if (config.Uses(Features.Terminal)) Consoles = new ConsolesViewModel(config);
        if (config.Uses(Features.Docs) && config.Docs is { Length: > 0 } docs) Docs = new DocsViewModel(docs);
        if (config.Uses(Features.Notes) && config.NotesFolder() is { } notes)
            Notes = new NotesViewModel(notes, config.Boards)
            {
                TicketBoard = () => Board?.Folder,
                OpenTicket = id => { if (Board?.OpenById(id) == true) Section = Section.Board; },
            };
        if (config.Uses(Features.Code) && config.Repos.Count > 0) Code = new CodeViewModel(config);
        if (config.Uses(Features.GitHub) && config.Repos.Count > 0) Hub = new GitHubViewModel(config, () => Board?.Folder);
        if (config.Uses(Features.Preview) && Core.Preview.For(config) is { } preview) Preview = new PreviewViewModel(preview);
        Section = Board is not null ? Section.Board : Agents is not null ? Section.Agents : Section.Terminal;
        if (Board is not null && Agents is not null)
        {
            Board.AgentFor = Agents.ForCard;
            Board.OpenAgent = a => { Agents.Selected = a; Section = Section.Agents; };
            Board.StartAgent = (prompt, cwd, name) => { Agents.Start(prompt, cwd, name: name); Section = Section.Agents; };
            string CardPrompt(Card card, string file, IReadOnlyList<string> alongside)
            {
                // everyone at work in this folder: the batch this card was started with, and agents busy here now
                var folder = Config.WorkingFolder();
                var busy = Agents.Agents
                    .Where(x => x.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou && SameFolder(x.Cwd, folder))
                    .Select(x => x.CardId ?? $"\"{x.Name}\"");
                var others = alongside.Concat(busy).Where(x => x != card.Id).Distinct().ToList();
                var repo = Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")) ? folder : null;
                return AgentsViewModel.CardPrompt(Name, card, file, others, repo);
            }
            // what picks a card agent's model profile: the card's words, type and labels, not the house rules around them
            static AgentJob CardJob(Card card) => new(JobKinds.Card, $"{card.Title}\n\n{card.Description}".Trim(), card.Type, card.Labels);
            // the card shows the agent's state; when the agent ends, the card drops its link
            void Watch(AgentViewModel a) =>
                a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AgentViewModel.State) && a.State == AgentState.Exited) Board.Reload(); };
            Board.WorkOn = (card, file, alongside, show) =>
            {
                Watch(Agents.Start(CardPrompt(card, file, alongside), cardId: card.Id, name: $"{card.Id} {card.Title}", job: CardJob(card)));
                if (show) Section = Section.Agents;
            };
            Board.Flows = () => Agents.Flows;
            Board.RunFlow = (card, file, flow) =>
            {
                Agents.ReloadFlows(); // its file may have been edited since the board last looked
                var current = Agents.Flows.FirstOrDefault(f => f.Name == flow.Name) ?? flow;
                var run = Agents.RunFlow(current, CardPrompt(card, file, []), $"{card.Id} {card.Title}", cardId: card.Id, job: CardJob(card));
                // each stage is a new agent: the card follows the one at work
                run.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowRun.Status)) Board.Reload(); };
                Section = Section.Agents;
            };
        }
    }

    static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    partial void OnSectionChanged(Section value)
    {
        if (value == Section.Terminal) { TerminalStarted = true; Consoles?.EnsureOne(); }
        if (value == Section.Code) Code?.Load();
        if (value == Section.GitHub) Hub?.Load();
        if (value == Section.Artifacts) Artifacts?.Refresh();
        if (value == Section.Preview) _ = Preview?.Open();
    }

    /// <summary>Show a section, if this project has it (an unticked feature's section never opens).</summary>
    [RelayCommand]
    void Open(string section)
    {
        var s = Enum.Parse<Section>(section);
        if (Has(s)) Section = s;
    }

    /// <summary>Whether this project has a section (its feature ticked and what it needs set up).</summary>
    public bool Has(Section s) => s switch
    {
        Section.Board => Board is not null, Section.Notes => Notes is not null, Section.Docs => Docs is not null, Section.Agents => Agents is not null, Section.Artifacts => Artifacts is not null,
        Section.Code => Code is not null, Section.GitHub => Hub is not null, Section.Preview => Preview is not null, _ => Consoles is not null,
    };

    /// <summary>The project is leaving the app (removed or changed in Setup): stop its agents and its watcher.</summary>
    public void Close()
    {
        foreach (var a in Agents?.Agents ?? []) a.End();
        Board?.Dispose();
        Preview?.Close();
    }
}
