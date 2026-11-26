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
    bool _movedShown;

    public MainViewModel()
    {
        // before anything opens a path: a project's working files move into .darg in its repo (WOR-0144)
        try { Moved = ProjectData.Migrate(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var config = InstanceConfig.Load();
        InstanceFile = InstanceConfig.FilePath();
        foreach (var p in config.Projects) Projects.Add(Make(p));
        Mascot = new MascotViewModel(config.Projects) { Context = Where };
        Mascot.Restore();
        Usage.OptimiseChanged = on => SetOptimiseTokens(on);
        foreach (var p in Projects) if (p.Agents is { } a) a.Mascot = Mascot;
        Mascot.Notify += (agent, what, detail) =>
        {
            Log(MascotId, agent.Id, "Fettle", agent.Name, what, detail);
            if (!(WindowActive && Mascot.Open))
            {
                Notifier.Show(MascotId, agent.Id, NotificationEntry.HeaderFor("Fettle", agent.Name, what), "Fettle", detail, NotificationEntry.Asks(what));
                PushTell(NotificationEntry.HeaderFor("Fettle", agent.Name, what), what, detail);
            }
        };
        Mascot.AsksSettled += agent => Settle(MascotId, agent.Id);
        Notifier.Opened += (project, agent) => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenAgent(project, agent));
        Selected = LastProject.Pick(Projects, p => p.Config.Id); // the project that was open last time
        if (ArtifactHost.Server is { } artifacts)
        {
            artifacts.Reply += r => Avalonia.Threading.Dispatcher.UIThread.Post(() => FromArtifact(r));
            artifacts.ListAgents = () => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ListAgents()).GetTask();
            artifacts.CloseAgents = ids => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => CloseAgents(ids)).GetTask();
            RegisterApi(artifacts.Api);
            StartRemote(config, artifacts.Api);
        }
        ArtifactHost.OpenRequested += file => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenArtifact(file));
        Restart = new RestartViewModel(BusyAgents, SaveAll);
        StartPush(config.Push); // AND-0006: phone notifications, when the instance has a topic
        DevHooks();
    }

    /// <summary>The header's ⟳ menu: restart, or update from the repo and restart.</summary>
    public RestartViewModel Restart { get; }

    /// <summary>The header's usage bar and timer (WOR-0069).</summary>
    public UsageViewModel Usage { get; } = new();

    /// <summary>The header's Optimise tokens toggle (WOR-0159): saved, shown, and every agent open now switches
    /// (from its next turn); null when saved, else why it wasn't.</summary>
    public string? SetOptimiseTokens(bool on)
    {
        var why = TokenSaver.Set(on);
        if (Usage.OptimiseTokens != on) Usage.OptimiseTokens = on; // comes back here, which does no harm
        foreach (var a in Projects.SelectMany(p => p.Agents?.Agents ?? []).Concat(Mascot.Live()).Distinct().ToList()) a.ApplyTokenSaver();
        return why;
    }

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
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProjectViewModel.StatusLine) && vm == Selected) OnPropertyChanged(nameof(FooterLine)); };
        if (vm.Agents is { } agents)
        {
            agents.Notify += (agent, what, detail) => Tell(vm, agent, what, detail);
            agents.AsksSettled += agent => Settle(vm.Config.Id, agent.Id);
            // WOR-0124: a card waiting on a decision notifies as a question does; clicking opens the card
            vm.CardNotify += (card, title, decision) => TellCard(vm, card, title, decision);
            vm.CardSettled += card => { Settle(vm.Config.Id, CardPrefix + card); PushSettled(vm, card); };
            agents.Mascot = Mascot; // null while the app starts; set just after Bench is made
        }
        if (vm.Notes is { } notes) notes.Organise = (shown, text) => Mascot?.Ask(shown, text) ?? false;
        if (vm.Docs is { } docs) docs.OpenElsewhere = full => ShowDocElsewhere(vm, full);
        WatchNav(vm);
        return vm;
    }

    /// <summary>A link to a markdown file clicked where nothing else handles links (an agent's transcript, Fettle; WOR-0192):
    /// it opens in a project's Docs section. A relative path is looked for from the repos and docs folder of the project
    /// on screen, then the others'; the first that exists wins. A file inside another project's docs opens in that
    /// project, else in the project on screen as outside its docs folder. A missing file still switches to Docs,
    /// whose status line says so. False when the link can't be a file path.</summary>
    public bool OpenDocLink(string link)
    {
        link = link.Trim().Trim('<', '>');
        string path;
        try
        {
            path = link.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(link).LocalPath : Uri.UnescapeDataString(link.Split('#', '?')[0]);
        }
        catch (UriFormatException) { return false; }
        path = path.Replace('\\', '/');
        var order = Projects.Where(p => p == Selected).Concat(Projects.Where(p => p != Selected)).Where(p => p.Docs is not null).ToList();
        if (order.Count == 0) return false;
        string? full = null;
        if (Path.IsPathRooted(path) && path.Length > 1 && (path[1] == ':' || path[0] == '/')) full = Path.GetFullPath(path);
        else
        {
            foreach (var p in order)
            {
                var bases = p.Config.ExistingRepos().Append(p.Docs!.Folder).Append(Path.GetDirectoryName(Path.GetFullPath(p.Docs.Folder)) ?? "");
                full = bases.Where(b => b.Length > 0).Select(b => Path.GetFullPath(Path.Combine(b, path.TrimStart('/')))).FirstOrDefault(File.Exists);
                if (full is not null) break;
            }
            full ??= Path.GetFullPath(Path.Combine(order[0].Config.ExistingRepos().FirstOrDefault() ?? order[0].Docs!.Folder, path.TrimStart('/')));
        }
        Setup = null; ToolsPage = null; SettingsPage = null;
        // the project whose docs hold it (the deepest folder wins), else the one on screen
        var owner = order.Select(p => (p, rel: Path.GetRelativePath(Path.GetFullPath(p.Docs!.Folder), full)))
            .Where(x => x.rel != ".." && !x.rel.StartsWith(".." + Path.DirectorySeparatorChar) && !x.rel.StartsWith("../") && !Path.IsPathRooted(x.rel))
            .OrderByDescending(x => x.p.Docs!.Folder.Length).Select(x => x.p).FirstOrDefault() ?? order[0];
        Selected = owner;
        owner.OpenCommand.Execute(nameof(Section.Docs));
        owner.Docs!.Show(full);
        return true;
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
                if (p.Board.Editor is { IsNew: false } card) s.Append($", card {card.Heading} ({card.Title}) open, in column {card.Status}" + (card.FilePath is { } cf ? $", file {cf}" : ""));
                break;
            case Section.Notes when p.Notes is not null:
                s.Append($", notes wall in {p.Notes.Folder}");
                break;
            case Section.Docs when p.Docs is not null:
                s.Append($", docs folder {p.Docs.Folder}");
                if (p.Docs.Selected is { } doc) s.Append($", doc {doc.Path} open");
                break;
            case Section.Design when p.Design is not null:
                s.Append($", plans folder {p.Design.Folder}");
                if (p.Design.Page is { } plan) s.Append($", plan \"{plan.Title}\" open ({plan.File})");
                break;
            case Section.Code when p.Code is not null:
                s.Append($", repos {string.Join(", ", p.Config.Repos)}");
                if (p.Code.Current is { } f) s.Append($", file {f.FullPath} open");
                break;
            case Section.Agents when p.Agents?.Shown is { } a:
                s.Append($", agent \"{a.Name}\" (id {a.Id}{(a.CardId is { } c ? $", on card {c}" : "")}, {a.StateText}), its folder {a.Cwd}"); // "working in" read as its state
                break;
            case Section.Reports when p.Reports is not null:
                s.Append($", reports folder {p.Reports.Folder}" + (p.Reports.Selected is { } r ? $", report \"{r.Heading}\" ({r.Report.File})" : ""));
                break;
            case Section.Mistakes when p.Mistakes is not null:
                s.Append($", mistake log {p.Mistakes.File}");
                break;
            case Section.Context:
                s.Append($", context page (CLAUDE.md files and memory for {p.Config.WorkingFolder()})");
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
        AgentStore.Flush(); // saves are written in the background: all of them before the app goes
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
    /// <summary>Empty the list and go back to the chat: an empty Notifications tab has nothing to show (WOR-0090).</summary>
    [RelayCommand] void ClearNotifications() { Notifications.Clear(); ShowNotifications = false; }
    [RelayCommand] void OpenNotification(NotificationEntry? n) { if (n is not null) OpenAgent(n.ProjectId, n.AgentId); }

    /// <summary>An agent's questions and permission asks are all answered: their entries leave the list (WOR-0094).</summary>
    void Settle(string projectId, string agentId)
    {
        void Remove()
        {
            for (var i = Notifications.Count - 1; i >= 0; i--)
                if (Notifications[i] is { } n && n.ProjectId == projectId && n.AgentId == agentId && n.IsAsk) Notifications.RemoveAt(i);
            if (Notifications.Count == 0) ShowNotifications = false;
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Remove(); else Avalonia.Threading.Dispatcher.UIThread.Post(Remove);
    }

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
        using var timing = StallWatch.Shared.Section("Notifier.Show");
        if (looking) return;
        Notifier.Show(project.Config.Id, agent.Id, NotificationEntry.HeaderFor(project.Name, agent.Name, what), project.Name, detail, NotificationEntry.Asks(what));
        PushTell($"{project.Name}: " + NotificationEntry.HeaderFor(project.Name, agent.Name, what), what, detail);
    }

    /// <summary>A notification's "agent" for a card waiting on a decision (WOR-0124): opening it opens the card.</summary>
    public const string CardPrefix = "card:";

    /// <summary>A card is waiting on my decision (WOR-0124), unless I'm looking at it already.</summary>
    void TellCard(ProjectViewModel project, string card, string title, CardDecision decision)
    {
        var question = decision.Question;
        Log(project.Config.Id, CardPrefix + card, project.Name, $"{card} {title}", NotificationEntry.DecisionWaits, question);
        var looking = WindowActive && Selected == project && project.OnBoard && project.Board?.Editor?.CardId == card;
        if (looking) return;
        Notifier.Show(project.Config.Id, CardPrefix + card, NotificationEntry.HeaderFor(project.Name, $"{card} {title}", NotificationEntry.DecisionWaits), project.Name, question, needsYou: true);
        PushDecision(project, card, title, decision); // AND-0006: its options as buttons on the phone
    }

    /// <summary>Open a project's agent: its tab, the agents section, that agent, the window to the front.</summary>
    public void OpenAgent(string projectId, string agentId)
    {
        if (agentId.StartsWith(CardPrefix, StringComparison.Ordinal))
        {
            if (Projects.FirstOrDefault(p => p.Config.Id == projectId) is { Board: { } board } owner)
            {
                Selected = owner;
                if (board.OpenById(agentId[CardPrefix.Length..])) owner.OpenCommand.Execute(nameof(Section.Board));
            }
            BringToFront?.Invoke();
            return;
        }
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

    /// <summary>What moved into .darg at start (WOR-0144), said in the status line until another project is picked.</summary>
    [ObservableProperty] public partial string Moved { get; set; } = "";

    /// <summary>The window's bottom line: what moved at start, else the project's.</summary>
    public string FooterLine => Moved.Length > 0 ? Moved : Selected?.StatusLine ?? "";

    partial void OnMovedChanged(string value) => OnPropertyChanged(nameof(FooterLine));

    partial void OnSelectedChanged(ProjectViewModel? value)
    {
        if (_movedShown) Moved = ""; // the first pick is the project opened at start; the next one clears the note
        _movedShown = true;
        OnPropertyChanged(nameof(FooterLine));
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
    /// <summary>The agent's name leads, so headers differ at a glance; the project (or Fettle) follows in Sub.</summary>
    public static string HeaderFor(string project, string agent, string what) =>
        string.IsNullOrWhiteSpace(agent) ? $"{project}: {what}" : $"{agent}: {what}";
    /// <summary>A question or permission ask, which goes stale once answered.</summary>
    public bool IsAsk => Asks(What);
    /// <summary>Whether a notification saying this waits on my answer: a question, a permission ask or a card's decision.
    /// The list and the toast mark those (WOR-0163).</summary>
    public static bool Asks(string what) => what == "has a question" || what == DecisionWaits || what.StartsWith("wants to use ", StringComparison.Ordinal);
    /// <summary>What a card waiting on a decision says (WOR-0124).</summary>
    public const string DecisionWaits = "needs your decision";
    public string Title => HeaderFor(Project, Agent, What);
    public string Sub => $"{Project} · {At:HH:mm}";
}

public enum Section { Home, Board, Notes, Docs, Design, Agents, Flows, Reports, Artifacts, Mistakes, Context, Code, GitHub, Preview, Terminal }

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
    /// <summary>The project's plans in docs/design/, as a tree and a page each (WOR-0175; there with a docs folder).</summary>
    public DesignViewModel? Design { get; }
    public CodeViewModel? Code { get; }
    public GitHubViewModel? Hub { get; }
    public PreviewViewModel? Preview { get; }
    public AgentsViewModel? Agents { get; }
    public ConsolesViewModel? Consoles { get; }
    /// <summary>Pages the project's agents made (there with the agents feature).</summary>
    public ArtifactsViewModel? Artifacts { get; }
    /// <summary>The project's mistake log, read and counted (there with the agents feature, which writes it).</summary>
    public MistakesViewModel? Mistakes { get; }
    /// <summary>The instructions, memory and added context agents start with, to read (WOR-0145; every project has it).</summary>
    public ContextViewModel Context { get; }
    /// <summary>Where a project opens: its README and what's going on (every project has one).</summary>
    public HomeViewModel Home { get; }
    /// <summary>The project's card queues (WOR-0212; there with a board and agents).</summary>
    public QueuesViewModel? Queues { get; }
    /// <summary>What card agents filed when they finished (there with the agents feature).</summary>
    public ReportsViewModel? Reports { get; }
    public bool UsesTerminal => Consoles is not null;
    /// <summary>A card is waiting on my decision (WOR-0124): its id, title and decision. Notifies as an agent's question does.</summary>
    public event Action<string, string, CardDecision>? CardNotify;
    /// <summary>A card's decision was answered (WOR-0124): its notification goes from the list.</summary>
    public event Action<string>? CardSettled;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OnHome), nameof(OnBoard), nameof(OnNotes), nameof(OnDocs), nameof(OnDesign), nameof(OnAgents), nameof(OnFlows), nameof(OnReports), nameof(OnArtifacts), nameof(OnMistakes), nameof(OnContext), nameof(OnCode), nameof(OnHub), nameof(OnPreview), nameof(OnTerminal))]
    public partial Section Section { get; set; }
    /// <summary>The terminal starts the first time its section is opened, not with the app.</summary>
    [ObservableProperty] public partial bool TerminalStarted { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    /// <summary>The footer's line: the project, its folder and the board on screen (all boards on the All board, WOR-0214).</summary>
    public string StatusLine => $"{Name} · {Config.WorkingFolder()}" + (Board switch
    {
        { ShowingAll: true } => " · all boards",
        { SelectedBoard: { } b } => $" · board {b}",
        _ => "",
    });

    public bool OnHome => Section == Section.Home;
    public bool OnBoard => Section == Section.Board;
    public bool OnNotes => Section == Section.Notes;
    public bool OnDocs => Section == Section.Docs;
    public bool OnDesign => Section == Section.Design;
    public bool OnCode => Section == Section.Code;
    public bool OnHub => Section == Section.GitHub;
    public bool OnPreview => Section == Section.Preview;
    public bool OnAgents => Section == Section.Agents;
    public bool OnFlows => Section == Section.Flows;
    public bool OnArtifacts => Section == Section.Artifacts;
    public bool OnMistakes => Section == Section.Mistakes;
    public bool OnContext => Section == Section.Context;
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
        Context = new ContextViewModel(config);
        if (config.Uses(Features.Board))
        {
            Board = new BoardViewModel(config);
            // the footer names the board on screen, so it follows the pick and the All board (WOR-0214)
            Board.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(BoardViewModel.SelectedBoard) or nameof(BoardViewModel.ShowingAll)) OnPropertyChanged(nameof(StatusLine));
            };
        }
        if (config.Uses(Features.Agents))
        {
            Agents = new AgentsViewModel(config);
            Agents.ShowAgents = () => Section = Section.Agents;
            Artifacts = new ArtifactsViewModel(config.Id, id => Agents.Find(id)?.Name);
            Mistakes = new MistakesViewModel(MistakeLog.PathFor(config.Id), config.Name);
            Reports = new ReportsViewModel(ReportStore.FolderFor(config.Id));
            // an agent that filed a report goes once its turn ends well: the report is what's kept of it
            Reports.Loaded += Agents.Reported;
            Agents.Reported(ReportStore.List(Reports.Folder));
        }
        if (config.Uses(Features.Terminal)) Consoles = new ConsolesViewModel(config);
        if (config.Uses(Features.Docs) && config.Docs is { Length: > 0 } docs) Docs = new DocsViewModel(docs);
        if (PlanStore.FolderFor(config) is { } plans)
            Design = new DesignViewModel(config, plans)
            {
                AgentFor = id => Agents?.ForCard(id),
                ReportsFolder = Reports?.Folder,
                OpenReport = file => { if (Reports is null) return; Reports.Show(file); Section = Section.Reports; },
                OpenCard = id => { if (Board?.OpenById(id) != true) return false; Section = Section.Board; return true; },
                SendBack = (id, notes) => Board?.SendBackById(id, notes),
                BoardsChanged = () => Board?.ReloadAll(),
                OpenBoard = name => { if (Board is null) return; if (Board.Boards.Contains(name)) Board.SelectedBoard = name; Section = Section.Board; },
            };
        // the planner beside the page (WOR-0176): + Plan starts one, the chat talks to the plan's own
        if (Design is not null && Agents is not null)
        {
            Design.StartPlanner = (idea, kind) => StartPlan(idea, kind).Agent;
            Design.PlannerFor = Agents.PlannerFor;
            Design.StartReviser = StartReviser;
            Design.AdoptPlanner = AdoptPlanner;
            Design.StartFeaturePlanner = (idea, parent) => StartPlan(idea, PlanKind.Feature, parent, show: false);
        }
        if (config.Uses(Features.Notes) && config.NotesFolder() is { } notes)
            Notes = new NotesViewModel(notes, config.Boards)
            {
                // tickets land on the inbox board, and Haiku moves each to its story's board (WOR-0087)
                TicketBoard = () => Board?.InboxFolder,
                Ticketed = (board, id, retitled) => Board?.PlaceLater(board, id, retitled),
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
            Board.QaRunning = Agents.QaRunningFor;
            Agents.CardAgentChanged += _ => Board.StatesChanged();
            // a card agent shows its card's state: "Complete, pending removal" once the card is done
            Agents.DoneCardIds = Board.DoneCardIds;
            Agents.CardBoards = Board.CardBoards;
            Board.Reloaded += Agents.RefreshCards;
            // a plan's made cards show their column and agent as the board has them
            Board.Reloaded += () => { if (OnDesign) Design?.Reload(); };
            Reports!.StoryMap = Board.CardBoards;
            Reports.Reload();
            Reports!.OpenCard = id => { if (!Board.OpenById(id)) return false; Section = Section.Board; return true; };
            Agents.OpenCard = Reports.OpenCard;
            // a work or rework agent cut off mid-turn when Darg last closed carries on if its card is still being worked (WOR-0221)
            Agents.CardStillWorked = id => CutOffTurn.StillWorked(config.Boards, id);
            Agents.RefreshCards();
            // a card landing in Needs Attention gets its decision report, whoever moved it (WOR-0092)
            // WOR-0102: a card entering QA gets a QA report, one reaching done has it archived and a done report written
            // (ReportFlow, where the rules and their tests are); this only says which agent is at work on the card.
            // a card agent at work wins over a QA agent: it is the one that writes its own report
            string? AtWork(Card card) => Agents.Agents.Where(a => a.CardId == card.Id && a.State is AgentState.Starting or AgentState.Working)
                .Select(a => a.Purpose ?? "").OrderBy(p => p == AgentPurposes.Qa).Cast<string?>().FirstOrDefault();
            Task InBackground(Action write) => Task.Run(() => { try { write(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } });
            Board.NeedsAttention = (card, file) =>
            {
                var folder = Reports!.Folder; var repo = CardFolder(card);
                InBackground(() => ReportFlow.EnteredAttention(folder, card, file, repo, DateTime.Now));
                DecisionOnLanding(card, file);
            };
            // WOR-0124: a card in For Human Attention waits on a decision. Once the turn of the agent that moved it has
            // ended (it may write the section just after the move), the decision tells me; with none, that agent is
            // asked for one, and if it gives none (or there's no agent to ask) Haiku drafts one from the card.
            void DecisionOnLanding(Card card, string file)
            {
                var mover = Agents.Agents.Where(a => a.CardId == card.Id).MaxBy(a => a.Created);
                // a task agent goes once it stops (WOR-0183); this one stays until it's known whether it has to be asked
                if (mover is not null) Agents.Hold(mover);
                AfterTurn(mover, () => Check(nudged: false));
                void Check(bool nudged)
                {
                    var asking = false;
                    try { asking = Ask(nudged); }
                    finally { if (!asking) Agents.Release(mover); }
                }
                // true: the mover has been asked for the decision, and is held until that turn ends
                bool Ask(bool nudged)
                {
                    if (!Board.CardInAttention(card.Id)) return false; // answered or moved on meanwhile
                    var column = AttentionColumn.Find(Board.Columns) ?? BoardStore.AttentionName;
                    string text;
                    try { text = File.ReadAllText(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
                    if (CardDecision.Read(text) is { Valid: true } d) { DecisionWaits(card, d); return false; }
                    if (!nudged && mover is not null && Agents.Find(mover.Id) is not null && mover.SessionId is not null)
                    {
                        mover.Say($"Darg: {card.Id} landed in {column} with no decision. Asked for one.", CardDecision.Nudge(card, file, column));
                        AfterTurn(mover, () => Check(nudged: true));
                        return true;
                    }
                    _ = Task.Run(async () =>
                    {
                        var draft = await CardDecision.Draft(card, text, column);
                        if (draft is null) return;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (!Board.CardInAttention(card.Id) || CardDecision.Read(File.ReadAllText(file)) is { Valid: true }) return;
                                draft.WriteTo(file);
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
                            DecisionWaits(card, draft);
                        });
                    });
                    return false;
                }
            }
            // after the agent's turn (and any question it is waiting on) has ended; at once when there is none
            void AfterTurn(AgentViewModel? agent, Action go)
            {
                static bool Busy(AgentViewModel a) => a.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou;
                if (agent is null || !Busy(agent)) { Avalonia.Threading.Dispatcher.UIThread.Post(go); return; }
                System.ComponentModel.PropertyChangedEventHandler? ended = null;
                ended = (_, e) =>
                {
                    if (e.PropertyName != nameof(AgentViewModel.State) || Busy(agent)) return;
                    agent.PropertyChanged -= ended;
                    Avalonia.Threading.Dispatcher.UIThread.Post(go);
                };
                agent.PropertyChanged += ended;
            }
            void DecisionWaits(Card card, CardDecision d) => CardNotify?.Invoke(card.Id, card.Title, d);
            // WOR-0124: an answer with no done/back/stay goes to the agent that asked if it's still listed, else to a new card agent
            Board.DecisionToAgent = (card, file, decision, answer, notes) =>
            {
                var asker = decision.AgentId is { } aid ? Agents.Find(aid) : null;
                asker ??= Agents.Agents.Where(a => a.CardId == card.Id && a.SessionId is not null).MaxBy(a => a.Created);
                if (asker is not null && asker.SessionId is not null)
                {
                    // one mid-turn gets it when the turn ends, as a message typed then would
                    var message = CardDecision.AnswerMessage(card, decision, answer, notes);
                    AfterTurn(asker, () => asker.Say(message, message));
                    return;
                }
                var id = AgentsViewModel.NewId();
                var prompt = CardDecision.AnswerPreface(decision, answer, notes) + Environment.NewLine + Environment.NewLine + CardPrompt(card, file, [], id);
                Watch(Agents.Start(prompt, CardFolder(card), cardId: card.Id, name: $"{card.Id} {card.Title} (answered)", job: CardJob(card), id: id, purpose: AgentPurposes.Work));
            };
            Board.DecisionSettled = (card, decision, notes) =>
            {
                Reports!.SettledFromBoard(card, decision, notes);
                CardSettled?.Invoke(card);
                // WOR-0169: and on the agent that showed it
                var how = decision == DecisionReport.Approved ? "approved, moved to done" : decision == DecisionReport.SentBack ? "sent back" : "answered";
                foreach (var a in Agents.Agents) a.CloseDecision(card, $"Settled on the card: {how}");
            };
            // WOR-0169: a card's open decision shows on the agent responsible for it (the one that asked, else the
            // card's newest), linked: picking there answers the card through Decide, and answering the card, or the
            // card leaving the attention column, closes it there. Kept in step on every board read, which a card
            // file changing triggers, and when an agent joins the list (a restored one included).
            AgentViewModel? DecisionAgent(Card card, CardDecision d) =>
                (d.AgentId is { } aid ? Agents.Find(aid) : null)
                ?? Agents.Agents.Where(a => a.CardId == card.Id).MaxBy(a => a.Created);
            void SyncDecisions()
            {
                var owners = Board.OpenDecisions().Select(x => (x.Card, x.Decision, Agent: DecisionAgent(x.Card, x.Decision))).ToList();
                foreach (var a in Agents.Agents.ToList())
                    foreach (var item in a.OpenDecisions.ToList())
                    {
                        if (owners.Any(o => o.Card.Id == item.CardId && o.Agent == a)) continue;
                        if (owners.Any(o => o.Card.Id == item.CardId)) { item.Close("Now shown on the card's newer agent"); continue; }
                        // posted: when Decide moved the card, this read runs inside it, and DecisionSettled, just after,
                        // closes it first saying how (approved, sent back); this is for a card moved or edited by hand
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => item.Close("Settled on the card: it has no open decision now"));
                    }
                foreach (var (card, d, agent) in owners) agent?.ShowDecision(card.Id, d, Board.Decide);
            }
            Board.Reloaded += SyncDecisions;
            Agents.Agents.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    Avalonia.Threading.Dispatcher.UIThread.Post(SyncDecisions);
            };
            SyncDecisions();
            Board.EnteredQa = (card, file) =>
            {
                QaAfterRework(card, file);
                var folder = Reports!.Folder; var repo = CardFolder(card); var agent = AtWork(card);
                InBackground(() => ReportFlow.EnteredQa(folder, card, file, repo, DateTime.Now, agent));
            };
            Board.EnteredDone = (card, file) =>
            {
                var folder = Reports!.Folder; var repo = CardFolder(card); var agent = AtWork(card);
                // then (WOR-0131) the card's agents close and go: settled ones now, ones mid-turn once their turn ends well
                InBackground(() => ReportFlow.EnteredDone(folder, card, file, repo, DateTime.Now, agent))
                    .ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => Agents.CardFinished(card.Id)), TaskScheduler.Default);
            };
            Board.ReportFor = id => Reports!.LatestFor(id);
            Board.OpenReport = file => { Reports!.Show(file); Section = Section.Reports; };
            Reports!.ApproveCard = Board.Approve;
            Reports.CleanUpDone = () =>
            {
                var (archived, written) = DecisionReport.CleanUp(Reports.Folder, Board.DoneCards(), CardFolder, DateTime.Now);
                // and the agents left on done cards close and go (WOR-0131), after their reports are in
                var forgotten = Agents.ClearDoneCards(Board.DoneCards(completedToo: true).Select(d => d.Card.Id));
                return archived == 0 && written == 0 && forgotten == 0 ? "Nothing to clean up: no done card has a QA report left in the list, a missing done report or an agent."
                    : $"Cleaned up: {archived} QA {(archived == 1 ? "report" : "reports")} archived, {written} done {(written == 1 ? "report" : "reports")} written, " +
                      $"{forgotten} {(forgotten == 1 ? "agent" : "agents")} on done cards forgotten.";
            };
            Board.Approved = Reports.ApprovedFromBoard;
            Reports.SendCardBack = Board.SendBackById;
            // a card looks up its agent only when the board rebuilds: forgetting the agent frees the card now
            Agents.Forgotten += a => { if (a.CardId is { } card) Board.AgentGone(card); };
            Board.OpenAgent = a => { Agents.Selected = a; Section = Section.Agents; };
            Board.StartAgent = (prompt, cwd, name, dirs) => { Agents.Start(prompt, cwd, name: name, dirs: dirs); Section = Section.Agents; };
            string CardPrompt(Card card, string file, IReadOnlyList<string> alongside, string? agentId = null)
            {
                using var timing = StallWatch.Shared.Section("CardPrompt");
                // everyone at work in this folder: the batch this card was started with, and agents busy here now
                var folder = CardFolder(card);
                var busy = Agents.Agents
                    .Where(x => x.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou && SameFolder(x.Cwd, folder))
                    .Select(x => x.CardId ?? $"\"{x.Name}\"");
                var others = alongside.Concat(busy).Where(x => x != card.Id).Distinct().ToList();
                var repo = Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")) ? folder : null;
                var commits = Core.Commits.For(Config, folder) is { } rule ? Core.Commits.Describe(rule, folder) : null;
                // done and a report when it's sure, the board's QA column when I should look (WOR-0061)
                var finish = ReportStore.Rules(Reports!.Folder, card, BoardColumns.Qa(Board.Columns), agentId, AttentionColumn.Find(Board.Columns));
                // what it waits on, where that is and its newest report, so it builds on their work (WOR-0173)
                var reports = Reports.Folder;
                var blockers = card.BlockedBy.Count == 0 ? [] : BlockedStart.PromptLines(
                    Blockers.Of(card, Blockers.Index(CardIndex.Read(Config.Boards))),
                    id => CardIndex.ReportsFor(reports, id).FirstOrDefault()?.File is { } f ? Path.GetFullPath(f).Replace('\\', '/') : null);
                // the plan it was made from, and the rule for a choice the plan didn't settle (WOR-0178)
                var plan = PlanStore.FolderFor(Config) is { } pf && PlanRules.PlanOf(PlanStore.List(pf), card.Id) is { } pd ? PlanRules.Prompt(pd.File, card.Id) : null;
                return AgentsViewModel.CardPrompt(Name, card, file, others, repo, commits, finish, blockers, plan);
            }
            // what picks a card agent's model profile: the card's words, type and labels, not the house rules around them
            static AgentJob CardJob(Card card) => new(JobKinds.Card, $"{card.Title}\n\n{card.Description}".Trim(), card.Type, card.Labels);
            // the card shows the agent's state; when the agent ends, the card drops its link
            void Watch(AgentViewModel a) =>
                a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AgentViewModel.State) && a.State == AgentState.Exited) Board.ReloadAll(); };
            Board.WorkOn = (card, file, alongside, show) =>
            {
                using var timing = StallWatch.Shared.Section("Board.WorkOn");
                var id = AgentsViewModel.NewId();
                // a card in QA: Work on gets the result running for me to try (WOR-0075), not more work
                var inQa = Board.InQa(card);
                // a card QA sent to needs more work: its agent is reworking it, so its return to QA runs QA (WOR-0115)
                var purpose = inQa ? AgentPurposes.Qa : Board.InRework(card) ? AgentPurposes.Rework : AgentPurposes.Work;
                Watch(Agents.Start(inQa ? ReportStore.CheckPrompt(Name, card, file) : CardPrompt(card, file, alongside, id), CardFolder(card), cardId: card.Id, name: $"{card.Id} {card.Title}", job: CardJob(card), id: id, purpose: purpose));
                if (show) Section = Section.Agents;
            };
            // Run QA (WOR-0108): an agent checks the card from its How to check section; the board stays showing
            Board.RunQa = (card, file) =>
            {
                if (Agents.QaRunningFor(card.Id)) return;
                var id = AgentsViewModel.NewId();
                Watch(Agents.Start(ReportStore.QaRunPrompt(Name, card, file, Board.Columns.LastOrDefault()?.Name, AttentionColumn.Find(Board.Columns), BoardColumns.Rework(Board.Columns)), CardFolder(card), cardId: card.Id, name: $"{card.Id} {card.Title} (QA)", job: CardJob(card), id: id, purpose: AgentPurposes.Qa));
                // the button comes back once the QA agent's turn ends: the board reads its state again
                if (Agents.Find(id) is { } qa)
                    qa.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AgentViewModel.State)) Board.ReloadAll(); };
                Board.ReloadAll(); // the button goes at once, not when the agent's state next changes
            };
            // WOR-0115: a card back in QA after rework (sent back from QA, or Work on in the needs-more-work column) gets
            // Run QA without asking. Only once the reworking agent's turn has ended: it may still be merging or writing its
            // report, and QA should check what landed. Not if the card has left QA meanwhile or a QA agent is already on it.
            void QaAfterRework(Card card, string file)
            {
                var last = Agents.Agents.Where(a => a.CardId == card.Id).MaxBy(a => a.Created);
                if (last is not { Purpose: AgentPurposes.Rework }) return;
                static bool Busy(AgentViewModel a) => a.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou;
                void Go() { if (Board.CardInQa(card.Id) && !Agents.QaRunningFor(card.Id)) Board.RunQa?.Invoke(card, file); }
                if (!Busy(last)) { Go(); return; }
                System.ComponentModel.PropertyChangedEventHandler? ended = null;
                ended = (_, e) =>
                {
                    if (e.PropertyName != nameof(AgentViewModel.State) || Busy(last)) return;
                    last.PropertyChanged -= ended;
                    // after this change has been handled, as Forget is: the board may still be reading the agent's last move
                    Avalonia.Threading.Dispatcher.UIThread.Post(Go);
                };
                last.PropertyChanged += ended;
            }
            // sent back from QA with notes (WOR-0079): the usual card prompt, after the notes and how to build on the last round
            Board.Rework = (card, file, notes) =>
            {
                var id = AgentsViewModel.NewId();
                var prompt = ReportStore.ReworkPreface(notes) + Environment.NewLine + Environment.NewLine + CardPrompt(card, file, [], id);
                Watch(Agents.Start(prompt, CardFolder(card), cardId: card.Id, name: $"{card.Id} {card.Title} (sent back)", job: CardJob(card), id: id, purpose: AgentPurposes.Rework));
                // stays on the board (WOR-0129): the card just moves to In Progress
            };
            // tied to the card (WOR-0155): the card shows the agent at work and is locked (no second Improve, no Work on)
            // until its turn ends. The board stays on screen (WOR-0103): improving is a background chore, and a question
            // it asks reaches me as a notification like any agent's.
            Board.Improve = (card, file) =>
            {
                var folder = Path.GetDirectoryName(file)!;
                var commits = Core.Commits.For(Config, folder) is { } rule ? Core.Commits.Describe(rule, folder) : null;
                var prompt = CardImprover.Prompt(Name, card, file, CardImprover.NoteFile(Config.NotesFolder(), card.Description), commits);
                var improver = Agents.Start(prompt, CardFolder(card), cardId: card.Id, name: $"Improve {card.Id} {card.Title}", job: CardJob(card), purpose: AgentPurposes.Improve);
                // the lock lifts when its turn ends: the board reads its state again
                improver.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AgentViewModel.State)) Board.ReloadAll(); };
                Board.ReloadAll(); // the lock shows at once, not when the agent's state next changes
            };
            // WOR-0212: named queues of cards, worked one at a time; the next starts as the one before finishes
            Queues = new QueuesViewModel(config, Agents)
            {
                Start = Board.StartQueued, Changed = Board.ReloadAll,
                // WOR-0220: a queue's lane draws its cards' own tiles, and a card on a board not showing opens by id
                TileFor = Board.LaneTile, OpenCard = id => Board.OpenById(id),
            };
            Board.Queues = Queues;
            Board.Reloaded += Queues.Look;
            Board.Flows = () => Agents.Flows;
            Board.RunFlow = (card, file, flow) =>
            {
                Agents.ReloadFlows(); // its file may have been edited since the board last looked
                var current = Agents.Flows.FirstOrDefault(f => f.Name == flow.Name) ?? flow;
                var run = Agents.RunFlow(current, CardPrompt(card, file, []), $"{card.Id} {card.Title}", cwd: CardFolder(card), cardId: card.Id, job: CardJob(card));
                // each stage is a new agent: the card follows the one at work
                run.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowRun.Status)) Board.ReloadAll(); };
                Section = Section.Agents;
            };
            // the boards were read before their cards' agents could be looked up
            Board.ReloadAll();
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
        if (value == Section.Context) Context.Reload();
        if (value == Section.Design) Design?.Reload();
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
        Section.Home => true, Section.Context => true, Section.Board => Board is not null, Section.Notes => Notes is not null, Section.Docs => Docs is not null, Section.Design => Design is not null, Section.Agents => Agents is not null, Section.Flows => Agents is not null, Section.Artifacts => Artifacts is not null, Section.Mistakes => Mistakes is not null, Section.Reports => Reports is not null,
        Section.Code => Code is not null, Section.GitHub => Hub is not null, Section.Preview => Preview is not null, _ => Consoles is not null,
    };

    /// <summary>The project is leaving the app (removed or changed in Setup): stop its agents and its watcher.</summary>
    public void Close()
    {
        Agents?.Close();
        Board?.Dispose();
        Mistakes?.Dispose();
        Reports?.Dispose();
        Preview?.Close();
    }
}
