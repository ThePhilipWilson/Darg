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
        Agents.CollectionChanged += (_, _) => { Resort(); CountRunning(); };
        foreach (var r in AgentStore.List(_store)) Add(new AgentViewModel(r, _store), select: false);
        // agents waiting to start when Darg last closed wait again, first asked first (WOR-0245)
        // once the agents cut off mid-turn have resumed (RefreshCards, as the boards are read), so they count; a project
        // with no boards has none to resume
        _pending.AddRange(Agents.Where(a => a.IsPending).OrderBy(a => a.Created));
        if (_pending.Count > 0) Dispatcher.UIThread.Post(() => { if (CardStillWorked is null) Fill(); }, DispatcherPriority.Background);
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

    /// <summary>The project is leaving the app: end every agent and start none from now on (<see cref="AgentViewModel.Shut"/>).</summary>
    public void Close()
    {
        _closed = true;
        foreach (var a in Agents.ToList()) a.Shut();
    }
    bool _closed;

    /// <summary>The agents as the list shows them: <see cref="Agents"/> in its own order, or grouped by status.</summary>
    public ObservableCollection<AgentViewModel> Listed { get; } = [];

    /// <summary>Sort the list by status: waiting on you, then working, failed, done, saved. Otherwise newest first.</summary>
    [ObservableProperty] public partial bool SortByStatus { get; set; }
    partial void OnSortByStatusChanged(bool value) => Resort();

    static int Rank(AgentViewModel a) => a.State switch
    {
        AgentState.NeedsYou => 0,
        AgentState.Starting or AgentState.Working => 1,
        AgentState.Pending => 2, // after the ones at work, before how others ended
        AgentState.Exited => 3,
        AgentState.Idle => a.TurnFailed ? 3 : 4,
        _ => 5, // saved
    };

    /// <summary>Set by the project: each card's board by card id (<c>BoardViewModel.CardBoards</c>).</summary>
    public Func<Dictionary<string, string>>? CardBoards { get; set; }
    Dictionary<string, string> _cardBoards = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The boards whose agents show; empty is all of them (the All tab lit).</summary>
    readonly HashSet<string> _shownBoards = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tabs above the list (WOR-0150), as the board's: All, then a tab per board, lit for each showing.</summary>
    public ObservableCollection<AgentBoardTab> Tabs { get; } = [];
    public bool HasTabs => Tabs.Count > 2; // All and one board is no choice
    /// <summary>Set by the view as a tab is pressed: whether Ctrl was down.</summary>
    public bool TabCtrl { get; set; }

    bool InShownBoards(AgentViewModel a)
    {
        if (_shownBoards.Count == 0) return true;
        if (a.CardId is null) return false;
        if (!_cardBoards.ContainsKey(a.CardId) && CardBoards is not null) _cardBoards = CardBoards(); // a card made since
        return _cardBoards.TryGetValue(a.CardId, out var b) && _shownBoards.Contains(b);
    }

    /// <summary>A tab's click: plain shows only that board (All: every board); Ctrl adds it to those showing or takes it off.
    /// Taking off the last one goes back to All.</summary>
    public void PickBoard(string? board, bool additive)
    {
        if (board is null) _shownBoards.Clear();
        else if (!additive) { _shownBoards.Clear(); _shownBoards.Add(board); }
        else if (!_shownBoards.Remove(board)) _shownBoards.Add(board);
        SyncTabs();
        Resort();
    }

    void SyncTabs()
    {
        var boards = _cardBoards.Values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var b in _shownBoards) if (!boards.Contains(b, StringComparer.OrdinalIgnoreCase)) boards.Add(b);
        var names = new[] { (string?)null }.Concat(boards).ToList();
        if (!names.SequenceEqual(Tabs.Select(t => t.Board)))
        {
            Tabs.Clear();
            foreach (var n in names) Tabs.Add(new AgentBoardTab(this, n));
            OnPropertyChanged(nameof(HasTabs));
        }
        foreach (var t in Tabs) t.IsShown = t.Board is null ? _shownBoards.Count == 0 : _shownBoards.Contains(t.Board);
    }

    /// <summary>Bring Listed into the wanted order by moving rows, so the selection and scroll stay put.</summary>
    void Resort()
    {
        var visible = Agents.Where(InShownBoards);
        var want = SortByStatus ? visible.OrderBy(Rank).ToList() : visible.ToList(); // OrderBy is stable: ties keep list order
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
        a.Gone = true; // its session's end, which arrives after this, mustn't save it back
        a.End();
        AgentStore.Delete(_store, a.Id);
        Agents.Remove(a);
        Fill(); // a pending one forgotten leaves the line; one at work forgotten makes room
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

    /// <summary>Forget every finished agent here: one whose turn ended well and is waiting, a saved one whose card is done, and
    /// every task agent that isn't running (<see cref="TaskOver"/>). Working, waiting on you, set to carry on after a usage
    /// reset and failed agents stay listed. Each goes as the × on it would (its file goes, Claude Code keeps its history).</summary>
    [RelayCommand]
    void ClearFinished()
    {
        foreach (var a in Agents.Where(IsFinished).ToList()) Forget(a);
    }

    /// <summary>Whether the agent has finished its work and is only sitting in the list.</summary>
    public bool IsFinished(AgentViewModel a) =>
        a.IsDone || (a.State == AgentState.Saved && a.CardDone && a.FinishedTurn)
        || (Replaced(a) && Stopped(a)) || TaskOver(a);

    /// <summary>A task agent that isn't running (WOR-0183): its turn ended, it was cut off mid-turn and isn't carrying on, or
    /// it never got past its prompt. Its card waiting on the person keeps nothing: the decision is on the card, answered
    /// there (a free-text answer starts a fresh card agent). Held only while Darg asks it for a missing decision.
    /// Not a work or rework agent cut off mid-turn (WOR-0221): until its card's place is known it may carry on
    /// (<see cref="ResumeCutOff"/>), and one cut off again on the turn Darg resumed it for stays listed, saved, for the person.</summary>
    bool TaskOver(AgentViewModel a) => IsTask(a) && Stopped(a) && !_held.Contains(a.Id) && !a.CutOff && !a.LeftCutOff;

    /// <summary>A card agent doing one task (work, rework, QA, improve): once it stops the task is over and the agent has
    /// no place in the list (WOR-0183, Philip 2026-11-22: once a task, QA, building or whatever, has finished, there
    /// shouldn't be an agent in the list). What it found is in the card and its reports.</summary>
    static bool IsTask(AgentViewModel a) =>
        a.CardId is not null && a.Purpose is AgentPurposes.Work or AgentPurposes.Rework or AgentPurposes.Qa or AgentPurposes.Improve;

    /// <summary>Not running and with nothing to show: done with its turn, cut off partway or saved, and not at work, waiting
    /// on the person, set to carry on after a usage reset (all <see cref="AgentViewModel.IsLiveOnCard"/>) or failed.</summary>
    static bool Stopped(AgentViewModel a) =>
        a.State is AgentState.Idle or AgentState.Saved && !a.IsLiveOnCard && !a.EndedInError;

    /// <summary>Task agents kept while Darg waits on something from them (a decision it asked for).</summary>
    readonly HashSet<string> _held = [];

    /// <summary>Keep a task agent listed after it stops, until <see cref="Release"/>: its card landed in For Human
    /// Attention with no decision yet, and it is the one asked to write it.</summary>
    public void Hold(AgentViewModel a) => _held.Add(a.Id);

    /// <summary>Done waiting on the agent: it goes now if its task is over.</summary>
    public void Release(AgentViewModel? a)
    {
        if (a is null || !_held.Remove(a.Id)) return;
        if (Agents.Contains(a) && TaskOver(a)) Forget(a);
    }

    /// <summary>Whether a card agent's work has been taken over (WOR-0183): a newer work, rework or QA agent is on its card.
    /// What it did is in the card, its reports and the newer agent's prompt; it only clutters the list.</summary>
    public bool Replaced(AgentViewModel a) =>
        a.CardId is not null && a.Purpose is null or AgentPurposes.Work or AgentPurposes.Rework or AgentPurposes.Qa or AgentPurposes.Improve
        && Agents.Any(b => b != a && b.CardId == a.CardId && b.Purpose is AgentPurposes.Work or AgentPurposes.Rework or AgentPurposes.Qa && b.Created > a.Created);

    /// <summary>A new work, rework or QA agent has started on a card (WOR-0183): the older agents on it that have stopped
    /// (turn ended or cut off) leave the list; one still at work, waiting on the person or out of usage goes once its turn
    /// ends well, and a failed one stays.</summary>
    void ForgetReplacedBy(AgentViewModel newer)
    {
        foreach (var a in Agents.Where(a => a != newer && a.CardId == newer.CardId && a.Purpose is null or AgentPurposes.Work or AgentPurposes.Rework or AgentPurposes.Qa or AgentPurposes.Improve).ToList())
        {
            if (Stopped(a)) Forget(a);
            else if (a.IsLiveOnCard) _goWhenFinished.Add(a.Id);
        }
    }

    // ---- Finished agent reports (WOR-0061): an agent that filed a report is forgotten once its turn ends well.

    /// <summary>Agents that go once their turn ends well: they filed a report, or their card reached done mid-turn.</summary>
    readonly HashSet<string> _goWhenFinished = [];

    /// <summary>The reports in the project's folder: every agent that wrote one is forgotten when it's finished, at once if it
    /// is already, else when its turn ends well (one that fails or asks something stays, for the person to see).
    /// A report that doesn't name its agent stands for the agents on its card that were started before it was written.
    /// A QA report (WOR-0088) forgets nobody: its agent stays for the person to talk to about what they find.</summary>
    public void Reported(IEnumerable<AgentReport> reports)
    {
        foreach (var r in reports.Where(r => !r.Qa))
            foreach (var a in Agents.Where(a => Wrote(a, r)).ToList())
            {
                _goWhenFinished.Add(a.Id);
                if (Finished(a)) Forget(a);
            }
    }

    // ---- A card reaching done (WOR-0131): its agents close and leave the list, so done cards don't keep them.

    /// <summary>A card has reached done (after its done report): every agent on it that has settled goes now, its session
    /// closed and off the list as the × does. One still at it (working, waiting on the person, or set to carry on after a
    /// usage reset; a QA or card agent that moved the card itself is mid-turn) goes once its turn ends well; if that turn
    /// fails it stays, for the person to see. Returns how many went now.</summary>
    public int CardFinished(string cardId)
    {
        var gone = 0;
        foreach (var a in Agents.Where(a => a.CardId == cardId).ToList())
        {
            if (a.IsLiveOnCard) { _goWhenFinished.Add(a.Id); continue; }
            Forget(a);
            gone++;
        }
        return gone;
    }

    /// <summary>The one-off clean-up (WOR-0131): every settled agent on a done card goes, as <see cref="CardFinished"/>
    /// does for each; ones still at it go when their turn ends. Returns how many went now.</summary>
    public int ClearDoneCards(IEnumerable<string> doneCardIds) =>
        doneCardIds.Distinct().Sum(CardFinished);

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
        agent.McpArgs ??= McpLaunch;
        agent.MistakeLogFile ??= MistakeLog.PathFor(project.Id);
        agent.ProjectName ??= project.Name;
        agent.CardDone = IsCardDone(agent.CardId);
        agent.OpenCard = id => OpenCard?.Invoke(id) == true;
        agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentViewModel.CanContinueAfterReset)) AnyOutOfUsage = Agents.Any(a => a.CanContinueAfterReset);
            if (e.PropertyName is nameof(AgentViewModel.State) or nameof(AgentViewModel.TurnFailed) && SortByStatus) Resort();
            if (e.PropertyName is nameof(AgentViewModel.State) or nameof(AgentViewModel.TurnFailed) && agent.CardId is not null) CardAgentChanged?.Invoke(agent);
            if (e.PropertyName != nameof(AgentViewModel.State)) return;
            // it filed a report, or its card reached done, and its turn has now ended well: it goes (after this change has been handled)
            if (_goWhenFinished.Contains(agent.Id) && Finished(agent) || TaskOver(agent)) Dispatcher.UIThread.Post(() => { if (Agents.Contains(agent) && (Finished(agent) && _goWhenFinished.Contains(agent.Id) || TaskOver(agent))) Forget(agent); });
            NeedingYou = Agents.Any(a => a.State == AgentState.NeedsYou);
            CountRunning();
            Fill(); // one stopped: the next pending one starts (WOR-0245)
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

    /// <summary>The ids of the cards done on the project's boards, all of them (WOR-0153): set by the project, null: no board here.</summary>
    public Func<IReadOnlySet<string>>? DoneCardIds { get; set; }
    bool IsCardDone(string? cardId) => cardId is not null && DoneCardIds?.Invoke().Contains(cardId) == true;
    /// <summary>Shows a card on the board (WOR-0143); false when it isn't on the board shown.</summary>
    public Func<string, bool>? OpenCard { get; set; }

    /// <summary>Whether a card is still being worked: in its board's working or needs-more-work column (set by the project,
    /// <see cref="CutOffTurn.StillWorked"/>). Null: no boards, and a cut-off agent stays saved.</summary>
    public Func<string, bool>? CardStillWorked { get; set; }

    /// <summary>Work and rework agents Darg closed, crashed or updated on mid-turn (WOR-0221), once the boards are read: one
    /// whose card is still being worked and that no newer agent on the card has replaced resumes its own session with a
    /// carry-on message; one already resumed by Darg on that turn stays saved; the rest go as finished tasks (WOR-0183).</summary>
    void ResumeCutOff()
    {
        if (CardStillWorked is null || _closed) return;
        foreach (var a in Agents.Where(a => a.CutOff).ToList())
            a.SeeToCutOff(!Replaced(a) && CardStillWorked(a.CardId!));
    }

    /// <summary>Whether the boards have been read yet (set by the project): until they have, <see cref="RefreshCards"/> waits
    /// for the read's own call rather than read every board on the UI thread (WOR-0227).</summary>
    public Func<bool>? BoardsRead { get; set; }

    /// <summary>Re-read every card agent's card after the boards change (a card moved to Done shows on its agent). Called as
    /// each read of the boards lands (WOR-0227), and answered from that read: it reads no file. An agent it forgets only
    /// redraws the board's tiles, so this isn't run again inside itself.</summary>
    public void RefreshCards()
    {
        if (BoardsRead?.Invoke() == false) return;
        using var timing = StallWatch.Shared.Section("Agents.RefreshCards");
        if (CardBoards is not null) { _cardBoards = CardBoards(); SyncTabs(); Resort(); }
        var done = DoneCardIds?.Invoke(); // every board's, not only the one shown: an agent's card may be on another (WOR-0153)
        foreach (var a in Agents) if (a.CardId is not null) a.CardDone = done?.Contains(a.CardId) == true;
        ResumeCutOff();
        Fill(); // after the resumes: they are at work, and count against the cap (WOR-0245)
        // a task agent that isn't running has finished its task, ended or cut off: it leaves the list (a failed one stays)
        foreach (var a in Agents.Where(TaskOver).ToList()) Forget(a);
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
    /// <summary>How many agents here are starting or at work (WOR-0164), shown quietly on the rail's Agents icon.
    /// One waiting on the person isn't counted: it has the "!" instead.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AnyRunning))] public partial int RunningCount { get; set; }
    public bool AnyRunning => RunningCount > 0;
    void CountRunning() => RunningCount = Running();
    /// <summary>The project's agents at work: starting (a model being picked too) or mid-turn. What its cap counts (WOR-0245).</summary>
    int Running() => Agents.Count(a => a.Working);

    // ---- The project's cap (WOR-0245, AgentCap): agents asked for past it wait here, first asked first started.
    readonly List<AgentViewModel> _pending = [];
    bool _filling;

    /// <summary>The agents waiting to start, in the order they will.</summary>
    public IReadOnlyList<AgentViewModel> Pending => _pending.Where(a => a.IsPending).ToList();

    /// <summary>Start pending agents while the project has room under its cap (all of them with no cap), and tell the rest
    /// their place in line. Run whenever an agent's state changes, one is forgotten, or the cap changes.</summary>
    void Fill()
    {
        if (_filling || _closed) return;
        _filling = true;
        try
        {
            _pending.RemoveAll(a => !a.IsPending || !Agents.Contains(a));
            for (var free = AgentCap.Free(project.MaxAgents, Running()); free > 0 && _pending.Count > 0; free--)
            {
                var next = _pending[0];
                _pending.RemoveAt(0);
                next.Go(); // starting from here, so it counts as at work at once
            }
            var running = Running();
            for (var i = 0; i < _pending.Count; i++)
            {
                _pending[i].PendingPlace = i + 1;
                _pending[i].Activity = AgentCap.Why(project.MaxAgents, running);
            }
        }
        finally { _filling = false; }
    }

    /// <summary>The project's cap was changed (Setup, or the API's max-agents): raised or taken off, waiting agents start now.</summary>
    public void CapChanged() => Fill();
    public event Action<AgentViewModel>? AgentNeedsYou;
    /// <summary>Something the person needs to hear about (a question, a permission ask, or the end of a long turn;
    /// see <see cref="Decisions"/>): (agent, what happened, detail).</summary>
    public event Action<AgentViewModel, string, string>? Notify;
    public event Action<AgentViewModel>? AsksSettled;
    /// <summary>A card's agent changed state (WOR-0214: the board's split by state regroups its card).</summary>
    public event Action<AgentViewModel>? CardAgentChanged;
    public AgentViewModel? Find(string id) => Agents.FirstOrDefault(a => a.Id == id);
    /// <summary>The newest agent started on a card that hasn't ended.</summary>
    public AgentViewModel? ForCard(string cardId) =>
        Agents.FirstOrDefault(a => a.CardId == cardId && a.IsLiveOnCard);
    /// <summary>A QA agent (Run QA) is at work on the card, or waiting on the person: one that has finished its turn
    /// doesn't count, so Run QA is offered again (WOR-0110).</summary>
    public bool QaRunningFor(string cardId) =>
        Agents.Any(a => a.CardId == cardId && a.Purpose == AgentPurposes.Qa && a.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou or AgentState.Pending);

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

    /// <summary>A plan's planner (WOR-0176): the newest agent started to draft or keep that plan file, if still listed.</summary>
    public AgentViewModel? PlannerFor(string file) =>
        Agents.Where(a => a.Purpose == AgentPurposes.Plan && string.Equals(a.Plan, file, StringComparison.OrdinalIgnoreCase)).MaxBy(a => a.Created);

    /// <summary>The plan files planners are drafting or keeping: names a new plan mustn't take, though not on disk yet.</summary>
    public IEnumerable<string> PlanFiles => Agents.Where(a => a.Purpose == AgentPurposes.Plan && a.Plan is not null).Select(a => a.Plan!);

    /// <summary>Start a planner on a plan file (purpose plan; its model picked by its profile rules, kind plan).</summary>
    public AgentViewModel StartPlanner(string prompt, string file, string name, string idea, IReadOnlyList<string> dirs)
    {
        var a = Start(prompt, Folder, name: name, job: new AgentJob(JobKinds.Plan, idea), dirs: dirs, purpose: AgentPurposes.Plan);
        a.Plan = file;
        a.Save();
        return a;
    }

    /// <summary>List the planner chat a plan names (its <c>planner:</c> session, WOR-0199) when none listed has the plan: a
    /// fresh instance's agents know nothing of it, but Claude Code still has the conversation, so a message resumes it.
    /// Given the args a planner starts with (artifacts, the other repos, the plan and board folders). Null once closed.</summary>
    public AgentViewModel? AdoptPlanner(string file, string sessionId, string name, IReadOnlyList<string> dirs)
    {
        if (_closed) return null;
        var id = NewId();
        var folder = project.WorkingFolder();
        var now = DateTime.Now;
        var record = new AgentRecord
        {
            Id = id, Name = name, Cwd = folder, Purpose = AgentPurposes.Plan, Plan = file, SessionId = sessionId, Created = now, Updated = now,
            Args = [.. ArtifactArgs(id), .. RepoArgs(folder), .. dirs.SelectMany(d => new[] { "--add-dir", d })],
            Items = [new ItemRecord { Kind = "note", Text = $"The planner chat {Path.GetFileName(file)} names (session {sessionId}). Send a message to carry it on." }],
        };
        var agent = new AgentViewModel(record, _store);
        Add(agent, select: false);
        agent.Save();
        return agent;
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
        NewAgentStarted?.Invoke(); // the popup box closes (WOR-0240)
    }
    /// <summary>Raised when the new-agent box starts an agent, so its popup closes.</summary>
    public event Action? NewAgentStarted;

    /// <param name="job">What it's for, which picks its model profile; a chat with the prompt when not given.</param>
    /// <param name="id">The agent's id, when its prompt has to name it (a card agent's report does); a new one otherwise.</param>
    /// <param name="dirs">More folders it may work in (--add-dir), beyond the project's repos.</param>
    public AgentViewModel Start(string prompt, string? cwd = null, string? cardId = null, string? name = null, AgentJob? job = null, string? id = null,
        IReadOnlyList<string>? dirs = null, string? purpose = null)
    {
        using var timing = StallWatch.Shared.Section("Agents.Start");
        id ??= NewId();
        job ??= new AgentJob(JobKinds.Chat, prompt);
        if (_closed)
        {
            // asked by work still queued when the project closed: an agent that never starts, listed nowhere
            var gate = new TaskCompletionSource<ProfilePick?>();
            var never = new AgentViewModel(project.WorkingFolder(), prompt, cardId, name ?? ChatTitle.Guess(prompt), id: id, profile: ProfilePicking.Of(gate.Task)) { Purpose = purpose };
            never.Shut();
            gate.SetResult(null);
            return never;
        }
        // off the UI thread: reading profiles.md, and Haiku's pick, whose process start and prompt write are
        // synchronous (one per card when several start at once, WOR-0076)
        var forJob = job with { Project = job.Project ?? project.Id };
        var profile = ModelProfiles.StartPick(forJob);
        var folder = string.IsNullOrWhiteSpace(cwd) ? project.WorkingFolder() : cwd;
        // the project's cap reached, or others already waiting: it waits its turn (WOR-0245)
        var wait = AgentCap.MustWait(project.MaxAgents, Running(), _pending.Count(a => a.IsPending));
        var agent = new AgentViewModel(folder, prompt, cardId, name ?? ChatTitle.Guess(prompt),
            args: [.. ArtifactArgs(id), .. RepoArgs(folder), .. (dirs ?? []).SelectMany(d => new[] { "--add-dir", d })], id: id, profile: profile, mcp: McpLaunch,
            pending: wait)
            { StoreFolder = _store, Purpose = purpose };
        Add(agent, select: true);
        if (wait) { _pending.Add(agent); Fill(); }
        if (cardId is not null && purpose is AgentPurposes.Work or AgentPurposes.Rework or AgentPurposes.Qa) ForgetReplacedBy(agent);
        agent.Save();
        if (name is null) _ = Retitle(agent, prompt); // a card's agent is already named for the card
        return agent;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>The MCP servers ticked on this project's Context page, as flags (WOR-0208): read when each session starts.</summary>
    IReadOnlyList<string> McpLaunch() => McpServers.LaunchArgs(project);

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
    /// <param name="blockers">Lines naming the cards it waits on, their columns and newest reports (<see cref="BlockedStart.PromptLines"/>).</param>
    public static string CardPrompt(string projectName, Card card, string file, IReadOnlyList<string>? alongside = null, string? repo = null, string? commits = null,
        string? finish = null, IReadOnlyList<string>? blockers = null, IReadOnlyList<string>? plan = null)
    {
        var lines = new List<string> { $"Work on this card from the {projectName} board.", "", $"{card.Id}: {card.Title}" };
        if (card.IsBug) lines.Add("Type: Bug. First check it still happens (if it doesn't, see CNR below). Find the cause before changing anything, and add a test that fails without the fix where one can be written.");
        else if (!card.Kind.Equals(CardTypes.Task, StringComparison.OrdinalIgnoreCase)) lines.Add($"Type: {card.Kind}");
        if (card.Labels.Count > 0) lines.Add($"Labels: {string.Join(", ", card.Labels)}");
        if (card.Priority.Length > 0) lines.Add($"Priority: {card.Priority}");
        lines.Add($"Card file: {file}");
        if (card.Description.Length > 0) { lines.Add(""); lines.Add(card.Description); }
        if (blockers is { Count: > 0 }) { lines.Add(""); lines.AddRange(blockers); }
        if (plan is { Count: > 0 }) { lines.Add(""); lines.AddRange(plan); }
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

/// <summary>Pending (WOR-0245): asked for, but waiting to start under its project's cap (<see cref="AgentCap"/>).</summary>
public enum AgentState { Starting, Working, NeedsYou, Idle, Exited, Saved, Pending }

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
    public bool HasCard => !string.IsNullOrEmpty(CardId);
    public string CardLabel => $"↗ {CardId}";
    /// <summary>Set by the agents list: shows the card; false when it isn't on the board shown.</summary>
    public Func<string, bool>? OpenCard { get; set; }
    [RelayCommand]
    void ShowCard()
    {
        if (CardId is not { } id) return;
        if (OpenCard?.Invoke(id) != true) Activity = $"{id} isn't on the board shown now.";
    }
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
    /// <summary>For a planner (WOR-0176): the plan file it drafts and keeps, so the Design page shows its chat beside it.</summary>
    public string? Plan { get; set; }
    readonly List<string> _args;
    DateTime _created = DateTime.Now;
    public DateTime Created => _created;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText), nameof(Working), nameof(NeedsYou), nameof(Open), nameof(IsDone), nameof(IsFailed), nameof(IsPending))]
    public partial AgentState State { get; set; }
    /// <summary>Waiting to start under its project's cap (WOR-0245): asked for, but no session yet.</summary>
    public bool IsPending => State == AgentState.Pending;
    /// <summary>Its place among the project's pending agents, from 1 (set by the project as the line moves); 0: not pending.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText))] public partial int PendingPlace { get; set; }
    /// <summary>Darg closed its idle process to free memory (WOR-0209): listed and resumable, not finished.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText))] public partial bool Resting { get; set; }

    partial void OnStateChanged(AgentState value) => ArmIdle();

    DispatcherTimer? _idleTimer;

    /// <summary>An agent that sits idle gives its claude process (about 0.5 GB, plus its MCP servers) back after
    /// <see cref="IdleClose.Limit"/> (WOR-0209); a message then resumes the session as one saved by a restart is.</summary>
    void ArmIdle()
    {
        _idleTimer?.Stop();
        var limit = IdleClose.Limit;
        if (State != AgentState.Idle || _session is null || limit <= TimeSpan.Zero) return;
        _idleTimer ??= new DispatcherTimer(DispatcherPriority.Background);
        _idleTimer.Interval = limit;
        _idleTimer.Tick -= IdleTick;
        _idleTimer.Tick += IdleTick;
        _idleTimer.Start();
    }

    void IdleTick(object? sender, EventArgs e)
    {
        _idleTimer?.Stop();
        // only a quiet one: no ask pending, not waiting out a usage limit (State is Idle then too), and not while
        // the person has a message half typed (Fettle's live chat): look again after another interval
        if (State != AgentState.Idle || _session is null || LimitHit || ContinueAt is not null
            || Items.OfType<IPending>().Any(p => p.Pending)) return;
        if (Compose.Length > 0) { ArmIdle(); return; }
        if (CloseSession("Darg (idle)")) Resting = true;
    }

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

    partial void OnModelChoiceChanged(AgentChoice value)
    {
        if (Picked(ref _model, value, s => s.SetModel(value.Value))) { _modelByHand = true; _runModel = _model; }
    }
    partial void OnEffortChoiceChanged(AgentChoice value)
    {
        if (Picked(ref _effort, value, s => s.SetEffort(value.Value))) { _effortByHand = true; _runEffort = _effort; }
    }
    partial void OnModeChoiceChanged(AgentChoice value) => Picked(ref _mode, value, s => s.SetPermissionMode(value.Value));

    /// <summary>A pick made by hand: kept, and told to a running session. False when the session said so (nothing to tell it).</summary>
    bool Picked(ref string? field, AgentChoice? value, Action<AgentSession> tell)
    {
        if (_fromSession || value is null) return false;
        field = value.Value == AgentChoices.Default ? null : value.Value;
        _handPicked = true;
        if (_session is not null) tell(_session);
        Save();
        return true;
    }

    // Optimise tokens (WOR-0159): what the session runs on, which is _model and _effort lowered while the
    // header's toggle is on (a pick made by hand isn't). _model and _effort stay what it was given, so turning
    // the toggle off puts it back.
    bool _modelByHand, _effortByHand;
    string? _runModel, _runEffort, _defaultModel;

    (string? Model, string? Effort) Running() => TokenSaver.Running(_model, _effort, _defaultModel, _modelByHand, _effortByHand);

    /// <summary>The Optimise tokens toggle changed: a running session switches from its next turn, with a line in the
    /// transcript saying so; one not running picks it up when it starts.</summary>
    public void ApplyTokenSaver()
    {
        if (_session is not { } session) return;
        var (model, effort) = Running();
        if (model == _runModel && effort == _runEffort) return;
        if (model != _runModel) session.SetModel(model ?? AgentChoices.Default);
        if (effort != _runEffort) session.SetEffort(effort ?? AgentChoices.Default);
        _runModel = model;
        _runEffort = effort;
        Items.Add(new NoteItem(TokenSaver.On
            ? $"Optimise tokens on: {TokenSaver.Describe(model, effort)} from the next turn (it was given {TokenSaver.Describe(_model, _effort)})"
            : $"Optimise tokens off: back to {TokenSaver.Describe(model, effort)} from the next turn", false));
    }

    /// <summary>Say in the model picker what "default" is, keeping the pick as it was.</summary>
    void NameDefaultModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        _defaultModel = model;
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
    public bool IsLiveOnCard => State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou or AgentState.Pending || ((LimitHit || ContinueAt is not null) && State is AgentState.Idle or AgentState.Saved);
    /// <summary>Its turn ended in an error, or its session died or never started.</summary>
    public bool IsFailed => (State == AgentState.Idle && TurnFailed) || State == AgentState.Exited;
    public string StateText => State switch
    {
        AgentState.Starting => "starting",
        AgentState.Working => Steps switch { 0 => "working", 1 => "working · 1 step", _ => $"working · {Steps} steps" },
        AgentState.NeedsYou => "needs you",
        AgentState.Pending => AgentCap.State(PendingPlace),
        AgentState.Idle or AgentState.Saved when ContinueAt is { } at => $"out of usage · carries on {When(at)}",
        AgentState.Idle when LimitHit => "out of usage",
        AgentState.Idle => TurnFailed ? "stopped with an error" : "done",
        AgentState.Saved when CardDone && FinishedTurn => "Complete, pending removal",
        AgentState.Saved when Resting => "resting · send to carry on",
        AgentState.Saved => "saved · send to carry on", _ => "ended",
    };

    /// <summary>Its card is done (in Completed or the board's last column). Set by the project as boards load.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText))] public partial bool CardDone { get; set; }

    /// <summary>Its last turn ended normally ("Turn done" is the transcript's last line), so it wasn't stopped partway.
    /// A close after that turn (Darg closing a QA agent that's done, Close, Close idle) doesn't change how it ended
    /// (WOR-0153: it hid the turn's end, so a QA agent that passed its card was never forgotten); a close mid-turn does.</summary>
    public bool FinishedTurn =>
        Items.LastOrDefault(i => !(i is NoteItem { Bad: false } c && ClosedAfterTurn(c.Text))) is NoteItem { Bad: false } n
        && n.Text.StartsWith("Turn done", StringComparison.Ordinal);

    /// <summary>Its last turn ended in an error, or its session died or never started: what the person is left to see.
    /// A saved one reads it from its transcript (an error note last, or just before that turn's "Turn done"), since a
    /// restored agent doesn't keep <see cref="TurnFailed"/>. One cut off mid-turn (a crash, an update) didn't fail:
    /// its transcript just stops (WOR-0183).</summary>
    public bool EndedInError => State switch
    {
        AgentState.Exited => true,
        AgentState.Idle => TurnFailed,
        AgentState.Saved => TurnFailed || SavedInError(),
        _ => false,
    };

    bool SavedInError()
    {
        var i = Items.Count - 1;
        while (i >= 0 && Items[i] is NoteItem { Bad: false } c && ClosedAfterTurn(c.Text)) i--;
        if (i >= 0 && Items[i] is NoteItem { Bad: false } n && n.Text.StartsWith("Turn done", StringComparison.Ordinal)) i--;
        return i >= 0 && Items[i] is NoteItem { Bad: true };
    }

    const string ClosedTail = ". Send a message to carry on.";

    /// <summary>The note <see cref="CloseSession"/> leaves when it closes a session that wasn't mid-turn.</summary>
    static bool ClosedAfterTurn(string note) =>
        note.StartsWith("Session closed", StringComparison.Ordinal) && note.EndsWith(ClosedTail, StringComparison.Ordinal)
        && !note.EndsWith(" mid-turn" + ClosedTail, StringComparison.Ordinal);

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

    /// <summary>What a work or rework agent cut off mid-turn is sent when Darg opens again (WOR-0221).</summary>
    public const string CutOffMessage = "Darg closed while you were mid-turn (a crash, a close or an update), so your last turn was cut off. "
        + "Carry on with what you were doing where you left off: check what you had already done before going on.";
    /// <summary>The line its transcript gets, saying why it carries on by itself.</summary>
    public const string CutOffNote = "Cut off mid-turn when Darg last closed: resuming its session to carry on.";

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
    /// <param name="profile">The model profile for its job, still being picked (WOR-0034): the first message goes once
    /// it is in, on that profile's model and effort (while Haiku picks, the session is already starting, WOR-0233).
    /// Null: start now on the person's own settings.</param>
    /// <param name="spare">WOR-0235: offered the folder and full arguments its first session would start with, gives back a
    /// session already started with exactly those (a spare, Fettle's), or null to start one as ever.</param>
    public AgentViewModel(string cwd, string prompt, string? cardId = null, string? name = null, IEnumerable<string>? args = null, string? shown = null, string? id = null,
        ProfilePicking? profile = null, Func<IReadOnlyList<string>>? mcp = null, Func<string, IReadOnlyList<string>, AgentSession?>? spare = null,
        bool pending = false)
    {
        if (id is not null) Id = id;
        if (pending) { _slot = new(TaskCreationOptions.RunContinuationsAsynchronously); PendingMessage = prompt; State = AgentState.Pending; }
        McpArgs = mcp;
        _takeSpare = spare;
        _firstClock = new AgentTimings(Id); // from now, so the model pick counts in its first turn (WOR-0206)
        Cwd = cwd;
        CardId = cardId;
        _args = args?.ToList() ?? [];
        NameDefaultModel(AgentChoices.DefaultModel(cwd));
        _mode = AgentChoices.NewAgentMode; // saved with it, so a resume stays in Auto too
        ShowMode(_mode);
        var visible = shown ?? prompt;
        Name = name ?? (visible.Length > 40 ? visible[..40].TrimEnd() + "…" : visible);
        Items.Add(new UserItem(visible));
        if (profile is null && _slot is null) Begin(prompt, resume: false);
        else StartOnProfile(profile ?? ProfilePicking.Of(Task.FromResult<ProfilePick?>(null)), prompt);
    }

    // ---- Pending (WOR-0245): its project had as many agents at work as its cap allows when it was asked for. It waits,
    // its model picked meanwhile, until the project lets it go (Go) as others stop; its first message is saved with it,
    // so a restart keeps it waiting in its place.

    TaskCompletionSource? _slot;   // completed when it may start; null: it was never held, or has been let go
    /// <summary>The first message it will be sent once it starts; null: not pending.</summary>
    public string? PendingMessage { get; private set; }

    /// <summary>The project lets it start (a slot came free, or Start now was pressed). It shows as starting at once, so
    /// the project counts it as at work before its session is up.</summary>
    public void Go()
    {
        if (_slot is not { } slot) return;
        _slot = null;
        PendingPlace = 0;
        if (State == AgentState.Pending) { State = AgentState.Starting; Activity = "Starting…"; }
        slot.TrySetResult();
    }

    /// <summary>Start now: the person starts a pending agent past the cap.</summary>
    [RelayCommand]
    void StartNow() => Go();

    /// <summary>Wait for <see cref="Go"/>. False: it was ended meanwhile (forgotten, or its project closed).</summary>
    async Task<bool> WaitForSlot(string message)
    {
        if (_slot is not { } slot) return !_ended;
        PendingMessage = message;
        State = AgentState.Pending;
        Save();
        await slot.Task;
        PendingMessage = null;
        return !_ended;
    }

    /// <summary>A pending agent loaded from an earlier run: it waits again, then sends what it was to start with.</summary>
    async void WaitAgain(string message)
    {
        if (!await WaitForSlot(message)) return;
        Begin(message, resume: false);
        Save();
    }

    /// <summary>The model profile it started on ("Deep"), if one was picked.</summary>
    [ObservableProperty] public partial string Profile { get; set; } = "";
    /// <summary>Its profile has <c>Hooks: off</c> (WOR-0236): its sessions start without the person's Claude Code hooks.</summary>
    public bool NoHooks { get; private set; }
    bool _handPicked, _ended;
    // where its first session may come from ready started (WOR-0235); asked once, by the first Begin
    Func<string, IReadOnlyList<string>, AgentSession?>? _takeSpare;
    // the clock its first session takes over: started when the agent was made (WOR-0206); a resumed session starts its own
    AgentTimings? _firstClock;

    /// <summary>Wait for the profile (a rule's is there at once; Haiku's takes a few seconds), take its model
    /// and effort unless the person picked their own meanwhile, then send the first message. While Haiku picks,
    /// claude is already starting (WOR-0233) on the person's own settings: Claude Code starts up (node, SessionStart
    /// hooks, MCP servers) before any message comes, and takes set_model and apply_flag_settings before the first,
    /// so the start-up overlaps the pick and the picked model and effort are sent ahead of the held message. A rule's
    /// pick (known once profiles.md is read) starts it on the profile's own command line as before, so a spare session
    /// (WOR-0235) still fits and Hooks: off (WOR-0236) is on it from the start.</summary>
    async void StartOnProfile(ProfilePicking picking, string prompt)
    {
        ProfilePick? pick = null;
        var clock = _firstClock; // Begin hands it to the session
        clock?.PickStarted();
        AgentSession? early = null;
        var earlyNoHooks = false;
        // a pending agent's model is picked while it waits (WOR-0245), and it starts no claude early: the cap is on processes
        if (_slot is not null) State = AgentState.Pending;
        else if (!picking.Result.IsCompleted)
        {
            _whilePicking = []; // what's sent meanwhile goes after the first message
            State = AgentState.Starting;
            Activity = "Picking a model…";
            HaikuWait? wait = null;
            try { wait = await picking.Waiting; } catch (Exception) { }
            if (wait is not null && !_ended && !_shut && !picking.Result.IsCompleted)
            {
                NoHooks = earlyNoHooks = wait.NoHooks;
                Begin(null, resume: false);
                early = _session;
                if (early is null) { _whilePicking = null; return; } // couldn't start claude: it says so, and the pick changes nothing
                Activity = "Picking a model…";
            }
        }
        try { pick = await picking.Result; } catch (Exception) { }
        clock?.PickEnded(pick?.Profile.Name, pick?.Why);
        var held = _whilePicking ?? [];
        _whilePicking = null;
        if (_ended) return; // closed or forgotten while it was being picked (End ended any claude it had started)
        // hooks go by the profile whatever was picked by hand: the model and effort boxes don't say anything about them
        NoHooks = pick is not null && !pick.Profile.Hooks;
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
        if (early is not null && _session == early && NoHooks != earlyNoHooks)
        {
            // the pick's hooks setting differs from what it started with, which only a new process can take (--settings)
            _session = null;
            _ = early.DisposeAsync().AsTask();
            Items.Add(new NoteItem(NoHooks ? "Started again without hooks, as its profile says." : "Started again with hooks, as its profile says.", false));
            early = null;
            _firstClock = clock;
            BeginWith(prompt, held);
        }
        else if (early is null)
        {
            if (!await WaitForSlot(prompt)) return;
            BeginWith(prompt, held);
        }
        else if (_session is { } session) SendHeld(session, [prompt, .. held]);
        else if (!_shut)
        {
            // its claude ended while the model was picked (an exit that failed already said why)
            if (!Items.OfType<NoteItem>().Any(n => n.Bad))
                Items.Add(new NoteItem("Claude ended while its model was being picked, so nothing was sent. Start a new agent.", true));
            Activity = "Claude ended while its model was being picked.";
            State = AgentState.Exited;
        }
        Save();
    }

    /// <summary>Start its first session with the first message, then what was sent while its model was picked.</summary>
    void BeginWith(string prompt, List<string> held)
    {
        Begin(prompt, resume: false);
        if (_session is { } session) foreach (var m in held) session.Send(m);
    }

    /// <summary>The pick is in (WOR-0233): tell the session started early what to run on, then send the message held for it.
    /// A model or effort picked by hand meanwhile was told to the session at once (<see cref="Picked"/>), so it matches here.</summary>
    void SendHeld(AgentSession session, IReadOnlyList<string> messages)
    {
        var (model, effort) = Running();
        if (model != _runModel) session.SetModel(model ?? AgentChoices.Default);
        if (effort != _runEffort) session.SetEffort(effort ?? AgentChoices.Default);
        (_runModel, _runEffort) = (model, effort);
        NoteTokenSaver(model, effort);
        foreach (var m in messages) session.Send(m); // after the set_model and apply_flag_settings: stdin is written in order
        TurnBegins("Starting…");
        State = AgentState.Starting;
    }

    /// <summary>While its model is picked (its first message held): what the person sent meanwhile, sent after that first
    /// message. Null otherwise.</summary>
    List<string>? _whilePicking;

    /// <summary>Optimise tokens lowers what it starts on: the transcript says so.</summary>
    void NoteTokenSaver(string? runModel, string? runEffort)
    {
        if (runModel != _model || runEffort != _effort)
            Items.Add(new NoteItem($"Optimise tokens is on: starting on {TokenSaver.Describe(runModel, runEffort)} (it was given {TokenSaver.Describe(_model, _effort)})", false));
    }

    /// <summary>The MCP flags each session it starts or resumes gets (WOR-0208, <see cref="McpServers.LaunchArgs"/>), asked
    /// afresh every time so a server ticked on the Context page reaches it from its next start. Never saved with it. Not set:
    /// none at all (--strict-mcp-config alone).</summary>
    public Func<IReadOnlyList<string>>? McpArgs { get; set; }

    /// <summary>Set for Bench's chats: turns what the person typed into what's sent (adds where they are).</summary>
    public Func<string, string>? Wrap { get; set; }

    /// <summary>Send a message that shows as <paramref name="shown"/> but goes to Claude as <paramref name="sent"/>.</summary>
    public void Say(string shown, string sent)
    {
        if (State == AgentState.Pending)
        {
            Items.Add(new NoteItem("It hasn't started yet: it is waiting for one of this project's agents to stop. Press Start now to start it, then send this.", true));
            return;
        }
        Items.Add(new UserItem(shown));
        if (_whilePicking is not null && !_ended) { _whilePicking.Add(sent); return; } // its model is being picked: goes after the first message
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
        Id = r.Id; Name = r.Name; Cwd = r.Cwd; CardId = r.CardId; Purpose = r.Purpose; SessionId = r.SessionId; Project = r.Project; Plan = r.Plan;
        _args = r.Args; _created = r.Created; StoreFolder = storeFolder;
        _model = r.Model; _effort = r.Effort; _mode = r.Mode;
        _modelByHand = r.ModelByHand; _effortByHand = r.EffortByHand; NoHooks = r.NoHooks;
        NameDefaultModel(AgentChoices.DefaultModel(Cwd));
        _fromSession = true;
        ModelChoice = AgentChoices.Find(ModelChoices, r.Model);
        EffortChoice = AgentChoices.Find(AgentChoices.Efforts, r.Effort);
        ModeChoice = AgentChoices.Find(AgentChoices.Modes, r.Mode);
        _fromSession = false;
        foreach (var i in r.Items) Items.Add(FromRecord(i));
        Activity = OneLine(Items.OfType<TextItem>().LastOrDefault()?.Text);
        State = AgentState.Saved;
        _autoResumed = r.AutoResumed;
        // it was waiting to start under its project's cap (WOR-0245): it waits again, in its place
        if (r.Pending is { } first && SessionId is null)
        {
            _slot = new(TaskCreationOptions.RunContinuationsAsynchronously);
            WaitAgain(first);
            return;
        }
        // it was waiting for the usage limit to reset when Darg closed: it still is (or carries on now)
        if (r.ContinueAt is { } at) { LimitHit = true; WaitFor(at); }
        else CutOff = WasCutOff();
    }

    // ---- Cut off mid-turn (WOR-0221): Darg closed, crashed or updated while a work or rework agent was at work on its card.
    // It comes back saved; the project resumes its session with CutOffMessage once its boards are read, if its card is
    // still being worked (AgentsViewModel.ResumeCutOff). Once at most: cut off again on that turn, it is left saved.

    /// <summary>Loaded cut off mid-turn and not yet seen to: a work or rework agent on a card, with a session to resume,
    /// whose transcript just stops (no "Turn done", no error, not closed on purpose by Close or Fettle).</summary>
    public bool CutOff { get; private set; }
    /// <summary>Cut off again on the turn Darg resumed it for: left saved and listed, for the person (a send carries it on).</summary>
    public bool LeftCutOff { get; private set; }
    /// <summary>Darg resumed it by itself and that turn hasn't ended (saved as <see cref="AgentRecord.AutoResumed"/>).</summary>
    public bool AutoResumed => _autoResumed;
    bool _autoResumed;

    bool WasCutOff() =>
        Purpose is AgentPurposes.Work or AgentPurposes.Rework && CardId is not null && SessionId is not null
        && Items.Count > 0 && !FinishedTurn && !EndedInError
        && !(Items[^1] is NoteItem { Bad: false } n && n.Text.StartsWith("Session closed", StringComparison.Ordinal));

    /// <summary>See to a cut-off agent once its card's place is known: resume its session (its card still being worked, and
    /// not resumed by Darg already on the turn that was cut off), leave it saved and listed (it was), or let it go as a
    /// finished task (its card moved on, or a newer agent has it). Returns whether it was resumed.</summary>
    public bool SeeToCutOff(bool stillWorked)
    {
        if (!CutOff) return false;
        CutOff = false;
        if (!stillWorked) return false;
        if (_autoResumed)
        {
            LeftCutOff = true;
            if (!(Items.LastOrDefault() is NoteItem { Text: CutOffAgainNote }))
                Items.Add(new NoteItem(CutOffAgainNote, false));
            Activity = CutOffAgainNote;
            Save();
            return false;
        }
        Items.Add(new NoteItem(CutOffNote, false));
        _autoResumed = true;
        Say("↻ Carry on (Darg closed mid-turn)", CutOffMessage);
        Save(); // marked before its session starts: a crash right now still counts this as its one resume
        return true;
    }

    /// <summary>The line a cut-off agent gets when it was cut off again on the turn Darg resumed it for.</summary>
    public const string CutOffAgainNote = "Cut off mid-turn again on the turn Darg resumed it for: not resumed a second time. Send a message to carry on.";

    /// <summary>Start (or resume) the Claude session and send it a message.</summary>
    /// <summary>Its project is closing (removed in Setup, or a test's app ending): end the session and never start
    /// one again, whatever asks after this: a model pick coming in, a message, carrying on after a usage limit.
    /// Without it, work queued by a closed project started agents in the next test's sandbox (WOR-0131).</summary>
    public void Shut()
    {
        _shut = true;
        End();
    }
    bool _shut;

    /// <param name="message">What to send it; null: start the process and send nothing yet (its model is still being
    /// picked, <see cref="StartOnProfile"/>), so it starts up meanwhile.</param>
    void Begin(string? message, bool resume)
    {
        if (_shut) { Items.Add(new NoteItem("Not started: its project has closed.", false)); return; }
        Resting = false;
        LeftCutOff = false; // carried on: from here it is any other agent at work
        using var timing = StallWatch.Shared.Section("Agent.Begin");
        try
        {
            List<string> args = resume && SessionId is not null ? ["--resume", SessionId, .. _args] : [.. _args];
            var (runModel, runEffort) = Running();
            args.AddRange(AgentChoices.LaunchArgs(runModel, runEffort, _mode));
            IReadOnlyList<string> mcp;
            try { mcp = McpArgs?.Invoke() ?? McpServers.LaunchArgs(null); }
            catch (Exception) { mcp = McpServers.LaunchArgs(null); }
            args.AddRange(mcp);
            if (NoHooks) args.AddRange(ModelProfiles.NoHooksArgs());
            if (message is not null) NoteTokenSaver(runModel, runEffort); // held: said once the pick says what it runs on
            AgentSession session;
            var clock = _firstClock ?? new AgentTimings(Id);
            _firstClock = null;
            var take = resume ? null : _takeSpare;
            _takeSpare = null;
            // a spare started with exactly these args is already past start-up: its first reply skips it (WOR-0235)
            if (take?.Invoke(Cwd, args) is { } spare) { session = spare; session.Retime(clock); }
            else using (StallWatch.Shared.Section("AgentSession.Start")) session = AgentSession.Start(Cwd, extraArgs: args, apiUrl: ArtifactHost.Server?.ApiUrl, timings: clock);
            _session = session;
            (_runModel, _runEffort) = (runModel, runEffort);
            // a session closed on request still reports its last events and its exit: they're not news. Streamed text comes
            // in batches, a post every few frames rather than per token (WOR-0234); each event keeps when it was read, on the
            // reading thread, so how long the transcript takes to show it goes to agents.log (WOR-0224)
            var inbox = new StreamInbox(a => Dispatcher.UIThread.Post(a), events =>
            {
                foreach (var (e, read) in events)
                {
                    if (_session != session) return;
                    On(e);
                    clock.Shown(e, System.Diagnostics.Stopwatch.GetElapsedTime(read));
                }
            });
            inbox.Pace = () => StreamInbox.PaceFor(Volatile.Read(ref _streamedLength)); // asked on the reading thread
            session.Event += inbox.Add;
            if (message is not null)
            {
                session.Send(message); // queued: it never waits for claude to read (WOR-0076)
                TurnBegins("Starting…");
            }
            State = AgentState.Starting;
        }
        catch (Exception e)
        {
            Items.Add(new NoteItem($"Couldn't start Claude: {e.Message}", true));
            Activity = OneLine($"Couldn't start Claude: {e.Message}");
            State = AgentState.Exited;
        }
    }

    /// <summary>Forgotten: off the list and its file deleted. Nothing saves it again (the session's exit, which comes after
    /// <see cref="End"/>, wrote a forgotten agent's file back, WOR-0183).</summary>
    public bool Gone { get; set; }

    /// <summary>Write this agent to its store (after every turn and on exit).</summary>
    public void Save()
    {
        if (StoreFolder is null || Gone) return;
        using var timing = StallWatch.Shared.Section("Agent.Save");
        try
        {
            // the record is a snapshot taken here; it is written in the background (WOR-0076)
            AgentStore.SaveLater(StoreFolder, new AgentRecord
            {
                Id = Id, Name = Name, Cwd = Cwd, CardId = CardId, Purpose = Purpose, Project = Project, Plan = Plan, SessionId = SessionId, Args = [.. _args],
                Model = _model, Effort = _effort, Mode = _mode, ModelByHand = _modelByHand, EffortByHand = _effortByHand, ContinueAt = ContinueAt, AutoResumed = _autoResumed,
                NoHooks = NoHooks, Pending = PendingMessage, Created = _created, Updated = DateTime.Now, Items = Items.Select(ToRecord).OfType<ItemRecord>().ToList(),
            });
        }
        catch (Exception) { /* a failed save must never take the agent down */ }
    }

    static ItemRecord? ToRecord(TranscriptItem i) => i switch
    {
        UserItem u => new() { Kind = "user", Text = u.Text },
        TextItem { Partial: true } => null, // only whole messages are kept (WOR-0234)
        TextItem t => new() { Kind = "text", Text = t.Text },
        NoteItem n => new() { Kind = "note", Text = n.Text, Bad = n.Bad },
        ToolItem t => new() { Kind = "tool", Name = t.Name, Summary = t.Summary, ImagePath = t.ImagePath, Removed = t.Removed, Added = t.Added, Preview = t.Preview, Result = t.Result, Bad = t.IsError,
            Images = t.Gallery.Count > 0 ? t.Gallery.Select(g => g.Path).ToList() : null },
        QuestionItem q => new() { Kind = "note", Text = "Asked: " + string.Join(" / ", q.Questions.Select(g => g.Question + (g.Chosen is null ? "" : $" → {g.Chosen}"))) },
        PermissionItem p => new() { Kind = "note", Text = $"Wanted {p.Tool}: {p.Decision ?? "no answer"}" },
        // an open card decision isn't kept (the board shows it again on its next read); a closed one stays as a line
        CardDecisionItem { Open: false } d => new() { Kind = "note", Text = $"{d.CardId} asked: {d.Question} → {d.Outcome}" },
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
                // what it is, for agents.log's summary (WOR-0224): a chat with Wrap set is one of Fettle's
                _session?.Timings?.Describe(Purpose ?? (Wrap is not null ? "fettle" : AgentPurposes.Chat));
                Model = s.Model;
                if (_model is null && _runModel is null && !_args.Contains("--model")) NameDefaultModel(s.Model); // on the default: this is what it is
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
            case AssistantDelta d:
                // the reply as it's written (WOR-0234): grow the text of its message's block, or start one. Found by its
                // block, not as "the one being written": two streams mixed (turns overlapping) still grow a reply each
                var growing = Streamed(i => i.Block == (d.MessageId, d.Index));
                if (growing is null) Items.Add(growing = new TextItem(d.Text) { Partial = true, Block = (d.MessageId, d.Index) });
                else
                {
                    growing.Text += d.Text;
                    growing.Partial = true;
                }
                Volatile.Write(ref _streamedLength, growing.Text.Length);
                if (Working) Activity = OneLine(growing.Text);
                break;
            case AssistantText t:
                // the whole message replaces the text streamed for it, so it shows once
                if (Streamed(i => Whole(i, t)) is { } partial)
                {
                    partial.Text = t.Text;
                    partial.Partial = false;
                }
                else Items.Add(new TextItem(t.Text) { Block = (t.MessageId ?? "", -1) });
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
                var question = new QuestionItem(q, this, Cwd);
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
                SettleStreamed(); // a reply cut off mid-stream stays as it was shown
                _autoResumed = false; // the turn it was resumed for has ended: a later cut-off resumes again
                OutOfUsage(f);
                break;
            case TurnFinished f:
                SettleStreamed(); // a reply cut off mid-stream (an interrupt) stays as it was shown
                _autoResumed = false;
                if (f.IsError) Items.Add(new NoteItem(f.Text, true));
                // Claude Code reports a cost on every turn, priced as if on the API; on a subscription
                // nothing is charged per turn, so only an API-key session shows it
                // how long it took, from agents.log's clock (WOR-0206): the first turn's counts from the agent being made
                var took = _session?.Timings?.LastTurn is { Length: > 0 } spent ? $" · {spent}" : "";
                Items.Add(new NoteItem(!OnSubscription && f.CostUsd is { } cost ? $"Turn done{took} · ${cost:0.000} (API key)" : $"Turn done{took}", false));
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
                SettleStreamed();
                if (x.ExitCode != 0) Activity = OneLine($"Session ended ({x.ExitCode}) {Last(x.Stderr)}");
                State = SessionId is not null && x.ExitCode == 0 ? AgentState.Saved : AgentState.Exited;
                Save();
                TurnEnded?.Invoke(true);
                break;
        }
    }

    /// <summary>How long the reply streaming now is, so a long one is drawn in fewer, bigger batches (<see cref="StreamInbox.PaceFor"/>).</summary>
    int _streamedLength;

    /// <summary>Whether a whole message is the one a reply was streaming: the same message, and still being written or
    /// what streamed starts it (a message with two text blocks has a whole line for each, and a reply settled when an
    /// overlapping turn ended still gets its whole); a stream that names no message, a partial reply it starts with.</summary>
    static bool Whole(TextItem streamed, AssistantText t)
    {
        var starts = t.Text.StartsWith(streamed.Text.Trim(), StringComparison.Ordinal);
        return t.MessageId is { Length: > 0 } id && streamed.Block.MessageId is { Length: > 0 } was
            ? id == was && (streamed.Partial || starts)
            : streamed.Partial && starts;
    }

    /// <summary>The newest streamed reply among the transcript's last few items that matches (WOR-0234), without scanning a
    /// long transcript; null when none does.</summary>
    TextItem? Streamed(Func<TextItem, bool> match)
    {
        for (var i = Items.Count - 1; i >= 0 && i >= Items.Count - 20; i--)
            if (Items[i] is TextItem { Block.MessageId: not null } t && match(t)) return t;
        return null;
    }

    /// <summary>The turn ended: a reply still being written (cut off by an interrupt, its whole never sent) is as whole as
    /// it will get, so it settles as it stands: kept, saved and no longer read again by the phone.</summary>
    void SettleStreamed()
    {
        for (var i = Items.Count - 1; i >= 0 && i >= Items.Count - 20; i--)
            if (Items[i] is TextItem { Partial: true } t) t.Partial = false;
    }

    /// <summary>Whether a turn stopped because the usage limit was hit (the error text says so, or it failed after
    /// Claude Code said the limit was hit).</summary>
    bool IsLimit(TurnFinished f) =>
        UsageLimit.StoppedByLimit(f.IsError, f.Text, _rejected is not null, Items.OfType<TextItem>().LastOrDefault()?.Text);

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
    internal static string OneLine(string? s, int max = 90)
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
        _autoResumed = false; // the person's own turn: cut off, it is resumed again (WOR-0221)
        Items.Add(new UserItem(text));
        if (_whilePicking is not null && !_ended) { _whilePicking.Add(text); return; } // its model is being picked: goes after the first message
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
            : mid ? $"{who} mid-turn{ClosedTail}" : who + ClosedTail, false)); // FinishedTurn reads past the second
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
        _slot?.TrySetResult(); // a pending one never starts now (let go, it sees it has ended)
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

    // WOR-0169: the decision this agent's card waits on, shown in its transcript too and linked to the card's, so
    // answering either closes both. Not an IPending: the agent isn't waiting on it (its turn has ended), so it
    // neither holds the card nor sets "needs you"; it isn't saved either, the board puts it back on the next read.

    /// <summary>The card decisions shown here that haven't been answered or closed.</summary>
    public IEnumerable<CardDecisionItem> OpenDecisions => Items.OfType<CardDecisionItem>().Where(i => i.Open);

    /// <summary>Show the card's open decision here, answered through <paramref name="decide"/> (the board's Decide:
    /// card id, answer, notes; returns what went wrong or null). One already shown for the card stays if it is the
    /// same decision, and is swapped for this one if the card's was rewritten.</summary>
    public CardDecisionItem ShowDecision(string cardId, CardDecision decision, Func<string, string, string, string?> decide)
    {
        var shown = OpenDecisions.FirstOrDefault(i => i.CardId == cardId);
        if (shown is not null && shown.Decision.Format() == decision.Format()) return shown;
        if (shown is not null) Items.Remove(shown);
        var item = new CardDecisionItem(cardId, decision, decide, this);
        Items.Add(item);
        if (!Working && !NeedsYou) Activity = OneLine($"{cardId} waits on your decision: {decision.Question}");
        return item;
    }

    /// <summary>Whether it waits on the person for an answer to a question of its own (AskUserQuestion) it can still take.</summary>
    public bool AsksQuestion => Open && Items.OfType<QuestionItem>().Any(q => q.Pending);

    /// <summary>WOR-0243: what it waits on the person for and can still take, oldest first: its questions (AskUserQuestion)
    /// and permission asks, the transcript's own items, so its card's panel shows them with the same buttons and a pick
    /// there answers the agent as one in its chat does.</summary>
    public IReadOnlyList<TranscriptItem> PendingAsks => !Open ? [] :
        Items.Where(i => i is QuestionItem { Pending: true } or PermissionItem { Pending: true }).ToList();

    /// <summary>Its card's decision was answered on the card while it was asking in its chat too, as a rule the same
    /// question (WOR-0225): each question still waiting is settled with <paramref name="message"/> rather than left
    /// open on Needs you, where answering it again would only start it on the work Darg has already done.</summary>
    public void SettleQuestions(string shown, string message)
    {
        foreach (var q in Items.OfType<QuestionItem>().Where(q => q.Pending).ToList()) q.Settle(shown, message);
    }

    /// <summary>The card's decision was answered or closed elsewhere: its item here closes, saying how.</summary>
    public void CloseDecision(string cardId, string how)
    {
        foreach (var item in OpenDecisions.Where(i => i.CardId == cardId).ToList()) item.Close(how);
    }
}

