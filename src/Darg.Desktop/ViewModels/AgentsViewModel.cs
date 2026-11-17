using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

public partial class AgentsViewModel : ObservableObject
{
    readonly ProjectConfig project;
    readonly string _store;
    readonly string _reports;

    /// <summary>A project's agents, with the ones saved by earlier runs listed (newest first) to carry on.</summary>
    public AgentsViewModel(ProjectConfig project)
    {
        this.project = project;
        _store = AgentStore.Folder(project.Id.Length > 0 ? project.Id : "default");
        _reports = ReportStore.FolderFor(project.Id);
        SelectedRepo = RepoChoices.FirstOrDefault();
        Agents.CollectionChanged += (_, _) => Resort();
        foreach (var r in AgentStore.List(_store)) Add(new AgentViewModel(r, _store), select: false);
        Selected = Agents.FirstOrDefault();
        ReloadFlows();
        // older agents were named by cutting off their first message: give those a proper title
        foreach (var a in Agents.Where(a => a.CardId is null && a.Name.EndsWith('…')).ToList())
            if (a.Items.OfType<UserItem>().FirstOrDefault()?.Text is { } first) _ = Retitle(a, first);
    }

    /// <summary>Give an agent a short title (Haiku, else a guess from the words) and save it.</summary>
    static async Task Retitle(AgentViewModel a, string firstMessage)
    {
        a.Name = await Task.Run(() => ChatTitle.Make(firstMessage)); // Haiku's process starts off the UI thread
        a.Save();
    }

    public ObservableCollection<AgentViewModel> Agents { get; } = [];

    /// <summary>The agents as the list shows them: <see cref="Agents"/> in its own order, or grouped by status.</summary>
    public ObservableCollection<AgentViewModel> Listed { get; } = [];

    /// <summary>Sort the list by status: waiting on you, then working, failed, done, saved. Otherwise newest first.</summary>
    [ObservableProperty] public partial bool SortByStatus { get; set; }
    partial void OnSortByStatusChanged(bool value) => Resort();

    static int Rank(AgentViewModel a) => a.State switch
    {
        AgentState.NeedsYou => 0,
        AgentState.Starting or AgentState.Working => 1,
        AgentState.Exited => 2,
        AgentState.Idle => a.TurnFailed ? 2 : 3,
        _ => 4, // saved
    };

    /// <summary>Bring Listed into the wanted order by moving rows, so the selection and scroll stay put.</summary>
    void Resort()
    {
        var want = SortByStatus ? Agents.OrderBy(Rank).ToList() : Agents.ToList(); // OrderBy is stable: ties keep list order
        foreach (var gone in Listed.Where(a => !want.Contains(a)).ToList()) Listed.Remove(gone);
        for (var i = 0; i < want.Count; i++)
        {
            if (i < Listed.Count && Listed[i] == want[i]) continue;
            var at = Listed.IndexOf(want[i]);
            if (at < 0) Listed.Insert(i, want[i]); else Listed.Move(at, i);
        }
    }

    /// <summary>Bench, shared by every project: the chats started in this project are listed here too, under their own heading.</summary>
    [ObservableProperty] public partial MascotViewModel? Mascot { get; set; }
    /// <summary>The Bench chat open in this view (null while a project agent is).</summary>
    [ObservableProperty] public partial ChatEntry? BenchChat { get; set; }

    /// <summary>The agent in the right-hand pane: the project agent picked in the list, or the Bench chat
    /// picked under it. (Kept apart from Selected, which the project list owns: a Bench chat isn't in
    /// that list, so the list would clear it.)</summary>
    [ObservableProperty] public partial AgentViewModel? Shown { get; set; }

    partial void OnBenchChatChanged(ChatEntry? value)
    {
        if (value is null || Mascot is null) return;
        Mascot.SelectedChat = value;
        Selected = null;
        Shown = Mascot.Agent;
    }

    partial void OnSelectedChanged(AgentViewModel? value)
    {
        if (value is null) return;
        BenchChat = null; // picking one of this project's agents leaves the Bench chat
        Shown = value;
    }

    partial void OnShownChanged(AgentViewModel? value)
    {
        if (value is not null) value.Unseen = false;
    }

    /// <summary>Forget an agent: off the list and its saved file gone (Claude Code's own history stays).</summary>
    [RelayCommand]
    void Forget(AgentViewModel? a)
    {
        if (a is null) return;
        a.End();
        AgentStore.Delete(_store, a.Id);
        Agents.Remove(a);
        AnyOutOfUsage = Agents.Any(x => x.CanContinueAfterReset);
        if (Selected == a) Selected = Agents.FirstOrDefault();
        if (Shown == a) Shown = Selected;
        Forgotten?.Invoke(a);
    }

    /// <summary>Forget a Fettle chat from the list under this project's agents (as Fettle's own Forget does).</summary>
    [RelayCommand]
    void ForgetChat(ChatEntry? c)
    {
        if (c is not null) Mascot?.ForgetChat(c.Id);
    }

    partial void OnMascotChanged(MascotViewModel? oldValue, MascotViewModel? newValue)
    {
        if (oldValue is not null) oldValue.ChatForgotten -= LetGo;
        if (newValue is not null) newValue.ChatForgotten += LetGo;
    }

    /// <summary>A Fettle chat was forgotten (from here or Fettle's box): stop showing it.</summary>
    void LetGo(string id)
    {
        if (Shown?.Id != id) return;
        BenchChat = null;
        Shown = null;
        Selected = Agents.FirstOrDefault();
    }

    /// <summary>An agent was forgotten (it's off the list): a card it was on is free again.</summary>
    public event Action<AgentViewModel>? Forgotten;

    /// <summary>Close every agent here that's open but idle (not working, not waiting on the person).</summary>
    [RelayCommand]
    void CloseIdle()
    {
        foreach (var a in Agents.Where(a => a.State == AgentState.Idle).ToList()) a.CloseSession();
    }

    /// <summary>Forget every finished agent here: one whose turn ended well and is waiting, or a saved one whose card is done.
    /// Working, waiting on you and failed agents stay listed. Each goes as the × on it would (its file goes, Claude Code keeps its history).</summary>
    [RelayCommand]
    void ClearFinished()
    {
        foreach (var a in Agents.Where(IsFinished).ToList()) Forget(a);
    }

    /// <summary>Whether the agent has finished its work and is only sitting in the list.</summary>
    public bool IsFinished(AgentViewModel a) =>
        a.IsDone || (a.State == AgentState.Saved && a.CardDone && a.FinishedTurn);

    // ---- Finished agent reports (WOR-0061): an agent that filed a report is forgotten once its turn ends well.

    readonly HashSet<string> _reported = [];

    /// <summary>The reports in the project's folder: every agent that wrote one is forgotten when it's finished, at once if it
    /// is already, else when its turn ends well (one that fails or asks something stays, for the person to see).
    /// A report that doesn't name its agent stands for the agents on its card that were started before it was written.
    /// A QA report (WOR-0088) forgets nobody: its agent stays for the person to talk to about what they find.</summary>
    public void Reported(IEnumerable<AgentReport> reports)
    {
        foreach (var r in reports.Where(r => !r.Qa))
            foreach (var a in Agents.Where(a => Wrote(a, r)).ToList())
            {
                _reported.Add(a.Id);
                if (Finished(a)) Forget(a);
            }
    }

    static bool Wrote(AgentViewModel a, AgentReport r)
    {
        if (r.Agent.Length > 0) return a.Id == r.Agent;
        if (r.Card.Length == 0 || a.CardId != r.Card) return false;
        try { return a.Created <= File.GetCreationTime(r.File); } // not the write time: marking it read rewrites the file
        catch (IOException) { return false; }
    }

    /// <summary>Done with its turn, well: idle and not failed, or saved after a turn that ended normally.</summary>
    static bool Finished(AgentViewModel a) => a.IsDone || (a.State == AgentState.Saved && a.FinishedTurn);

    /// <summary>True while an agent here has run out of usage and isn't yet set to carry on.</summary>
    [ObservableProperty] public partial bool AnyOutOfUsage { get; set; }

    /// <summary>Continue all after reset: every agent here that ran out of usage carries on when its limit resets.</summary>
    [RelayCommand]
    void ContinueAll()
    {
        foreach (var a in Agents.Where(a => a.CanContinueAfterReset).ToList()) a.ContinueAfterReset();
    }

