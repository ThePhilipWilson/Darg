using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<ProjectViewModel> Projects { get; } = [];
    [ObservableProperty] public partial ProjectViewModel? Selected { get; set; }
    public string InstanceFile { get; }
    public bool Empty => Projects.Count == 0;
    public MascotViewModel Mascot { get; }
    static readonly IBrush FettleLightDefault = new SolidColorBrush(Color.Parse("#f5c56b"));
    static readonly IBrush FettleDarkDefault = new SolidColorBrush(Color.Parse("#d08a2e"));
    static readonly IBrush FettleShavingDefault = new SolidColorBrush(Color.Parse("#b8741f"));
    /// <summary>Fettle takes the colour of the project on screen.</summary>
    public Color FettleLight => (Selected?.LightBrush ?? FettleLightDefault) is SolidColorBrush b ? b.Color : Colors.Orange;
    public Color FettleDark => (Selected?.DarkBrush ?? FettleDarkDefault) is SolidColorBrush b ? b.Color : Colors.Orange;
    public IBrush FettleShaving => Selected?.ShavingBrush ?? FettleShavingDefault;
    public IBrush FettleEdge => Selected?.Brush ?? Brushes.Orange;
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
            Log(MascotId, agent.Id, "Fettle", agent.Name, what, detail);
            if (!(WindowActive && Mascot.Open)) Notifier.Show(MascotId, agent.Id, $"Fettle: {what}", "", detail);
        };
        Notifier.Opened += (project, agent) => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenAgent(project, agent));
        Selected = LastProject.Pick(Projects, p => p.Config.Id); // the project that was open last time
        if (ArtifactHost.Server is { } artifacts)
        {
            artifacts.Reply += r => Avalonia.Threading.Dispatcher.UIThread.Post(() => FromArtifact(r));
            artifacts.ListAgents = () => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ListAgents()).GetTask();
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
    /// <summary>The agents as JSON: every project's, or only <paramref name="only"/>'s.</summary>
    string ListAgents(ProjectViewModel? only = null)
    {
        var list = new System.Text.Json.Nodes.JsonArray();
        foreach (var p in Projects.Where(p => only is null || p == only))
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
        if (vm.Docs is { } docs) docs.OpenElsewhere = full => ShowDocElsewhere(vm, full);
        WatchNav(vm);
        return vm;
    }

    /// <summary>A doc link that left a project's docs folder: if another project's docs hold the doc, that
    /// project comes up with it open (the deepest folder wins when one is inside another).</summary>
    bool ShowDocElsewhere(ProjectViewModel from, string full)
    {
        var owner = Projects.Where(p => p != from && p.Docs is not null)
            .Select(p => (p, rel: Path.GetRelativePath(Path.GetFullPath(p.Docs!.Folder), full)))
            .Where(x => x.rel != ".." && !x.rel.StartsWith(".." + Path.DirectorySeparatorChar) && !x.rel.StartsWith("../") && !Path.IsPathRooted(x.rel))
            .OrderByDescending(x => x.p.Docs!.Folder.Length)
            .Select(x => x.p).FirstOrDefault();
        if (owner is null) return false;
        Setup = null; ToolsPage = null; SettingsPage = null;
        Selected = owner;
        owner.OpenCommand.Execute(nameof(Section.Docs));
        return owner.Docs!.Show(full);
    }

    /// <summary>What Philip is looking at, in a line Bench reads before each message: the project, the
    /// section, and what's open there (board and card, docs folder and doc, file, agent).</summary>
    public string Where()
    {
        if (Setup is not null) return "the Setup page";
        if (ToolsPage is not null) return "the Tools page";
        if (SettingsPage is not null) return "the Settings page";
        if (Selected is not { } p) return "";
        var s = new System.Text.StringBuilder($"{p.Name} › {p.Section}");
        switch (p.Section)
        {
            case Section.Home:
                s.Append(p.Home.ReadmePath is { } readme ? $", its home page showing {readme}" : ", its home page (no README)");
                break;
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
            case Section.Reports when p.Reports is not null:
                s.Append($", reports folder {p.Reports.Folder}" + (p.Reports.Selected is { } r ? $", report \"{r.Heading}\" ({r.Report.File})" : ""));
                break;
            case Section.Mistakes when p.Mistakes is not null:
                s.Append($", mistake log {p.Mistakes.File}");
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

    // ---------------------------------------------------------------- Settings
    [ObservableProperty] public partial SettingsViewModel? SettingsPage { get; set; }
    [RelayCommand] void OpenSettings() => SettingsPage = new SettingsViewModel(this);
    public void CloseSettings() => SettingsPage = null;

    /// <summary>The glyph before the app's name in the header: the theme's (girlypop's is a heart).</summary>
    public string Mark => ThemeManager.Current.Mark + " Darg";

    /// <summary>Shows a theme at once and saves it as the instance's; null when saved, else why it wasn't.</summary>
    public string? SetTheme(Theme theme)
    {
        ThemeManager.Apply(theme);
        OnPropertyChanged(nameof(Mark));
        var settings = AppSettings.Load();
        settings.Theme = theme.Id;
        return settings.Save() is { } why ? $"Showing {theme.Name}, but couldn't save it: {why}" : null;
    }

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

    /// <summary>For checking the app without clicking: DARG_SECTION opens a section of the first
    /// project (Board, Agents, Terminal); DARG_AGENT starts an agent there with that prompt, in
    /// DARG_AGENT_CWD if set, or with DARG_FLOW runs it through that flow.</summary>
    void DevHooks()
    {
        // DARG_ADD_FOLDER: open Setup and add that folder as a project (unsaved), as the folder picker would
        if (Environment.GetEnvironmentVariable("DARG_ADD_FOLDER") is { Length: > 0 } add)
        {
            OpenSetupCommand.Execute(null);
            Setup?.AddFolder(add);
        }
        var section = Environment.GetEnvironmentVariable("DARG_SECTION");
        var prompt = Environment.GetEnvironmentVariable("DARG_AGENT");
        // these act on the first project, whichever was open last
        if (!string.IsNullOrWhiteSpace(section) || !string.IsNullOrWhiteSpace(prompt)) Selected = Projects.FirstOrDefault();
        if (Selected is null) return;
        if (Enum.TryParse<Section>(section, true, out var s)) Selected.OpenCommand.Execute(s.ToString());
        if (string.IsNullOrWhiteSpace(prompt) || Selected.Agents is null) return;
        var cwd = Environment.GetEnvironmentVariable("DARG_AGENT_CWD");
        // DARG_FLOW: run the prompt through that flow (by name) instead of starting one agent
        if (Environment.GetEnvironmentVariable("DARG_FLOW") is { Length: > 0 } flowName)
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

    /// <summary>Every notification agents have raised this run, newest first: the popups come and go, this stays.</summary>
    public System.Collections.ObjectModel.ObservableCollection<NotificationEntry> Notifications { get; } = [];
    [ObservableProperty] public partial bool ShowNotifications { get; set; }
    [RelayCommand] void ShowChatTab() => ShowNotifications = false;
    [RelayCommand] void ShowNotificationsTab() => ShowNotifications = true;
    [RelayCommand] void ClearNotifications() => Notifications.Clear();
    [RelayCommand] void OpenNotification(NotificationEntry? n) { if (n is not null) OpenAgent(n.ProjectId, n.AgentId); }

    void Log(string projectId, string agentId, string project, string agent, string what, string detail)
    {
        void Add()
        {
            Notifications.Insert(0, new NotificationEntry(projectId, agentId, project, agent, what, detail, DateTime.Now));
            while (Notifications.Count > 100) Notifications.RemoveAt(Notifications.Count - 1);
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Add(); else Avalonia.Threading.Dispatcher.UIThread.Post(Add);
    }

    /// <summary>A notification, unless the person is already looking at that agent.</summary>
    void Tell(ProjectViewModel project, AgentViewModel agent, string what, string detail)
    {
        Log(project.Config.Id, agent.Id, project.Name, agent.Name, what, detail);
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
        OnPropertyChanged(nameof(FettleLight)); OnPropertyChanged(nameof(FettleDark));
        OnPropertyChanged(nameof(FettleShaving)); OnPropertyChanged(nameof(FettleEdge));
        Mascot?.ShowProject(value?.Config.Id); // Bench lists the chats of the project on screen
        LastProject.Write(value?.Config.Id);
        NavMoved();
    }
}

/// <summary>One notification an agent raised, kept in the list Fettle shows.</summary>
public record NotificationEntry(string ProjectId, string AgentId, string Project, string Agent, string What, string Detail, DateTime At)
{
    public string Title => $"{Project}: {What}";
    public string Sub => string.IsNullOrWhiteSpace(Agent) ? $"{At:HH:mm}" : $"{Agent} · {At:HH:mm}";
}

public enum Section { Home, Board, Notes, Docs, Agents, Reports, Artifacts, Mistakes, Code, GitHub, Preview, Terminal }

public partial class ProjectViewModel : ObservableObject
{
    public ProjectConfig Config { get; }
    public string Name => Config.Name;
    public IBrush Brush { get; }
    /// <summary>The project colour, lightened and darkened: Fettle wears it.</summary>
    public IBrush LightBrush { get; }
    public IBrush DarkBrush { get; }
    public IBrush ShavingBrush { get; }
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
    /// <summary>The project's mistake log, read and counted (there with the agents feature, which writes it).</summary>
    public MistakesViewModel? Mistakes { get; }
    /// <summary>Where a project opens: its README and what's going on (every project has one).</summary>
    public HomeViewModel Home { get; }
    /// <summary>What card agents filed when they finished (there with the agents feature).</summary>
    public ReportsViewModel? Reports { get; }
    public bool UsesTerminal => Consoles is not null;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OnHome), nameof(OnBoard), nameof(OnNotes), nameof(OnDocs), nameof(OnAgents), nameof(OnReports), nameof(OnArtifacts), nameof(OnMistakes), nameof(OnCode), nameof(OnHub), nameof(OnPreview), nameof(OnTerminal))]
    public partial Section Section { get; set; }
    /// <summary>The terminal starts the first time its section is opened, not with the app.</summary>
    [ObservableProperty] public partial bool TerminalStarted { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    public string StatusLine => $"{Name} · {Config.WorkingFolder()}" + (Board?.SelectedBoard is { } b ? $" · board {b}" : "");

    public bool OnHome => Section == Section.Home;
    public bool OnBoard => Section == Section.Board;
    public bool OnNotes => Section == Section.Notes;
    public bool OnDocs => Section == Section.Docs;
    public bool OnCode => Section == Section.Code;
    public bool OnHub => Section == Section.GitHub;
    public bool OnPreview => Section == Section.Preview;
    public bool OnAgents => Section == Section.Agents;
    public bool OnArtifacts => Section == Section.Artifacts;
    public bool OnMistakes => Section == Section.Mistakes;
    public bool OnReports => Section == Section.Reports;
    public bool OnTerminal => Section == Section.Terminal;

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public ProjectViewModel(ProjectConfig config)
    {
        Config = config;
        Brush = Color.TryParse(config.Color, out var c) ? new SolidColorBrush(c) : Brushes.Orange;
        var baseColour = Color.TryParse(config.Color, out var bc) ? bc : Colors.Orange;
        LightBrush = new SolidColorBrush(Mix(baseColour, Colors.White, 0.35));
        DarkBrush = new SolidColorBrush(Mix(baseColour, Colors.Black, 0.25));
        ShavingBrush = new SolidColorBrush(Mix(baseColour, Colors.Black, 0.4));
        if (config.Uses(Features.Board)) Board = new BoardViewModel(config);
        if (config.Uses(Features.Agents))
        {
            Agents = new AgentsViewModel(config);
            Artifacts = new ArtifactsViewModel(config.Id, id => Agents.Find(id)?.Name);
            Mistakes = new MistakesViewModel(MistakeLog.PathFor(config.Id), config.Name);
            Reports = new ReportsViewModel(ReportStore.FolderFor(config.Id));
            // an agent that filed a report goes once its turn ends well: the report is what's kept of it
            Reports.Loaded += Agents.Reported;
            Agents.Reported(ReportStore.List(Reports.Folder));
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
        Home = new HomeViewModel(this);
        // every project opens to Home (Section's default), read in the background
        _ = Home.Load();
        if (Board is not null && Agents is not null)
        {
            Board.AgentFor = Agents.ForCard;
            // a card agent shows its card's state: "Complete, pending removal" once the card is done
            Agents.CardIsDone = Board.IsDone;
            Board.Reloaded += Agents.RefreshCards;
            Reports!.OpenCard = id => { if (!Board.OpenById(id)) return false; Section = Section.Board; return true; };
            Agents.RefreshCards();
            // a card looks up its agent only when the board rebuilds: forgetting the agent frees the card now
            Agents.Forgotten += a => { if (a.CardId is { } card) Board.AgentGone(card); };
            Board.OpenAgent = a => { Agents.Selected = a; Section = Section.Agents; };
            Board.StartAgent = (prompt, cwd, name) => { Agents.Start(prompt, cwd, name: name); Section = Section.Agents; };
            string CardPrompt(Card card, string file, IReadOnlyList<string> alongside, string? agentId = null)
            {
                // everyone at work in this folder: the batch this card was started with, and agents busy here now
                var folder = CardFolder(card);
                var busy = Agents.Agents
                    .Where(x => x.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou && SameFolder(x.Cwd, folder))
                    .Select(x => x.CardId ?? $"\"{x.Name}\"");
                var others = alongside.Concat(busy).Where(x => x != card.Id).Distinct().ToList();
                var repo = Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")) ? folder : null;
                var commits = Core.Commits.For(Config, folder) is { } rule ? Core.Commits.Describe(rule, folder) : null;
                // done and a report when it's sure, the board's QA column when I should look (WOR-0061)
                var finish = ReportStore.Rules(Reports!.Folder, card, BoardColumns.Qa(Board.Columns), agentId);
                return AgentsViewModel.CardPrompt(Name, card, file, others, repo, commits, finish);
            }
            // what picks a card agent's model profile: the card's words, type and labels, not the house rules around them
            static AgentJob CardJob(Card card) => new(JobKinds.Card, $"{card.Title}\n\n{card.Description}".Trim(), card.Type, card.Labels);
            // the card shows the agent's state; when the agent ends, the card drops its link
            void Watch(AgentViewModel a) =>
                a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AgentViewModel.State) && a.State == AgentState.Exited) Board.Reload(); };
            Board.WorkOn = (card, file, alongside, show) =>
            {
                var id = AgentsViewModel.NewId();
                Watch(Agents.Start(CardPrompt(card, file, alongside, id), CardFolder(card), cardId: card.Id, name: $"{card.Id} {card.Title}", job: CardJob(card), id: id));
                if (show) Section = Section.Agents;
            };
            Board.Flows = () => Agents.Flows;
            Board.RunFlow = (card, file, flow) =>
            {
                Agents.ReloadFlows(); // its file may have been edited since the board last looked
                var current = Agents.Flows.FirstOrDefault(f => f.Name == flow.Name) ?? flow;
                var run = Agents.RunFlow(current, CardPrompt(card, file, []), $"{card.Id} {card.Title}", cwd: CardFolder(card), cardId: card.Id, job: CardJob(card));
                // each stage is a new agent: the card follows the one at work
                run.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowRun.Status)) Board.Reload(); };
                Section = Section.Agents;
            };
        }
    }

    /// <summary>Where a card's agent starts: the repo the card names ("repo:Workspace" label, "Repo: Workspace"
    /// line), else the project's first repo. The other repos are given to it either way.</summary>
    string CardFolder(Card card) => Config.RepoFor(card.Labels, card.Description) ?? Config.WorkingFolder();

    static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // coming back to a tab on Home: the README or commits may have changed meanwhile
    partial void OnIsActiveChanged(bool value) { if (value && OnHome) _ = Home.Load(); }

    partial void OnSectionChanged(Section value)
    {
        if (value == Section.Home) _ = Home.Load();
        if (value == Section.Terminal) { TerminalStarted = true; Consoles?.EnsureOne(); }
        if (value == Section.Code) Code?.Load();
        if (value == Section.GitHub) Hub?.Load();
        if (value == Section.Artifacts) Artifacts?.Refresh();
        if (value == Section.Mistakes) Mistakes?.Reload();
        if (value == Section.Reports) Reports?.Reload();
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
        Section.Home => true, Section.Board => Board is not null, Section.Notes => Notes is not null, Section.Docs => Docs is not null, Section.Agents => Agents is not null, Section.Artifacts => Artifacts is not null, Section.Mistakes => Mistakes is not null, Section.Reports => Reports is not null,
        Section.Code => Code is not null, Section.GitHub => Hub is not null, Section.Preview => Preview is not null, _ => Consoles is not null,
    };

    /// <summary>The project is leaving the app (removed or changed in Setup): stop its agents and its watcher.</summary>
    public void Close()
    {
        foreach (var a in Agents?.Agents ?? []) a.End();
        Board?.Dispose();
        Mistakes?.Dispose();
        Reports?.Dispose();
        Preview?.Close();
    }
}