/// <summary>A card's decision shown in its agent's transcript (WOR-0169): the same question and options as the card
/// panel's, and picking one here answers the card, as the panel, the API's decide and the phone do. It closes when
/// the card's decision is answered anywhere, or leaves the attention column.</summary>
public sealed partial class CardDecisionItem : TranscriptItem
{
    readonly Func<string, string, string, string?> _decide;
    readonly AgentViewModel _agent;
    public string CardId { get; }
    public CardDecision Decision { get; }
    public string Header => $"{CardId} waits on your decision";
    public string Question => Decision.Question;
    public string Context => Decision.Context;
    public bool HasContext => Context.Length > 0;
    /// <summary>Recommended first, as the card panel has them; each says what picking it does.</summary>
    public IReadOnlyList<QuestionOption> Options { get; }
    /// <summary>How it was closed ("Answered: Works"); null while it is open.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Open))] public partial string? Outcome { get; set; }
    public bool Open => Outcome is null;
    /// <summary>What went wrong with the last pick, or a stay's note; "" when nothing.</summary>
    [ObservableProperty] public partial string Note { get; set; } = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendOwnCommand))] public partial string OwnAnswer { get; set; } = "";

    public CardDecisionItem(string cardId, CardDecision decision, Func<string, string, string, string?> decide, AgentViewModel agent)
    {
        CardId = cardId; Decision = decision; _decide = decide; _agent = agent;
        Options = decision.Options.OrderByDescending(o => o.Recommended)
            .Select(o => new QuestionOption(o.Recommended ? o.Label + " (recommended)" : o.Label,
                (o.Description.Length > 0 ? o.Description + " " : "") + o.ActionText, false, () => Pick(o.Label))).ToList();
    }

    /// <summary>Answer the card's decision with this label (or words of the person's own).</summary>
    public void Pick(string answer)
    {
        if (!Open) return;
        var option = Decision.OptionFor(answer);
        if (_decide(CardId, answer, "") is { } error) { Note = error; return; }
        // a stay leaves the decision open on the card, so it stays open here too
        if (option?.Action == DecisionAction.Stay) { Note = $"{option.Label}: it stays in the attention column for now."; return; }
        Outcome = $"You picked: {option?.Label ?? answer.Trim()}";
        Note = "";
        _agent.Activity = $"{CardId}: you picked {option?.Label ?? answer.Trim()}";
    }

    /// <summary>Closed from outside (answered on the card, moved out of attention, shown on a newer agent): the
    /// agent's row stops saying the card waits on a decision, if that is still what it says.</summary>
    public void Close(string how)
    {
        if (!Open) return;
        Outcome = how; Note = "";
        if (_agent.Activity.StartsWith($"{CardId} waits on your decision", StringComparison.Ordinal))
            _agent.Activity = AgentViewModel.OneLine($"{CardId}: {char.ToLowerInvariant(how[0])}{how[1..]}");
    }

    bool CanSendOwn => Open && !string.IsNullOrWhiteSpace(OwnAnswer);
    [RelayCommand(CanExecute = nameof(CanSendOwn))] void SendOwn() => Pick(OwnAnswer.Trim());
}