    void Add(AgentViewModel agent, bool select)
    {
        agent.StoreFolder ??= _store;
        agent.MistakeLogFile ??= MistakeLog.PathFor(project.Id);
        agent.ProjectName ??= project.Name;
        agent.CardDone = IsCardDone(agent.CardId);
        agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentViewModel.CanContinueAfterReset)) AnyOutOfUsage = Agents.Any(a => a.CanContinueAfterReset);
            if (e.PropertyName is nameof(AgentViewModel.State) or nameof(AgentViewModel.TurnFailed) && SortByStatus) Resort();
            if (e.PropertyName != nameof(AgentViewModel.State)) return;
            // it filed a report and its turn has now ended well: it goes (after this change has been handled)
            if (_reported.Contains(agent.Id) && Finished(agent)) Dispatcher.UIThread.Post(() => { if (Agents.Contains(agent) && Finished(agent)) Forget(agent); });
            NeedingYou = Agents.Any(a => a.State == AgentState.NeedsYou);
            if (agent.State == AgentState.NeedsYou) AgentNeedsYou?.Invoke(agent);
            // news from an agent that isn't the one open here stays marked in the list until it's looked at
            if (agent != Shown && agent.State is AgentState.NeedsYou or AgentState.Idle or AgentState.Exited) agent.Unseen = true;
        };
        agent.Notify += (what, detail) => Notify?.Invoke(agent, what, detail);
        agent.AsksSettled += () => AsksSettled?.Invoke(agent);
        if (select) { Agents.Insert(0, agent); Selected = agent; }
        else Agents.Add(agent);
    }

    public void SaveAll() { foreach (var a in Agents) a.Save(); }

    /// <summary>Whether a board card is done: set by the project (the board's view), null: no board here.</summary>
    public Func<string, bool>? CardIsDone { get; set; }
    bool IsCardDone(string? cardId) => cardId is not null && CardIsDone?.Invoke(cardId) == true;

    /// <summary>Re-read every card agent's card after the boards change (a card moved to Done shows on its agent).</summary>
    public void RefreshCards()
    {
        using var timing = StallWatch.Shared.Section("Agents.RefreshCards");
        foreach (var a in Agents) if (a.CardId is not null) a.CardDone = IsCardDone(a.CardId);
        // a QA agent that has ended its turn has given its verdict: close it so it doesn't linger on the card
        foreach (var a in Agents.Where(a => a.IsDone && !a.LimitHit && a.ContinueAt is null && a.CardId is not null && a.Purpose == AgentPurposes.Qa).ToList()) a.CloseSession("Darg");
    }
    [ObservableProperty] public partial AgentViewModel? Selected { get; set; }
    [ObservableProperty] public partial string NewPrompt { get; set; } = "";
    /// <summary>What the Flows tab's box holds: the task to run a flow on, or what a new flow is for (WOR-0097).</summary>
    [ObservableProperty] public partial string FlowTask { get; set; } = "";
    /// <summary>Asked to show the Agents section, so a started flow can be watched there.</summary>
    public Action? ShowAgents { get; set; }
    /// <summary>Where a new agent from the box starts: the project's first repo unless another is picked.</summary>
    public string Folder => SelectedRepo?.Path ?? project.WorkingFolder();
    /// <summary>The project's repos, for the box's folder picker (shown when there's more than one).</summary>
    public IReadOnlyList<RepoChoice> RepoChoices => _repoChoices ??= project.ExistingRepos().Select(r => new RepoChoice(r)).ToList();
    IReadOnlyList<RepoChoice>? _repoChoices;
    public bool HasRepoChoice => RepoChoices.Count > 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Folder))] public partial RepoChoice? SelectedRepo { get; set; }
    /// <summary>True while any agent here waits on the person (shown on the project's tab).</summary>
    [ObservableProperty] public partial bool NeedingYou { get; set; }
    public event Action<AgentViewModel>? AgentNeedsYou;
    /// <summary>Something the person needs to hear about (a question, a permission ask, or the end of a long turn;
    /// see <see cref="Decisions"/>): (agent, what happened, detail).</summary>
    public event Action<AgentViewModel, string, string>? Notify;
    public event Action<AgentViewModel>? AsksSettled;
    public AgentViewModel? Find(string id) => Agents.FirstOrDefault(a => a.Id == id);
    /// <summary>The newest agent started on a card that hasn't ended.</summary>
    public AgentViewModel? ForCard(string cardId) =>
        Agents.FirstOrDefault(a => a.CardId == cardId && a.IsLiveOnCard);
    /// <summary>A QA agent (Run QA) is at work on the card, or waiting on the person: one that has finished its turn
    /// doesn't count, so Run QA is offered again (WOR-0110).</summary>
    public bool QaRunningFor(string cardId) =>
        Agents.Any(a => a.CardId == cardId && a.Purpose == AgentPurposes.Qa && a.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou);

    /// <summary>The flows this project can run: the shared ones (the instance's flows folder) and its own, read again on Reload.</summary>
    public ObservableCollection<Flow> Flows { get; } = [];
    [ObservableProperty] public partial Flow? SelectedFlow { get; set; }

    string FlowProject => project.Id.Length > 0 ? project.Id : "default";

    public void ReloadFlows()
    {
        var keep = SelectedFlow?.Name;
        Flows.Clear();
        try { foreach (var f in FlowStore.ForProject(FlowProject)) Flows.Add(f); } catch (Exception) { }
        SelectedFlow = Flows.FirstOrDefault(f => f.Name == keep) ?? Flows.FirstOrDefault();
    }

    /// <summary>The flow generator (WOR-0077): an agent writes a new flow from what's typed in the box (or asks what it's for).
    /// <paramref name="which"/> is the kind and where it goes: "basic project", "art shared" and so on.</summary>
    [RelayCommand]
    void NewFlow(string? which)
    {
        var words = (which ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        GenerateFlow(words.Contains("art") ? FlowKind.Art : FlowKind.Basic, words.Contains("shared"), FlowTask);
        FlowTask = "";
        ShowAgents?.Invoke();
    }

    /// <summary>Start the agent that writes a new flow; the Flow list reads the folders again when its turn ends.</summary>
    public AgentViewModel GenerateFlow(FlowKind kind, bool shared, string description)
    {
        var sharedFolder = FlowStore.Folder();
        var own = FlowStore.ProjectFolder(FlowProject);
        try { Directory.CreateDirectory(shared ? sharedFolder : own); } catch (Exception) { }
        List<string> others = [sharedFolder, .. Directory.Exists(own) ? [own] : Array.Empty<string>()];
        List<ToolConfig> tools = [];
        try { tools = InstanceConfig.Load().ToolsOrDefaults(); } catch (Exception) { }
        var prompt = FlowGenerator.Prompt(project.Name, kind, description, shared ? sharedFolder : own, shared, others, tools);
        var what = (kind == FlowKind.Art ? "art flow" : "flow") + (shared ? " for all projects" : "");
        var a = Start(prompt, Folder, name: description.Trim().Length > 0 ? $"New {what}: {ChatTitle.Guess(description)}" : $"New {what}",
            dirs: others);
        a.TurnEnded += _ => Dispatcher.UIThread.Post(ReloadFlows);
        return a;
    }

    /// <summary>Open the flows folder to add or edit one (each is a markdown file), and read them again: the picked
    /// flow's folder (this project's own, or the shared one).</summary>
    [RelayCommand]
    void EditFlows()
    {
        ReloadFlows();
        var folder = SelectedFlow?.File is { } f ? Path.GetDirectoryName(f) ?? FlowStore.Folder() : FlowStore.Folder();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch (Exception) { }
    }

    /// <summary>Open profiles.md (written with the starter set if there isn't one) to change which model and
    /// effort new agents start on. It's read again for every new agent, so there's nothing to reload.</summary>
    /// <summary>Write out what a new agent here starts with by default (the arguments Darg adds and the
    /// CLAUDE.md files Claude Code loads for the folder) and open it.</summary>
    [RelayCommand]
    void ViewDefaultContext()
    {
        try
        {
            var path = DefaultContext.Write(project, Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    [RelayCommand]
    void EditProfiles()
    {
        ModelProfiles.Load();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ModelProfiles.FilePath()) { UseShellExecute = true }); } catch (Exception) { }
    }

    [RelayCommand]
    void StartFlow()
    {
        var task = FlowTask.Trim();
        if (task.Length == 0 || SelectedFlow is null) return;
        RunFlow(SelectedFlow, task, name: ChatTitle.Guess(task), cwd: Folder);
        FlowTask = "";
        ShowAgents?.Invoke();
    }

    /// <summary>Run a flow on a task: each stage an agent named for the task and its role ("WOR-0023 … · QA"), started when the one
    /// before it hands on.</summary>
    /// <param name="job">The task's job (a card's type and labels); each stage's agent picks its profile by it and its role.</param>
    public FlowRun RunFlow(Flow flow, string task, string name, string? cwd = null, string? cardId = null, AgentJob? job = null)
    {
        var run = new FlowRun(flow, task, (r, stage, prompt) =>
        {
            var s = flow.Stages[stage];
            var stageJob = (job ?? new AgentJob(JobKinds.Flow, task)) with
            {
                Kind = JobKinds.Flow, Role = s.Role, Text = $"Role: {s.Role}\n{s.Brief.Trim()}\n\nThe task:\n{(job?.Text ?? task).Trim()}",
            };
            var a = Start(prompt, cwd, cardId, $"{name} · {s.Role}", stageJob, purpose: AgentPurposes.Flow);
            a.Flow = r;
            return a;
        });
        run.Begin();
        return run;
    }

    [RelayCommand]
    void StartAgent()
    {
        var prompt = NewPrompt.Trim();
        if (prompt.Length == 0) return;
        Start(prompt, Folder, purpose: AgentPurposes.Chat);
        NewPrompt = "";
    }

    /// <param name="job">What it's for, which picks its model profile; a chat with the prompt when not given.</param>
    /// <param name="id">The agent's id, when its prompt has to name it (a card agent's report does); a new one otherwise.</param>
    /// <param name="dirs">More folders it may work in (--add-dir), beyond the project's repos.</param>
    public AgentViewModel Start(string prompt, string? cwd = null, string? cardId = null, string? name = null, AgentJob? job = null, string? id = null,
        IReadOnlyList<string>? dirs = null, string? purpose = null)
    {
        using var timing = StallWatch.Shared.Section("Agents.Start");
        id ??= NewId();
        job ??= new AgentJob(JobKinds.Chat, prompt);
        // off the UI thread: reading profiles.md, and Haiku's pick, whose process start and prompt write are
        // synchronous (one per card when several start at once, WOR-0076)
        var forJob = job with { Project = job.Project ?? project.Id };
        var profile = Task.Run(() => ModelProfiles.Pick(ModelProfiles.Load(), forJob));
        var folder = string.IsNullOrWhiteSpace(cwd) ? project.WorkingFolder() : cwd;
        var agent = new AgentViewModel(folder, prompt, cardId, name ?? ChatTitle.Guess(prompt),
            args: [.. ArtifactArgs(id), .. RepoArgs(folder), .. (dirs ?? []).SelectMany(d => new[] { "--add-dir", d })], id: id, profile: profile) { StoreFolder = _store, Purpose = purpose };
        Add(agent, select: true);
        agent.Save();
        if (name is null) _ = Retitle(agent, prompt); // a card's agent is already named for the card
        return agent;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>The project's other repos, so an agent started in one can work in the rest (Site and Workspace).
    /// Kept in its saved args like the artifacts folder.</summary>
    List<string> RepoArgs(string cwd)
    {
        try { return project.OtherRepos(cwd).SelectMany(r => new[] { "--add-dir", r }).ToList(); }
        catch (Exception) { return []; }
    }

    /// <summary>Every agent gets a folder for its artifacts (local HTML pages it can show and hear back
    /// from) and is told how to use it. Kept in its saved args, so a resumed session has them too.</summary>
    List<string> ArtifactArgs(string agentId)
    {
        try
        {
            var dir = ArtifactStore.AgentFolder(project.Id.Length > 0 ? project.Id : "default", agentId);
            // the mistake log has a folder of its own: --add-dir so the agent can append without a prompt
            string? log = null;
            try { log = MistakeLog.Ensure(MistakeLog.PathFor(project.Id), project.Name); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // no log; artifacts still work
            // so is the reports folder: a card agent files its report there when it's done (WOR-0061)
            List<string> reports = [];
            try { Directory.CreateDirectory(_reports); reports = ["--add-dir", _reports]; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return log is null
                ? ["--add-dir", dir, .. reports, "--append-system-prompt", ArtifactStore.Briefing(dir) + " " + Decisions.Briefing]
                : ["--add-dir", dir, "--add-dir", Path.GetDirectoryName(log)!, .. reports, "--append-system-prompt", ArtifactStore.Briefing(dir) + " " + MistakeLog.Briefing(log) + " " + Decisions.Briefing];
        }
        catch (Exception) { return []; } // no folder, no artifacts; the agent still runs
    }

    /// <summary>The prompt for working on a card: the card itself, where its file is, and the house rules.</summary>
    /// <param name="alongside">Other agents at work in the same folder: cards started in the same batch and
    /// agents already busy there.</param>
    /// <param name="repo">The folder, when it is a git repository: then the agent is told which branch to work on
    /// (<see cref="CardBranch"/>) and how to decide on, and use, a worktree of its own.</param>
    /// <param name="finish">What to do when the work is finished (<see cref="ReportStore.Rules"/>: QA or done and a report);
    /// null: move the card to done.</param>
    public static string CardPrompt(string projectName, Card card, string file, IReadOnlyList<string>? alongside = null, string? repo = null, string? commits = null,
        string? finish = null)
    {
        var lines = new List<string> { $"Work on this card from the {projectName} board.", "", $"{card.Id}: {card.Title}" };
        if (card.IsBug) lines.Add("Type: Bug. First check it still happens (if it doesn't, see CNR below). Find the cause before changing anything, and add a test that fails without the fix where one can be written.");
        else if (!card.Kind.Equals(CardTypes.Task, StringComparison.OrdinalIgnoreCase)) lines.Add($"Type: {card.Kind}");
        if (card.Labels.Count > 0) lines.Add($"Labels: {string.Join(", ", card.Labels)}");
        if (card.Priority.Length > 0) lines.Add($"Priority: {card.Priority}");
        lines.Add($"Card file: {file}");
        if (card.Description.Length > 0) { lines.Add(""); lines.Add(card.Description); }
        lines.Add("");
        lines.Add("Read the project's CLAUDE.md first. Use AskUserQuestion when a choice is mine to make. " +
                  "When you stop, say what is done and what is left. " + (finish ??
                  "When the work is finished and pushed, move the card to done yourself: " +
                  "set its Status to the board's last column (Completed if the board has no done column), add a Done line with today's date, " +
                  "and move its id into that column in board.json's order. Leave the card where it is while work is left."));
        if (commits is not null) lines.Add(commits + " This is Darg's rule for that folder and wins over any general one.");
        if (alongside is { Count: > 0 })
        {
            lines.Add("");
            lines.Add($"Other agents are working on {string.Join(", ", alongside)} in the same folder at the same time. " +
                      "Change only what this card needs, expect files you didn't touch to change under you, " +
                      "and stage and commit only the files you changed yourself.");
        }
        // every card gets a branch of its own (or the one it names), alone or not
        if (repo is not null) lines.AddRange(CardBranch.Rules(card, repo, alongside is { Count: > 0 }));
        return string.Join(Environment.NewLine, lines);
    }
}

public enum AgentState { Starting, Working, NeedsYou, Idle, Exited, Saved }

/// <summary>One agent and its transcript. Session events arrive on a reader thread and are applied on
/// the UI thread.</summary>
public partial class AgentViewModel : ObservableObject
{
    static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    readonly Dictionary<string, ToolItem> _tools = [];
    AgentSession? _session;

    public ObservableCollection<TranscriptItem> Items { get; } = [];
    public string Id { get; private init; } = Guid.NewGuid().ToString("N")[..10];
    /// <summary>The board card this agent was started on, if any.</summary>
    public string? CardId { get; }
    /// <summary>What it was started for (<see cref="AgentPurposes"/>); null for one saved before agents had one.</summary>
    public string? Purpose { get; init; }
    /// <summary>A question, a permission ask, or the end of a long turn (<see cref="Decisions"/>, WOR-0078).</summary>
    public event Action<string, string>? Notify;
    /// <summary>Every question and permission ask has been answered, so the notifications for them are stale (WOR-0094).</summary>
    public event Action? AsksSettled;
    /// <summary>The flow this agent is a stage of, while this run of Darg lasts.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(InFlow))] public partial FlowRun? Flow { get; set; }
    public bool InFlow => Flow is not null;
    /// <summary>A turn has ended (true: with an error), or the session has (Exited): what a flow waits on.</summary>
    public event Action<bool>? TurnEnded;
    [ObservableProperty] public partial string Name { get; set; }
    public string Cwd { get; }
    /// <summary>Claude Code's id for the conversation: what --resume takes.</summary>
    public string? SessionId { get; private set; }
    /// <summary>Where this agent is saved (set by its owner); null: not saved.</summary>
    public string? StoreFolder { get; set; }
    /// <summary>For a Bench chat: the project it belongs to.</summary>
    public string? Project { get; set; }
    readonly List<string> _args;
    DateTime _created = DateTime.Now;
    public DateTime Created => _created;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText), nameof(Working), nameof(NeedsYou), nameof(Open), nameof(IsDone), nameof(IsFailed))]
    public partial AgentState State { get; set; }
    /// <summary>The last turn ended in an error: shown as failed rather than done.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText), nameof(IsDone), nameof(IsFailed))] public partial bool TurnFailed { get; set; }
    /// <summary>Tool calls in the turn under way: the progress shown while it works.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText))] public partial int Steps { get; set; }
    /// <summary>One line on what it's doing now, what it asks, or how its turn ended.</summary>
    [ObservableProperty] public partial string Activity { get; set; } = "";
    /// <summary>It finished, failed or asked something since it was last open (set by the list that owns it).</summary>
    [ObservableProperty] public partial bool Unseen { get; set; }
    [ObservableProperty] public partial string Compose { get; set; } = "";
    [ObservableProperty] public partial string Model { get; set; } = "";

    // Model, effort and permission mode, changeable mid-chat. A running session is told at once (it takes
    // effect from its next turn); a saved one starts with them when it resumes. Only what the person picked
    // is saved: a mode the session reports (their default, or one the agent switched to) shows but isn't kept.
    /// <summary>The models, the first naming what "default" is here: from the settings Claude Code reads, then
    /// from what a session started on the default reports.</summary>
    [ObservableProperty] public partial IReadOnlyList<AgentChoice> ModelChoices { get; set; } = AgentChoices.Models;
    public IReadOnlyList<AgentChoice> EffortChoices => AgentChoices.Efforts;
    public IReadOnlyList<AgentChoice> ModeChoices => AgentChoices.Modes;
    [ObservableProperty] public partial AgentChoice ModelChoice { get; set; } = AgentChoices.Models[0];
    [ObservableProperty] public partial AgentChoice EffortChoice { get; set; } = AgentChoices.Efforts[0];
    [ObservableProperty] public partial AgentChoice ModeChoice { get; set; } = AgentChoices.Modes[0];
    string? _model, _effort, _mode;
    bool _fromSession;

    partial void OnModelChoiceChanged(AgentChoice value) => Picked(ref _model, value, s => s.SetModel(value.Value));
    partial void OnEffortChoiceChanged(AgentChoice value) => Picked(ref _effort, value, s => s.SetEffort(value.Value));
    partial void OnModeChoiceChanged(AgentChoice value) => Picked(ref _mode, value, s => s.SetPermissionMode(value.Value));

    void Picked(ref string? field, AgentChoice? value, Action<AgentSession> tell)
    {
        if (_fromSession || value is null) return; // the session said so; nothing to tell it
        field = value.Value == AgentChoices.Default ? null : value.Value;
        _handPicked = true;
        if (_session is not null) tell(_session);
        Save();
    }

    /// <summary>Say in the model picker what "default" is, keeping the pick as it was.</summary>
    void NameDefaultModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        var keep = ModelChoice;
        _fromSession = true;
        ModelChoices = AgentChoices.ModelsWithDefault(model);
        ModelChoice = keep is null || keep.Value == AgentChoices.Default ? ModelChoices[0] : AgentChoices.Find(ModelChoices, keep.Value);
        _fromSession = false;
    }

    /// <summary>Show what the session reports without sending it back as a change.</summary>
    void ShowMode(string mode)
    {
        _fromSession = true;
        ModeChoice = AgentChoices.Find(AgentChoices.Modes, mode);
        _fromSession = false;
    }

    /// <summary>How the session is paid for: "on your subscription", or which API key it uses.</summary>
    [ObservableProperty] public partial string Billing { get; set; } = "";
    bool OnSubscription { get; set; } = true;
    public bool Working => State is AgentState.Working or AgentState.Starting;
    public bool NeedsYou => State == AgentState.NeedsYou;
    /// <summary>True while a Claude process runs for this agent (what Close ends).</summary>
    public bool Open => _session is not null;
    /// <summary>Its turn ended well and it waits for the next message.</summary>
    public bool IsDone => State == AgentState.Idle && !TurnFailed;
    /// <summary>Whether the agent still has a claim on its card: at work, waiting on the person, or waiting out a usage limit.
    /// One that has finished its turn, failed, or is only a saved session (closed, resumable) doesn't, so the card
    /// can be worked on, sent back or sent to QA again.</summary>
    public bool IsLiveOnCard => State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou || ((LimitHit || ContinueAt is not null) && State is AgentState.Idle or AgentState.Saved);
    /// <summary>Its turn ended in an error, or its session died or never started.</summary>
    public bool IsFailed => (State == AgentState.Idle && TurnFailed) || State == AgentState.Exited;
    public string StateText => State switch
    {
        AgentState.Starting => "starting",
        AgentState.Working => Steps switch { 0 => "working", 1 => "working · 1 step", _ => $"working · {Steps} steps" },
        AgentState.NeedsYou => "needs you",
        AgentState.Idle or AgentState.Saved when ContinueAt is { } at => $"out of usage · carries on {When(at)}",
        AgentState.Idle when LimitHit => "out of usage",
        AgentState.Idle => TurnFailed ? "stopped with an error" : "done",
        AgentState.Saved when CardDone && FinishedTurn => "Complete, pending removal",
        AgentState.Saved => "saved · send to carry on", _ => "ended",
    };

    /// <summary>Its card is done (in Completed or the board's last column). Set by the project as boards load.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText))] public partial bool CardDone { get; set; }

    /// <summary>Its last turn ended normally ("Turn done" is the transcript's last line), so it wasn't stopped partway.</summary>
    public bool FinishedTurn => Items.LastOrDefault() is NoteItem { Bad: false } n && n.Text.StartsWith("Turn done", StringComparison.Ordinal);

    /// <summary>A turn starts: progress counts from nothing again.</summary>
    DateTime _turnStart = DateTime.MinValue; // when the turn began, for whether its end notifies (Decisions.LongTurn)

    void TurnBegins(string what)
    {
        _turnStart = DateTime.UtcNow;
        Steps = 0;
        TurnFailed = false;
        Activity = what;
        // any message, the person's or the one sent at the reset, ends the wait: a new limit starts a new one
        _continuing = _carryingOn;
        _carryingOn = false;
        _rejected = null;
        StopWaiting();
        LimitHit = false;
    }

    // ---- Out of usage: carry on when the limit resets (WOR-0031). Darg keeps the time and sends the
    // message itself, so nothing of Claude's runs while it waits.

    /// <summary>The last turn stopped because the usage limit was hit.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText), nameof(CanContinueAfterReset))] public partial bool LimitHit { get; set; }
    /// <summary>When it will carry on by itself (the limit's reset and a minute); null: not waiting.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText), nameof(WaitingForReset), nameof(CanContinueAfterReset), nameof(LimitLine))]
    public partial DateTimeOffset? ContinueAt { get; set; }
    /// <summary>When the limit that stopped the last turn resets (null if Claude didn't say).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LimitLine))] public partial DateTimeOffset? LimitResets { get; set; }
    public bool WaitingForReset => ContinueAt is not null;
    public bool CanContinueAfterReset => LimitHit && ContinueAt is null;
    public string LimitLine => ContinueAt is { } at ? $"Out of usage. Carries on by itself {When(at)}."
        : LimitResets is { } r ? $"Out of usage until {When(r)}." : "Out of usage; Claude didn't say when it resets.";
    /// <summary>What the agent is sent when the limit has reset.</summary>
    public const string CarryOnMessage = "Your usage limit has reset. Carry on with what you were doing where you left off.";

    UsageLimited? _rejected;          // a rate_limit_event this turn that said the limit is hit
    bool _carryingOn, _continuing;    // the next turn / this turn was sent at the reset (so a new limit re-arms by itself)
    DispatcherTimer? _waitTimer;

    /// <summary>Continue after reset: wait for the limit to reset, then send the agent a message to carry on.</summary>
    [RelayCommand]
    public void ContinueAfterReset()
    {
        if (CanContinueAfterReset) WaitFor(UsageLimit.ContinueAt(LimitResets, null, DateTimeOffset.Now));
    }

    /// <summary>Don't carry on by itself after all.</summary>
    [RelayCommand]
    void CancelContinue()
    {
        StopWaiting();
        Items.Add(new NoteItem("Won't carry on by itself. Send a message when you want it to.", false));
        Activity = LimitLine;
        Save();
    }

    void WaitFor(DateTimeOffset at)
    {
        ContinueAt = at;
        Activity = LimitLine;
        // a tick every 20s against the clock rather than one long timer: a PC that sleeps through the
        // reset still carries on soon after it wakes
        _waitTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => CheckWait());
        _waitTimer.Start();
        Save();
    }

    void StopWaiting()
    {
        _waitTimer?.Stop();
        ContinueAt = null;
    }

    void CheckWait()
    {
        if (ContinueAt is not { } at || DateTimeOffset.Now < at) return;
        StopWaiting();
        if (Working || NeedsYou) return; // it's been given something else to do meanwhile
        _carryingOn = true;
        Say("↻ Carry on (the usage limit has reset)", CarryOnMessage);
    }

    /// <summary>A time, said as the person reads a clock: "at 6:21pm", "tomorrow 5am", "Thu 20 Nov 5:01pm".</summary>
    public static string When(DateTimeOffset t)
    {
        var local = t.ToLocalTime().DateTime;
        var clock = local.ToString(local.Minute == 0 ? "htt" : "h:mmtt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
        var days = (local.Date - DateTime.Today).Days;
        return days switch { 0 => $"at {clock}", 1 => $"tomorrow {clock}", _ => $"{local:ddd d MMM} {clock}" };
    }

    /// <param name="shown">What the transcript shows for the first message, when what's sent carries more
    /// (Bench sends a line of context ahead of the person's words).</param>
    /// <param name="profile">The model profile for its job, still being picked (WOR-0034): the session starts once
    /// it is in, on that profile's model and effort. Null: start now on the person's own settings.</param>
    public AgentViewModel(string cwd, string prompt, string? cardId = null, string? name = null, IEnumerable<string>? args = null, string? shown = null, string? id = null,
        Task<ProfilePick?>? profile = null)
    {
        if (id is not null) Id = id;
        Cwd = cwd;
        CardId = cardId;
        _args = args?.ToList() ?? [];
        NameDefaultModel(AgentChoices.DefaultModel(cwd));
        _mode = AgentChoices.NewAgentMode; // saved with it, so a resume stays in Auto too
        ShowMode(_mode);
        var visible = shown ?? prompt;
        Name = name ?? (visible.Length > 40 ? visible[..40].TrimEnd() + "…" : visible);
        Items.Add(new UserItem(visible));
        if (profile is null) Begin(prompt, resume: false);
        else StartOnProfile(profile, prompt);
    }

    /// <summary>The model profile it started on ("Deep"), if one was picked.</summary>
    [ObservableProperty] public partial string Profile { get; set; } = "";
    bool _handPicked, _ended;

    /// <summary>Wait for the profile (a rule's is there at once; Haiku's takes a few seconds), take its model
    /// and effort unless the person picked their own meanwhile, then start.</summary>
    async void StartOnProfile(Task<ProfilePick?> profile, string prompt)
    {
        ProfilePick? pick = null;
        if (!profile.IsCompleted)
        {
            State = AgentState.Starting;
            Activity = "Picking a model…";
        }
        try { pick = await profile; } catch (Exception) { }
        if (_ended) return; // closed or forgotten while it was being picked
        if (pick is not null && !_handPicked)
        {
            _model = pick.Profile.Model == AgentChoices.Default ? null : pick.Profile.Model;
            _effort = pick.Profile.Effort == AgentChoices.Default ? null : pick.Profile.Effort;
            _fromSession = true;
            ModelChoice = AgentChoices.Find(AgentChoices.Models, _model);
            EffortChoice = AgentChoices.Find(AgentChoices.Efforts, _effort);
            _fromSession = false;
            Profile = pick.Profile.Name;
            Items.Add(new NoteItem($"Profile {pick.Profile.Name} ({pick.Profile.Summary}) · {pick.Why}", false));
        }
        Begin(prompt, resume: false);
        Save();
    }

    /// <summary>Set for Bench's chats: turns what the person typed into what's sent (adds where they are).</summary>
    public Func<string, string>? Wrap { get; set; }

    /// <summary>Send a message that shows as <paramref name="shown"/> but goes to Claude as <paramref name="sent"/>.</summary>
    public void Say(string shown, string sent)
    {
        Items.Add(new UserItem(shown));
        if (_session is null)
        {
            if (SessionId is null) { Items.Add(new NoteItem("This chat has no session to carry on; start a new one.", true)); return; }
            Items.Add(new NoteItem("Resuming the session…", false));
            Begin(sent, resume: true);
            return;
        }
        _session.Send(sent);
        TurnBegins("Thinking…");
        State = AgentState.Working;
    }

    /// <summary>An agent from an earlier run of Darg: its transcript, and its session to resume.</summary>
    public AgentViewModel(AgentRecord r, string storeFolder)
    {
        Id = r.Id; Name = r.Name; Cwd = r.Cwd; CardId = r.CardId; Purpose = r.Purpose; SessionId = r.SessionId; Project = r.Project;
        _args = r.Args; _created = r.Created; StoreFolder = storeFolder;
        _model = r.Model; _effort = r.Effort; _mode = r.Mode;
        NameDefaultModel(AgentChoices.DefaultModel(Cwd));
        _fromSession = true;
        ModelChoice = AgentChoices.Find(ModelChoices, r.Model);
        EffortChoice = AgentChoices.Find(AgentChoices.Efforts, r.Effort);
        ModeChoice = AgentChoices.Find(AgentChoices.Modes, r.Mode);
        _fromSession = false;
        foreach (var i in r.Items) Items.Add(FromRecord(i));
        Activity = OneLine(Items.OfType<TextItem>().LastOrDefault()?.Text);
        State = AgentState.Saved;
        // it was waiting for the usage limit to reset when Darg closed: it still is (or carries on now)
        if (r.ContinueAt is { } at) { LimitHit = true; WaitFor(at); }
    }

    /// <summary>Start (or resume) the Claude session and send it a message.</summary>
    void Begin(string message, bool resume)
    {
        using var timing = StallWatch.Shared.Section("Agent.Begin");
        try
        {
            List<string> args = resume && SessionId is not null ? ["--resume", SessionId, .. _args] : [.. _args];
            args.AddRange(AgentChoices.LaunchArgs(_model, _effort, _mode));
            AgentSession session;
            using (StallWatch.Shared.Section("AgentSession.Start")) session = AgentSession.Start(Cwd, extraArgs: args);
            _session = session;
            // a session closed on request still reports its last events and its exit: they're not news
            session.Event += e => Dispatcher.UIThread.Post(() => { if (_session == session) On(e); });
            session.Send(message); // queued: it never waits for claude to read (WOR-0076)
            TurnBegins("Starting…");
            State = AgentState.Starting;
        }
        catch (Exception e)
        {
            Items.Add(new NoteItem($"Couldn't start Claude: {e.Message}", true));
            Activity = OneLine($"Couldn't start Claude: {e.Message}");
            State = AgentState.Exited;
        }
    }

    /// <summary>Write this agent to its store (after every turn and on exit).</summary>
    public void Save()
    {
        if (StoreFolder is null) return;
        using var timing = StallWatch.Shared.Section("Agent.Save");
        try
        {
            // the record is a snapshot taken here; it is written in the background (WOR-0076)
            AgentStore.SaveLater(StoreFolder, new AgentRecord
            {
                Id = Id, Name = Name, Cwd = Cwd, CardId = CardId, Purpose = Purpose, Project = Project, SessionId = SessionId, Args = [.. _args],
                Model = _model, Effort = _effort, Mode = _mode, ContinueAt = ContinueAt,
                Created = _created, Updated = DateTime.Now, Items = Items.Select(ToRecord).OfType<ItemRecord>().ToList(),
            });
        }
        catch (Exception) { /* a failed save must never take the agent down */ }
    }

    static ItemRecord? ToRecord(TranscriptItem i) => i switch
    {
        UserItem u => new() { Kind = "user", Text = u.Text },
        TextItem t => new() { Kind = "text", Text = t.Text },
        NoteItem n => new() { Kind = "note", Text = n.Text, Bad = n.Bad },
        ToolItem t => new() { Kind = "tool", Name = t.Name, Summary = t.Summary, ImagePath = t.ImagePath, Removed = t.Removed, Added = t.Added, Preview = t.Preview, Result = t.Result, Bad = t.IsError,
            Images = t.Gallery.Count > 0 ? t.Gallery.Select(g => g.Path).ToList() : null },
        QuestionItem q => new() { Kind = "note", Text = "Asked: " + string.Join(" / ", q.Questions.Select(g => g.Question + (g.Chosen is null ? "" : $" → {g.Chosen}"))) },
        PermissionItem p => new() { Kind = "note", Text = $"Wanted {p.Tool}: {p.Decision ?? "no answer"}" },
        _ => null,
    };

    static TranscriptItem FromRecord(ItemRecord r) => r.Kind switch
    {
        "user" => new UserItem(r.Text),
        "text" => new TextItem(r.Text),
        "tool" => ToolItem.Restore(r),
        _ => new NoteItem(r.Text, r.Bad),
    };

    void On(AgentEvent e)
    {
        using var timing = StallWatch.Shared.Section("Agent event " + e.GetType().Name);
        switch (e)
        {
            case SessionStarted s:
                Model = s.Model;
                if (_model is null && !_args.Contains("--model")) NameDefaultModel(s.Model); // on the default: this is what it is
                SessionId = s.SessionId;
                OnSubscription = s.OnSubscription;
                Billing = s.OnSubscription ? "on your subscription" : $"API key ({s.ApiKeySource})";
                if (s.PermissionMode.Length > 0) ShowMode(s.PermissionMode);
                State = AgentState.Working;
                Save();
                break;
            case ModeChanged m:
                ShowMode(m.Mode);
                if (_mode is not null) _mode = m.Mode == AgentChoices.Default ? null : m.Mode; // left plan mode: resume where it is now
                break;
            case ModelChanged mc:
                Model = mc.Model;
                Items.Add(new NoteItem($"Model now {mc.Model}", false));
                break;
            case ControlFailed cf:
                Items.Add(new NoteItem($"Couldn't change that: {cf.Error}", true));
                break;
            case AssistantText t:
                Items.Add(new TextItem(t.Text));
                if (Working) Activity = OneLine(t.Text);
                break;
            case ToolCall c:
                var item = ToolItem.From(c);
                _tools[c.Id] = item;
                Items.Add(item);
                Steps++;
                Activity = OneLine(item.Summary.Length > 0 ? $"{c.Name}: {item.Summary}" : c.Name);
                break;
            case ToolResult r when _tools.TryGetValue(r.ToolUseId, out var tool):
                tool.SetResult(r, Cwd);
                break;
            case PermissionAsked { ToolName: "AskUserQuestion" } q:
                var question = new QuestionItem(q, this);
                Items.Add(question);
                Activity = OneLine(question.Questions.FirstOrDefault()?.Question ?? "has a question");
                State = AgentState.NeedsYou;
                Notify?.Invoke("has a question", question.Questions.FirstOrDefault()?.Question ?? "");
                break;
            case PermissionAsked p:
                var perm = new PermissionItem(p, this);
                Items.Add(perm);
                Activity = OneLine($"wants {p.ToolName}: {perm.Summary}");
                State = AgentState.NeedsYou;
                Notify?.Invoke($"wants to use {p.ToolName}", perm.Summary);
                break;
            case UsageLimited u when u.Rejected:
                _rejected = u;
                break;
            case TurnFinished f when IsLimit(f):
                OutOfUsage(f);
                break;
            case TurnFinished f:
                if (f.IsError) Items.Add(new NoteItem(f.Text, true));
                // Claude Code reports a cost on every turn, priced as if on the API; on a subscription
                // nothing is charged per turn, so only an API-key session shows it
                Items.Add(new NoteItem(!OnSubscription && f.CostUsd is { } cost ? $"Turn done · ${cost:0.000} (API key)" : "Turn done", false));
                TurnFailed = f.IsError;
                Activity = OneLine(f.IsError ? f.Text : Items.OfType<TextItem>().LastOrDefault()?.Text ?? f.Text);
                State = AgentState.Idle;
                Save();
                TurnEnded?.Invoke(f.IsError);
                // only long turns (or a failed one) interrupt: a quick answer is already in the transcript (WOR-0081)
                // only a long turn's end notifies, failed or not; a question or permission always does (WOR-0081, WOR-0078)
                if (Decisions.TellTurnEnded(_turnStart == DateTime.MinValue ? null : DateTime.UtcNow - _turnStart))
                    Notify?.Invoke(f.IsError ? "stopped with an error" : "finished",
                        Items.OfType<TextItem>().LastOrDefault()?.Text ?? f.Text);
                break;
            case SessionExited x:
                // a session we closed ourselves (resume later) isn't news; one that died is
                if (x.ExitCode != 0) Items.Add(new NoteItem($"Session ended ({x.ExitCode}) {Last(x.Stderr)}", true));
                _session = null;
                if (x.ExitCode != 0) Activity = OneLine($"Session ended ({x.ExitCode}) {Last(x.Stderr)}");
                State = SessionId is not null && x.ExitCode == 0 ? AgentState.Saved : AgentState.Exited;
                Save();
                TurnEnded?.Invoke(true);
                break;
        }
    }

    /// <summary>Whether a turn stopped because the usage limit was hit (the error text says so, or it failed after
    /// Claude Code said the limit was hit).</summary>
    bool IsLimit(TurnFinished f) =>
        UsageLimit.IsLimit(f.Text) || (f.IsError && (_rejected is not null || UsageLimit.IsLimit(Items.OfType<TextItem>().LastOrDefault()?.Text)));

    void OutOfUsage(TurnFinished f)
    {
        var text = f.Text.Length > 0 ? f.Text : Items.OfType<TextItem>().LastOrDefault()?.Text ?? "";
        var now = DateTimeOffset.Now;
        LimitResets = _rejected?.ResetsAt is { } r && r > now.AddDays(-1) ? r : UsageLimit.ResetFrom(text, now);
        // Claude Code says it as a reply too: shown once is enough
        if (Items.LastOrDefault() is not TextItem said || said.Text != text.Trim()) Items.Add(new NoteItem(text.Length > 0 ? text : "Out of usage", true));
        TurnFailed = true;
        LimitHit = true;
        State = AgentState.Idle;
        // a flow runs on its own, and a turn sent at the reset that hits a limit again (a weekly one, say)
        // was already asked to carry on: both wait again without asking
        var again = _continuing || InFlow;
        if (again) WaitFor(UsageLimit.ContinueAt(LimitResets, text, now));
        Activity = LimitLine;
        Save();
        if (!again) TurnEnded?.Invoke(true); // a flow that waits on this agent keeps waiting while it does
        // no notification: it waits for the reset on its own, nothing to decide (WOR-0078)
    }

    /// <summary>The first non-empty line of some text, without markdown heading marks, cut to fit a list row.</summary>
    static string OneLine(string? s, int max = 90)
    {
        var line = (s ?? "").Split(NewLines, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        line = line.TrimStart('#', '*', ' ');
        return line.Length > max ? line[..max].TrimEnd() + "…" : line;
    }
    static readonly char[] NewLines = [(char)13, (char)10];
    /// <summary>What the agent said in its last turn: its text since the last message it was sent.</summary>
    public string LastReply()
    {
        var since = Items.Count;
        while (since > 0 && Items[since - 1] is not UserItem) since--;
        return string.Join("\n\n", Items.Skip(since).OfType<TextItem>().Select(t => t.Text));
    }

    /// <summary>A line in the transcript from Darg itself (a flow saying where it is).</summary>
    public void AddNote(string text, bool bad = false)
    {
        Items.Add(new NoteItem(text, bad));
        Save();
    }

    static string Last(string s) => string.Join(" ", s.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(3));

    [RelayCommand]
    void Send()
    {
        var text = Compose.Trim();
        if (text.Length == 0) return;
        Compose = "";
        if (Wrap is not null) { Say(text, Wrap(text)); return; } // a Bench chat: it hears where Philip is
        Items.Add(new UserItem(text));
        if (_session is null)
        {
            // a saved agent (or one whose session ended): carry on in the same conversation if there is one
            if (SessionId is null) { Items.Add(new NoteItem("This agent has no session to carry on; start a new one.", true)); return; }
            Items.Add(new NoteItem("Resuming the session…", false));
            Begin(text, resume: true);
            return;
        }
        _session.Send(text);
        TurnBegins("Thinking…");
        State = AgentState.Working;
    }

    /// <summary>✎ Draw: the agent puts a canvas up through its own claude-draw tools, so the drawing comes back
    /// to this conversation. Whatever is in the compose box says what it's about.</summary>
    [RelayCommand]
    void Draw()
    {
        var about = Compose.Trim();
        Compose = "";
        var sent = ClaudeDraw.AskToDraw(about);
        Say(about.Length == 0 ? "✎ I want to draw something for you" : $"✎ I want to draw something for you: {about}", Wrap is null ? sent : Wrap(sent));
    }

    /// <summary>The project's mistake log (set by its owner); null hides ✗ Mistake.</summary>
    public string? MistakeLogFile { get; set; }
    public string? ProjectName { get; set; }

    /// <summary>✗ Mistake: the agent logs its last reply as a mistake in the project's log. Whatever is in the
    /// compose box says what was wrong; with nothing typed it works that out, or asks.</summary>
    [RelayCommand]
    void Mistake()
    {
        if (MistakeLogFile is null) return;
        try { MistakeLog.Ensure(MistakeLogFile, ProjectName ?? "This project"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Items.Add(new NoteItem($"Couldn't make the mistake log: {e.Message}", true));
            return;
        }
        var what = Compose.Trim();
        Compose = "";
        var sent = MistakeLog.AskToLog(MistakeLogFile, what);
        Say(what.Length == 0 ? "✗ That was a mistake: log it" : $"✗ That was a mistake: {what}", Wrap is null ? sent : Wrap(sent));
    }

    [RelayCommand]
    void Stop() => _session?.Interrupt();

    /// <summary>Close: end the Claude process but keep the agent, so sending it a message resumes the conversation.</summary>
    [RelayCommand]
    void Close() => CloseSession();

    /// <summary>Close (from the button, Close idle or Fettle). False: no session was open.</summary>
    public bool CloseSession(string? by = null)
    {
        if (_session is null) return false;
        var mid = Working || NeedsYou;
        End(stopWaiting: false); // closed while waiting for the reset, it resumes its session then
        var who = by is null ? "Session closed" : $"Session closed by {by}";
        Items.Add(new NoteItem(SessionId is null ? $"{who} before it began; start a new agent to carry on."
            : mid ? $"{who} mid-turn. Send a message to carry on." : $"{who}. Send a message to carry on.", false));
        Save();
        return true;
    }

    /// <summary>End the session (closed on request, forgotten, or its project is closing). What it was
    /// doing stops; the conversation can be resumed later if it has a session id.</summary>
    /// <param name="stopWaiting">False: keep waiting to carry on after a usage limit (only Close keeps it: the
    /// agent is still listed, and sending it a message resumes the session).</param>
    public void End(bool stopWaiting = true)
    {
        _ended = true;
        if (stopWaiting) _waitTimer?.Stop(); // ContinueAt stays saved: shown again, it waits again
        var s = _session;
        if (s is null) return;
        _session = null;
        // a question or permission still waiting can't be answered now: its buttons go quiet
        foreach (var p in Items.OfType<PermissionItem>().Where(p => p.Pending)) p.Decision = "No answer (session closed)";
        foreach (var q in Items.OfType<QuestionItem>().Where(q => q.Pending)) q.Sent = true;
        State = SessionId is not null ? AgentState.Saved : AgentState.Exited;
        _ = s.DisposeAsync().AsTask();
    }

    internal void Answered(TranscriptItem item, Action<AgentSession> respond)
    {
        if (_session is null) return;
        respond(_session);
        var waiting = Items.OfType<IPending>().Any(p => p.Pending);
        if (!waiting) { Activity = "Carrying on…"; AsksSettled?.Invoke(); }
        State = waiting ? AgentState.NeedsYou : AgentState.Working;
    }

    internal static bool IsImage(string? path) => path is not null && ImageExts.Contains(Path.GetExtension(path));
}

public abstract class TranscriptItem : ObservableObject;
public interface IPending { bool Pending { get; } }

public sealed class UserItem(string text) : TranscriptItem { public string Text { get; } = text; }
public sealed class TextItem(string text) : TranscriptItem { public string Text { get; } = text; }
public sealed class NoteItem(string text, bool bad) : TranscriptItem
{
    public string Text { get; } = text;
    public bool Bad { get; } = bad;
}

/// <summary>A tool call: a one-line summary, and where it helps, what it touched shown directly:
/// the image read or written, an edit's diff, a written file's start.</summary>
public sealed partial class ToolItem : TranscriptItem
{
    public required string Name { get; init; }
    public required string Summary { get; init; }
    public Bitmap? Image { get; init; }
    public string? ImagePath { get; init; }
    public string? Removed { get; init; }
    public string? Added { get; init; }
    public string? Preview { get; init; }
    public bool HasImage => Image is not null;
    public bool HasDiff => Removed is not null || Added is not null;
    public bool HasPreview => Preview is not null;
    /// <summary>The artifact this step wrote or changed, if it did: a button opens it in Artifacts.</summary>
    public string? ArtifactPath { get; init; }
    public bool HasArtifact => ArtifactPath is not null;
    [RelayCommand] void OpenArtifact() { if (ArtifactPath is not null) ArtifactHost.Open(ArtifactPath); }
    [ObservableProperty] public partial string? Result { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Ok))] public partial bool IsError { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Ok))] public partial bool Done { get; set; }
    /// <summary>Finished without an error: a tick. An error shows a cross instead.</summary>
    public bool Ok => Done && !IsError;
    /// <summary>Opened to show its detail where steps are shown folded (Fettle's box, WOR-0089); the Agents view always shows it.</summary>
    [ObservableProperty] public partial bool Expanded { get; set; }
    [RelayCommand] void ToggleExpanded() => Expanded = !Expanded;

    /// <summary>Images the step made or pointed at (a generator's output, a script's saved file, an image
    /// the tool sent back): thumbnails under the step; click one for the full image.</summary>
    public ObservableCollection<PreviewImage> Gallery { get; } = [];
    [ObservableProperty] public partial bool HasGallery { get; set; }
    /// <summary>The call's input as text, kept to look for image paths when its result comes.</summary>
    public string InputText { get; init; } = "";

    public void SetResult(ToolResult r, string cwd)
    {
        Done = true;
        IsError = r.IsError;
        // Successful results are noise in the transcript; errors and short outputs are worth a line.
        if (r.IsError || Name is "Bash" or "PowerShell" or "Grep" or "Glob")
            Result = r.Text.Length > 1200 ? r.Text[..1200] + "\n…" : r.Text;
        if (r.IsError) return;
        // images the tool sent back: kept as files so they come back with the transcript
        foreach (var bytes in r.Images ?? [])
        {
            try
            {
                var dir = Path.Combine(InstanceConfig.Folder(), "previews");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.png");
                File.WriteAllBytes(file, bytes);
                AddPreview(file);
            }
            catch (Exception) { }
        }
        // images the result or the call names (not ones only searched for: a Glob's list of matches isn't a picture to show)
        if (Name is "Glob" or "Grep") return;
        foreach (var p in ImagePaths.Find(r.Text + "\n" + InputText, cwd))
            if (!string.Equals(p, ImagePath, StringComparison.OrdinalIgnoreCase)) AddPreview(p);
    }

    public void AddPreview(string path)
    {
        if (Gallery.Count >= 12 || Gallery.Any(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            using var f = File.OpenRead(path);
            Gallery.Add(new PreviewImage(path, Bitmap.DecodeToHeight(f, 260)));
            HasGallery = true;
        }
        catch (Exception) { }
    }

    public static ToolItem From(ToolCall c)
    {
        string? S(string k) => (string?)c.Input[k];
        var path = S("file_path") ?? S("notebook_path") ?? S("path");
        var summary = S("command") ?? S("description") ?? path ?? S("pattern") ?? S("query") ?? S("url") ?? S("skill") ?? "";
        if (summary.Length > 160) summary = summary[..160] + "…";
        Bitmap? image = null;
        if (AgentViewModel.IsImage(path) && c.Name is "Read" or "Write" or "Edit")
            image = TryLoad(path!);
        string? removed = null, added = null, preview = null;
        if (c.Name == "Edit") { removed = Clip(S("old_string")); added = Clip(S("new_string")); }
        if (c.Name == "MultiEdit" && c.Input["edits"] is JsonArray edits)
        {
            removed = Clip(string.Join("\n···\n", edits.Select(x => (string?)x?["old_string"])));
            added = Clip(string.Join("\n···\n", edits.Select(x => (string?)x?["new_string"])));
        }
        if (c.Name == "Write" && image is null) preview = Clip(S("content"));
        return new ToolItem { Name = c.Name, Summary = summary, Image = image, ImagePath = image is null ? null : path,
            Removed = removed, Added = added, Preview = preview, InputText = c.Input.ToJsonString(), ArtifactPath = ArtifactIn(c.Name, path) };
    }

    /// <summary>A tool line from a saved transcript (its image read again from disk if it's still there).</summary>
    public static ToolItem Restore(ItemRecord r)
    {
        var image = AgentViewModel.IsImage(r.ImagePath) && File.Exists(r.ImagePath) ? TryLoad(r.ImagePath!) : null;
        var item = new ToolItem
        {
            Name = r.Name ?? "", Summary = r.Summary ?? "", Image = image, ImagePath = image is null ? null : r.ImagePath,
            Removed = r.Removed, Added = r.Added, Preview = r.Preview, Result = r.Result, IsError = r.Bad, Done = true,
            ArtifactPath = ArtifactIn(r.Name, r.Summary), // a file step's summary is its path
        };
        foreach (var p in r.Images ?? []) if (File.Exists(p)) item.AddPreview(p);
        return item;
    }

    static string? ArtifactIn(string? tool, string? path) =>
        tool is "Write" or "Edit" or "MultiEdit" && path is not null && ArtifactStore.Owner(path) is not null ? path : null;

    static string? Clip(string? s)
    {
        if (s is null) return null;
        var lines = s.Replace("\r\n", "\n").Split('\n');
        return lines.Length > 40 ? string.Join("\n", lines.Take(40)) + $"\n… {lines.Length - 40} more lines" : s;
    }

    static Bitmap? TryLoad(string path)
    {
        try { using var f = File.OpenRead(path); return Bitmap.DecodeToWidth(f, 520); }
        catch (Exception) { return null; }
    }
}

/// <summary>Claude wants to run a tool that needs the person's say.</summary>
public sealed partial class PermissionItem : TranscriptItem, IPending
{
    readonly PermissionAsked _ask;
    readonly AgentViewModel _agent;
    public string Tool => _ask.ToolName;
    public string Summary { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Pending))] public partial string? Decision { get; set; }
    public bool Pending => Decision is null;

    public PermissionItem(PermissionAsked ask, AgentViewModel agent)
    {
        _ask = ask; _agent = agent;
        var i = ask.Input;
        Summary = (string?)i["command"] ?? (string?)i["file_path"] ?? (string?)i["url"] ?? i.ToJsonString();
        if (Summary.Length > 400) Summary = Summary[..400] + "…";
    }

    [RelayCommand] void Allow() { Decision = "Allowed"; _agent.Answered(this, s => s.Allow(_ask.RequestId, _ask.Input)); }
    [RelayCommand] void Deny() { Decision = "Denied"; _agent.Answered(this, s => s.Deny(_ask.RequestId, "The person said no.")); }
}

/// <summary>An AskUserQuestion, shown as its questions with an option button each.</summary>
public sealed partial class QuestionItem : TranscriptItem, IPending
{
    readonly PermissionAsked _ask;
    readonly AgentViewModel _agent;
    readonly Dictionary<string, string> _answers = [];
    public ObservableCollection<QuestionGroup> Questions { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Pending))] public partial bool Sent { get; set; }
    public bool Pending => !Sent;

    public QuestionItem(PermissionAsked ask, AgentViewModel agent)
    {
        _ask = ask; _agent = agent;
        foreach (var q in ask.Input["questions"] as JsonArray ?? [])
        {
            var text = (string?)q?["question"] ?? "";
            var multi = (bool?)q?["multiSelect"] == true;
            QuestionGroup? group = null;
            var opts = (q?["options"] as JsonArray ?? []).Select(o =>
            {
                var label = (string?)o?["label"] ?? "";
                return new QuestionOption(label, (string?)o?["description"] ?? "", multi, () => group!.Choose(label));
            }).ToList();
            group = new QuestionGroup((string?)q?["header"] ?? "", text, opts, multi, this);
            Questions.Add(group);
        }
    }

    internal void Pick(string question, string label)
    {
        if (Sent) return;
        _answers[question] = label;
        foreach (var g in Questions.Where(g => g.Question == question)) g.Chosen = label;
        if (Questions.All(g => _answers.ContainsKey(g.Question)))
        {
            Sent = true;
            _agent.Answered(this, s => s.Answer(_ask.RequestId, _ask.Input, _answers));
        }
    }
}