public abstract class TranscriptItem : ObservableObject;
public interface IPending { bool Pending { get; } }

public sealed class UserItem(string text) : TranscriptItem { public string Text { get; } = text; }
/// <summary>A reply of the agent's. While it is being written (WOR-0234) it is <see cref="Partial"/>: its text grows as
/// it streams, and the whole message replaces it when that lands; a partial one is never saved.</summary>
public sealed partial class TextItem(string text) : TranscriptItem
{
    [ObservableProperty] string _text = text;
    /// <summary>Still being written: what has streamed so far, not the whole message.</summary>
    public bool Partial { get; set; }
    /// <summary>The stream's message and content block this is the text of, while it is partial.</summary>
    internal (string? MessageId, int Index) Block { get; set; }
}
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

    public QuestionItem(PermissionAsked ask, AgentViewModel agent, string? cwd = null)
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
            // WOR-0184: a picture the question, its options or their descriptions point at shows above it
            var about = text + "\n" + string.Join("\n", (q?["options"] as JsonArray ?? []).Select(o => (string?)o?["label"] + "\n" + (string?)o?["description"]));
            foreach (var path in ImagePaths.Find(about, cwd, 4))
                try { using var f = File.OpenRead(path); group.Pictures.Add(new DecisionPicture(Bitmap.DecodeToWidth(f, 720), path)); }
                catch (Exception) { }
            Questions.Add(group);
        }
    }

    /// <summary>Answered somewhere else (WOR-0225: the same question as its card's decision, answered on the card): the
    /// ask is declined with <paramref name="message"/>, which tells the agent what was decided and what Darg did, and
    /// each question shows <paramref name="shown"/>.</summary>
    internal void Settle(string shown, string message)
    {
        if (Sent) return;
        Sent = true;
        foreach (var g in Questions) g.Chosen ??= shown;
        _agent.Answered(this, s => s.Deny(_ask.RequestId, message));
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
    public List<DecisionPicture> Pictures { get; } = [];
    public bool HasPictures => Pictures.Count > 0;
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

/// <summary>A board's tab above the agents list; a plain click shows only its agents, Ctrl+click adds or removes it. Board null is All.</summary>
public sealed partial class AgentBoardTab(AgentsViewModel agents, string? board) : ObservableObject
{
    public string? Board { get; } = board;
    public string Name => Board ?? "All";
    [ObservableProperty] public partial bool IsShown { get; set; }
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    void Pick() { var ctrl = agents.TabCtrl; agents.TabCtrl = false; agents.PickBoard(Board, ctrl); }
}