/// <summary>One question: its option buttons, and a box to type an answer of your own instead (the terminal's "Other").
/// A multiSelect question takes several options: they toggle, and Send picks sends them together, labels joined by commas.</summary>
public sealed partial class QuestionGroup(string header, string question, IReadOnlyList<QuestionOption> options, bool multiSelect, QuestionItem owner) : ObservableObject
{
    public string Header { get; } = header;
    public string Question { get; } = question;
    public IReadOnlyList<QuestionOption> Options { get; } = options;
    public bool MultiSelect { get; } = multiSelect;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowSendPicks))] public partial string? Chosen { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendOwnCommand))] public partial string OwnAnswer { get; set; } = "";

    public bool ShowSendPicks => MultiSelect && Chosen is null;

    internal void Choose(string label)
    {
        if (!MultiSelect) { owner.Pick(Question, label); return; }
        var option = Options.First(o => o.Label == label);
        option.IsSelected = !option.IsSelected;
        SendPicksCommand.NotifyCanExecuteChanged();
    }

    bool CanSendPicks => Options.Any(o => o.IsSelected);
    [RelayCommand(CanExecute = nameof(CanSendPicks))] void SendPicks() =>
        owner.Pick(Question, string.Join(", ", Options.Where(o => o.IsSelected).Select(o => o.Label)));

    bool CanSendOwn => !string.IsNullOrWhiteSpace(OwnAnswer);
    [RelayCommand(CanExecute = nameof(CanSendOwn))] void SendOwn() => owner.Pick(Question, OwnAnswer.Trim());
}

public sealed partial class QuestionOption(string label, string description, bool multiSelect, Action choose) : ObservableObject
{
    public string Label { get; } = label;
    public string Description { get; } = description;
    public bool MultiSelect { get; } = multiSelect;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public IRelayCommand Command { get; } = new RelayCommand(choose);
}

/// <summary>A thumbnail of an image a step made; clicking it opens the full image in the system's viewer.</summary>
public sealed class PreviewImage(string path, Bitmap thumb)
{
    public string Path { get; } = path;
    public Bitmap Thumb { get; } = thumb;
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public IRelayCommand OpenCommand { get; } = new RelayCommand(() =>
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { }
    });
}
