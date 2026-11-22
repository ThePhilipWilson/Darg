using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The Design section (WOR-0175, docs/FEATURE-PLANNING.md "The Design section"): the project's plans in
/// <c>docs/design/</c> as a tree by <c>parent</c>, and the picked one as a page in its parts. The plan file is the
/// source and the page a view and editor of it, as the Notes wall is of its notes: Goals, Not doing and Design are
/// edited as text, a question is answered by tapping an option (an <c>**Answer:**</c> line), and a made card's row
/// shows its column, agent and open blockers read from the board each time, never written into the plan. Edits are
/// left uncommitted (Philip's call, 2026-11-20): the next agent in the repo, or he, commits them.</summary>
public sealed partial class DesignViewModel : ObservableObject
{
    readonly ProjectConfig _config;
    int _generation;
    bool _pending;

    /// <summary>The plans folder, <c>design/</c> in the project's docs folder.</summary>
    public string Folder { get; }
    public ObservableCollection<PlanNode> Tree { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPage))] public partial PlanNode? SelectedNode { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPage), nameof(ShowChat), nameof(NoPlanner))] public partial PlanPage? Page { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPlans), nameof(Empty))] public partial bool Loaded { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    public bool HasPage => Page is not null;
    public bool HasPlans => Tree.Count > 0;
    public bool Empty => Loaded && Tree.Count == 0 && !IsDrafting;
    public string EmptyText =>
        $"No plans yet. A plan is a markdown file in {Folder}: goals, what it isn't doing, the questions only you can answer, " +
        "the design, and the cards it makes. Each one opens here in its parts.";

    /// <summary>The live agent on a card, if any (the project's agents).</summary>
    public Func<string, AgentViewModel?>? AgentFor { get; set; }
    /// <summary>Open a card's panel on the board; false if no board has it.</summary>
    public Func<string, bool>? OpenCard { get; set; }
    /// <summary>Show a board by name in the Board section.</summary>
    public Action<string>? OpenBoard { get; set; }
    /// <summary>Send a card back with notes (the board's send-back); returns why it couldn't, or null.</summary>
    public Func<string, string, string?>? SendBack { get; set; }
    /// <summary>Open a report in the Reports section.</summary>
    public Action<string>? OpenReport { get; set; }
    /// <summary>The boards changed on disk (cards were made): the Board section reads them again.</summary>
    public Action? BoardsChanged { get; set; }
    /// <summary>The project's reports folder, for the Log (null without agents).</summary>
    public string? ReportsFolder { get; set; }

    public DesignViewModel(ProjectConfig config, string folder)
    {
        _config = config;
        Folder = folder;
    }

    // ---- the planner (WOR-0176): + Plan drafts one from a line; its chat sits beside the page ----

    public const string WholePlan = "Whole plan";
    public IReadOnlyList<string> Kinds { get; } = ["Feature", "Project"];
    [ObservableProperty] public partial string Kind { get; set; } = "Feature";
    /// <summary>The + Plan box: a line saying what to plan.</summary>
    [ObservableProperty] public partial string Idea { get; set; } = "";
    /// <summary>Start a planner on a line (the project's <see cref="ProjectViewModel.StartPlan"/>); null without agents.</summary>
    public Func<string, PlanKind, AgentViewModel>? StartPlanner { get; set; }
    public bool CanPlan => StartPlanner is not null;
    /// <summary>A plan file's planner, if one is listed.</summary>
    public Func<string, AgentViewModel?>? PlannerFor { get; set; }
    /// <summary>Start a planner on a plan that has none, with the first message for it.</summary>
    public Func<string, string, AgentViewModel?>? StartReviser { get; set; }
    /// <summary>Start a planner on a project plan's feature (WOR-0179): the feature's line and the project plan's name for
    /// its <c>parent:</c>, leaving the page where it is; returns the agent and the file Darg named for the plan.</summary>
    public Func<string, string, (AgentViewModel Agent, string File)>? StartFeaturePlanner { get; set; }
    /// <summary>The agent the chat beside the page talks to: the shown plan's planner, or the one drafting a new plan.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowChat), nameof(HasPlanner), nameof(NoPlanner))] public partial AgentViewModel? Planner { get; set; }
    public bool HasPlanner => Planner is not null;
    public bool ShowChat => CanPlan && (Planner is not null || Page is not null);
    /// <summary>A plan shown with no planner: a message from the chat starts one that reads the plan first.</summary>
    public bool NoPlanner => Planner is null && Page is not null;
    /// <summary>The chat's box, and which part the message is about (only that part may change).</summary>
    [ObservableProperty] public partial string Ask { get; set; } = "";
    public ObservableCollection<string> ChangeParts { get; } = [WholePlan];
    [ObservableProperty] public partial string? ChangePart { get; set; } = WholePlan;
    partial void OnChangePartChanged(string? value) { if (value is null) ChangePart = WholePlan; }

    (AgentViewModel Agent, string File)? _draft;
    string? _selectAfter;
    /// <summary>A new plan is being drafted and nothing else picked since: the page says so and the chat is its planner's.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Empty), nameof(DraftingText))] public partial bool IsDrafting { get; private set; }
    public string DraftingText => _draft is { } d
        ? $"The planner is drafting {Path.GetFileName(d.File)} from your line. It opens here when its turn ends; its chat is on the right." : "";

    [RelayCommand]
    void StartPlan()
    {
        var idea = Idea.Trim();
        if (idea.Length == 0) { Status = "Say in a line what to plan: a feature, or a whole project."; return; }
        if (StartPlanner is null) { Status = "Planning needs the project's agents."; return; }
        try { StartPlanner(idea, Kind == "Project" ? PlanKind.Project : PlanKind.Feature); Idea = ""; }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException) { Status = e.Message; }
    }

    /// <summary>A planner has started on a new plan file: show its chat and, when its turn ends, the plan.</summary>
    internal void Drafting(AgentViewModel agent, string file)
    {
        _draft = (agent, file);
        _selecting = true; SelectedNode = null; _selecting = false;
        Page = null;
        Planner = agent;
        IsDrafting = true;
        Status = $"Drafting {Path.GetFileName(file)}: it opens here when the planner's turn ends.";
        AfterTurn(agent, () =>
        {
            if (!File.Exists(file)) { Status = $"The planner's turn ended without writing {Path.GetFileName(file)}: ask it why in the chat."; return; }
            if (IsDrafting && _draft?.File == file) { _selectAfter = file; IsDrafting = false; }
            Status = $"{Path.GetFileName(file)} drafted: a draft to judge. Answer its questions, edit a part, or ask the planner beside it.";
            Reload();
        });
    }

    /// <summary>Send the chat's box to the planner. With a part picked, only that part may change: what it changes
    /// elsewhere is put back when its turn ends (<see cref="Core.Planner.KeepOnly"/>).</summary>
    [RelayCommand]
    void AskPlanner()
    {
        var text = Ask.Trim();
        if (text.Length == 0) return;
        var file = Page?.File ?? (IsDrafting ? _draft?.File : null);
        if (file is null) return;
        if (Planner is { Working: true }) { Status = "The planner is at work: wait for its turn to end."; return; }
        var part = ChangePart is { } p && p != WholePlan ? p : null;
        // what Darg writes to the plan meanwhile (answers, edits, Make cards) is the person's and stands; only the
        // planner's changes outside the part are put back
        PlanHold? hold = null;
        if (part is not null)
            try { hold = PlanHold.Start(file, part); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var sent = Core.Planner.ChangeMessage(file, part, text);
        var agent = Planner;
        if (agent is null)
        {
            agent = StartReviser?.Invoke(file, sent);
            if (agent is null) { hold?.Dispose(); return; }
            Planner = agent;
        }
        else agent.Say(text, sent);
        Ask = "";
        Status = part is null ? "Sent to the planner." : $"Asked the planner to change {part}: anything else it changes is put back.";
        AfterTurn(agent, () =>
        {
            if (hold is not null)
            {
                try
                {
                    var put = hold.Finish();
                    Status = put.Count > 0
                        ? $"The planner changed {part}, and {string.Join(", ", put)} too: put back, so only {part} changed (the put-back isn't committed)."
                        : $"The planner changed {part}.";
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Couldn't check what the planner changed: {e.Message}"; }
            }
            if (Page is null && IsDrafting && File.Exists(file)) { _selectAfter = file; IsDrafting = false; }
            Reload();
        });
    }

    static void AfterTurn(AgentViewModel agent, Action then)
    {
        void Ended(bool _) { agent.TurnEnded -= Ended; Dispatcher.UIThread.Post(then); }
        agent.TurnEnded += Ended;
    }

    partial void OnPageChanged(PlanPage? value)
    {
        List<string> parts = value is null ? [] : ["Goals", value.NotDoing.Name, "Questions", "Design", value.Plan.IsProject ? "Features" : "Cards"];
        if (!ChangeParts.Skip(1).SequenceEqual(parts))
        {
            var keep = ChangePart;
            while (ChangeParts.Count > 1) ChangeParts.RemoveAt(1);
            foreach (var p in parts) ChangeParts.Add(p);
            ChangePart = keep is not null && ChangeParts.Contains(keep) ? keep : WholePlan;
        }
        if (value is not null) Planner = PlannerFor?.Invoke(value.File);
        else if (!IsDrafting) Planner = null;
    }

    /// <summary>Read the plans and the boards again, in the background; calls made while one runs fold into one more.</summary>
    public void Reload()
    {
        if (_pending) return;
        _pending = true;
        var gen = ++_generation;
        var boards = _config.Boards;
        var reports = ReportsFolder;
        _ = Task.Run(() =>
        {
            List<PlanDoc> plans; List<BoardCard> cards; List<AgentReport> log;
            try
            {
                plans = PlanStore.List(Folder);
                cards = CardIndex.Read(boards);
                log = reports is null ? [] : ReportStore.List(reports);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { plans = []; cards = []; log = []; }
            Dispatcher.UIThread.Post(() =>
            {
                _pending = false;
                if (gen == _generation) Apply(plans, cards, log);
            });
        });
    }

    List<PlanDoc> _plans = [];

    void Apply(List<PlanDoc> plans, List<BoardCard> cards, List<AgentReport> log)
    {
        _plans = plans;
        var keep = _selectAfter ?? SelectedNode?.Plan.File;
        _selectAfter = null;
        var expanded = Flatten(Tree).Where(n => !n.IsExpanded).Select(n => n.Plan.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = Blockers.Index(cards);
        PlanNode Node(PlanStore.Node n) =>
            new(n.Plan, Progress(n, index), n.Children.Select(Node)) { IsExpanded = !expanded.Contains(n.Plan.File) };
        var roots = PlanStore.Tree(plans).Select(Node).ToList();
        Tree.Clear();
        foreach (var r in roots) Tree.Add(r);
        OnPropertyChanged(nameof(HasPlans));
        OnPropertyChanged(nameof(Empty));
        Loaded = true;
        if (IsDrafting) return; // the page says a plan is being drafted until it's written or another is picked
        var pick = Flatten(Tree).FirstOrDefault(n => string.Equals(n.Plan.File, keep, StringComparison.OrdinalIgnoreCase)) ?? Tree.FirstOrDefault();
        if (Page is { Editing: true } open && pick is not null && string.Equals(open.File, pick.Plan.File, StringComparison.OrdinalIgnoreCase))
        {
            // a part is being edited: keep its text, refresh only what the board says
            open.RefreshCards(pick.Plan, index);
            _selecting = true; SelectedNode = pick; _selecting = false;
            return;
        }
        _selecting = true; SelectedNode = pick; _selecting = false;
        Page = pick is null ? null : new PlanPage(this, pick.Plan, index, log, plans);
    }

    bool _selecting;
    partial void OnSelectedNodeChanged(PlanNode? value)
    {
        if (_selecting) return;
        if (value is not null) IsDrafting = false;
        // picking another plan reads it fresh, with the board as it is now
        Page = value is null ? null : new PlanPage(this, value.Plan, Blockers.Index(CardIndex.Read(_config.Boards)),
            ReportsFolder is null ? [] : ReportStore.List(ReportsFolder), _plans);
    }

    static IEnumerable<PlanNode> Flatten(IEnumerable<PlanNode> nodes) => nodes.SelectMany(n => Flatten(n.Children).Prepend(n));

    /// <summary>"3/7": made cards done of the plan's cards, or "" with none.</summary>
    internal static (int Done, int Total) Count(PlanDoc plan, IReadOnlyDictionary<string, BoardCard> index) =>
        (plan.Cards.Count(c => c.Made && index.TryGetValue(c.Id, out var b) && b.Done), plan.Cards.Count);

    /// <summary>A plan's cards done of its cards, plus those of every feature plan under it (WOR-0179: a project plan
    /// has no cards of its own, its progress is its features'), or "" with none.</summary>
    static string Progress(PlanStore.Node node, IReadOnlyDictionary<string, BoardCard> index)
    {
        int done = 0, total = 0;
        void Add(PlanStore.Node n)
        {
            var (d, t) = Count(n.Plan, index);
            done += d; total += t;
            foreach (var c in n.Children) Add(c);
        }
        Add(node);
        return total == 0 ? "" : $"{done}/{total}";
    }

    /// <summary>Write an answer into the plan as its <c>**Answer:**</c> line.</summary>
    internal void Answer(PlanQuestionRow q, string answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || Page is not { } page) return;
        try
        {
            if (!PlanStore.Answer(page.File, q.Question, answer, Environment.UserName, DateTime.Now))
            { Status = $"No question \"{q.Question}\" in {Path.GetFileName(page.File)} any more: it changed on disk."; Reload(); return; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Couldn't write the answer: {e.Message}"; return; }
        Status = $"Answered \"{q.Question}\": {answer.Trim()}. Saved to {Path.GetFileName(page.File)}, not committed.";
        // changing what an agent chose: its card goes back with the new answer as the notes (WOR-0178)
        if (q.ByAgent && q.DecidedCard is { } card && !answer.Trim().Equals(q.Chosen, StringComparison.OrdinalIgnoreCase))
        {
            var failed = SendBack?.Invoke(card, $"I changed a choice made on this card. In {Path.GetFileName(page.File)}, the question \"{q.Question}\" " +
                $"was answered \"{q.Chosen}\" by its agent; the answer is now \"{answer.Trim()}\". Change the work to match, and leave the plan's answer as it is.");
            Status += failed is null ? $" {card} sent back with the new answer." : $" {card} not sent back: {failed}";
        }
        Reload();
    }

    /// <summary>Keep what an agent chose: the answer stays, now with the person's name on it instead of the agent's.</summary>
    internal void Keep(PlanQuestionRow q) => Answer(q, q.Chosen);

    /// <summary>Write a part edited as text back into the plan.</summary>
    internal bool Save(PlanTextPart part, string text)
    {
        if (Page is not { } page) return false;
        try { PlanStore.SetSection(page.File, part.Name, text); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = $"Couldn't save {part.Name}: {e.Message}"; return false; }
        Status = $"{part.Name} saved to {Path.GetFileName(page.File)}, not committed.";
        Reload();
        return true;
    }

    readonly Dictionary<string, HashSet<string>> _unticked = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The headings a plan's page has unticked, kept here because a reload builds the page again.</summary>
    internal HashSet<string> UntickedFor(string file) =>
        _unticked.TryGetValue(file, out var set) ? set : _unticked[file] = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Make a plan's ticked cards on its story board (WOR-0177, <see cref="PlanStore.MakeCards"/>), then read the plans
    /// and boards again. Open questions need a second press (<paramref name="confirmOpen"/>). Sets <see cref="Status"/> to
    /// what happened, or why nothing did.</summary>
    public PlanStore.MadeCards Make(string file, IEnumerable<string>? only, bool confirmOpen)
    {
        PlanStore.MadeCards result;
        try
        {
            if (_config.Boards is not { Length: > 0 } boards) throw new InvalidOperationException($"{_config.Name} has no boards folder.");
            result = PlanStore.MakeCards(file, boards, only, confirmOpen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Status = $"Couldn't make the cards: {e.Message}";
            Reload();
            BoardsChanged?.Invoke();   // a part may have been made before it failed
            return new PlanStore.MadeCards("", [], e.Message, false, [], false);
        }
        if (result.NeedsConfirm)
        {
            Status = $"{result.OpenQuestions.Count} {(result.OpenQuestions.Count == 1 ? "question is" : "questions are")} still open. Press again to make the cards anyway.";
            return result;   // nothing changed: no reload, so the page keeps its second press
        }
        if (result.Problem.Length > 0) { Status = result.Problem; return result; }
        {
            Status = $"Made {result.Made.Count} {(result.Made.Count == 1 ? "card" : "cards")} on {result.Board}" + (result.CreatedBoard ? " (a new board)" : "") + ": " +
                     string.Join(", ", result.Made.Select(m => m.Id)) + $". The ids are in {Path.GetFileName(file)}, not committed.";
            BoardsChanged?.Invoke();
        }
        Reload();
        return result;
    }

    /// <summary>A planner is still drafting the plan file named <paramref name="slug"/>: it isn't written yet and its
    /// planner is at work, or waits on the person.</summary>
    internal bool IsDraftingPlan(string slug)
    {
        var path = Path.Combine(Folder, slug + ".md");
        return !File.Exists(path) && PlannerFor?.Invoke(path) is { IsLiveOnCard: true };
    }

    /// <summary>What <see cref="MakeFeatures"/> started, or why it started nothing.</summary>
    public sealed record MadeFeatures(IReadOnlyList<(string Title, string File, string Agent)> Started, string Problem, bool NeedsConfirm, IReadOnlyList<string> OpenQuestions);

    /// <summary>Draft a plan for each of a project plan's features still without one (WOR-0179, docs/FEATURE-PLANNING.md
    /// "Whole projects"): those in <paramref name="only"/> by title, else all. A planner starts on each with the feature's
    /// line and <c>parent:</c> set to the project plan, and the feature's title in the project plan is linked to the file
    /// Darg named for it at once, so the row reads as drafting and the plan sits under its project however the planner
    /// titles it. Open questions in the project plan need a second press (<paramref name="confirmOpen"/>), as Make cards
    /// does; the project plan is set to agreed. The page stays on the project; each plan shows in the tree as its planner's
    /// turn ends.</summary>
    public MadeFeatures MakeFeatures(string file, IEnumerable<string>? only, bool confirmOpen)
    {
        MadeFeatures Stop(string problem, IReadOnlyList<string>? open = null) { Status = problem; return new([], problem, false, open ?? []); }
        if (StartFeaturePlanner is null) return Stop("Planning needs the project's agents.");
        PlanDoc plan; List<PlanDoc> plans;
        try { plan = PlanDoc.Load(file); plans = PlanStore.List(Folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Stop($"Couldn't read {Path.GetFileName(file)}: {e.Message}"); }
        if (!plan.IsProject) return Stop($"{Path.GetFileName(file)} is a feature plan: only a project plan has features to plan.");
        if (plan.Status == "dropped") return Stop("The plan is dropped.");
        var open = plan.OpenQuestions.Select(q => q.Question).ToList();
        var todo = PlanStore.FeaturesToPlan(plan, plans, IsDraftingPlan);
        var pick = only?.Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
        if (pick is not null)
            foreach (var o in pick.Where(o => !todo.Any(f => f.Title.Equals(o, StringComparison.OrdinalIgnoreCase))))
                return Stop($"No feature \"{o}\" in the plan without a plan of its own.", open);
        var rows = todo.Where(f => pick is null || pick.Any(o => o.Equals(f.Title, StringComparison.OrdinalIgnoreCase))).ToList();
        if (rows.Count == 0) return Stop("Every feature in the plan has a plan of its own already.", open);
        if (open.Count > 0 && !confirmOpen)
        {
            Status = $"{open.Count} {(open.Count == 1 ? "question is" : "questions are")} still open. Press again to draft the feature plans anyway.";
            return new([], "", true, open);
        }
        var started = new List<(string, string, string)>();
        var problem = "";
        foreach (var f in rows)
        {
            try
            {
                var (agent, planFile) = StartFeaturePlanner(f.Idea, plan.Slug);
                PlanStore.SetFeatureLink(file, f.Title, Path.GetFileName(planFile));
                started.Add((f.Title, planFile, agent.Id));
                AfterTurn(agent, Reload);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            { problem = $"Couldn't start a planner on \"{f.Title}\": {e.Message}"; break; }
        }
        try { if (started.Count > 0 && plan.Status != "agreed") PlanStore.SetProperty(file, "status", "agreed"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (problem.Length == 0) problem = $"Couldn't set the plan agreed: {e.Message}"; }
        Status = (started.Count == 0 ? "" : $"Drafting {started.Count} feature {(started.Count == 1 ? "plan" : "plans")}: " +
                 string.Join(", ", started.Select(s => Path.GetFileName(s.Item2))) + $". Each shows under {(plan.Title.Length > 0 ? plan.Title : plan.Slug)} when its planner's turn ends; " +
                 $"the links are in {Path.GetFileName(file)}, not committed. ") + problem;
        Reload();
        return new(started, problem, false, open);
    }

    internal void ShowCard(string id)
    {
        if (OpenCard?.Invoke(id) != true) Status = $"No board of {_config.Name} has {id}.";
    }

    internal void ShowPlan(string link)
    {
        var name = Path.GetFileNameWithoutExtension(link.Split('#')[0]);
        var node = Flatten(Tree).FirstOrDefault(n => n.Plan.Slug.Equals(name, StringComparison.OrdinalIgnoreCase) || n.Plan.Title.Equals(link, StringComparison.OrdinalIgnoreCase));
        if (node is null) { Status = $"No plan {link} in {Folder}."; return; }
        SelectedNode = node;
    }

    internal void ShowBoard(string board) => OpenBoard?.Invoke(board);
    internal void ShowReport(string file) => OpenReport?.Invoke(file);

    [RelayCommand]
    void Refresh() => Reload();

    [RelayCommand]
    void OpenFile()
    {
        var target = Page?.File ?? Folder;
        if (Page is null && !Directory.Exists(Folder)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Status = $"Couldn't open {target}: {e.Message}"; }
    }
}

/// <summary>A plan in the tree: its title, what kind and state it is, its cards' progress, its feature plans under it.</summary>
public sealed partial class PlanNode : ObservableObject
{
    public PlanDoc Plan { get; }
    public string Title => Plan.Title.Length > 0 ? Plan.Title : Plan.Slug;
    /// <summary>Draft and dropped are marked; agreed is the normal state and says nothing.</summary>
    public string Mark => Plan.Status is "agreed" ? "" : Plan.Status;
    public bool HasMark => Mark.Length > 0;
    public bool Dropped => Plan.Status == "dropped";
    public string Progress { get; }
    public string Tip => $"{(Plan.IsProject ? "Project" : "Feature")} plan · {Plan.Status}" + (Progress.Length > 0 ? $" · {Progress} cards done" + (Plan.IsProject ? ", with its features'" : "") : "") + $"\n{Plan.File}";
    public ObservableCollection<PlanNode> Children { get; }
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;

    public PlanNode(PlanDoc plan, string progress, IEnumerable<PlanNode> children)
    {
        Plan = plan;
        Progress = progress;
        Children = new(children);
    }
}

/// <summary>The picked plan as a page: its header, then Goals, Not doing, Questions, Design, Cards (or Features) and Log.</summary>
public sealed partial class PlanPage : ObservableObject
{
    readonly DesignViewModel _owner;
    public PlanDoc Plan { get; private set; }
    public string File => Plan.File;
    public string Title => Plan.Title.Length > 0 ? Plan.Title : Plan.Slug;
    public string Intro => Plan.Intro;
    public bool HasIntro => Intro.Length > 0;
    [ObservableProperty] public partial string Summary { get; private set; } = "";
    public bool HasBoard => Plan.Board.Length > 0;
    public string BoardText => $"▦ {Plan.Board}";
    public PlanTextPart Goals { get; }
    public PlanTextPart NotDoing { get; }
    public PlanTextPart Design { get; }
    public IReadOnlyList<PlanQuestionRow> Questions { get; }
    public bool HasQuestions => Questions.Count > 0;
    public string QuestionsHeading => Plan.OpenQuestions.Count is var n and > 0 ? $"Questions · {n} open" : "Questions";
    public ObservableCollection<PlanCardRow> Cards { get; } = [];
    public bool ShowCards => !Plan.IsProject || Plan.Cards.Count > 0;
    public bool HasCards => Cards.Count > 0;
    public IReadOnlyList<PlanFeatureRow> Features { get; }
    public bool ShowFeatures => Plan.IsProject;
    public bool HasFeatures => Features.Count > 0;

    // ---- Make features (WOR-0179): the ticked features without a plan each get a planner, parent set to this plan ----

    /// <summary>Titles of the features the person unticked (all are ticked to start), kept across the page's reloads.</summary>
    internal HashSet<string> UntickedFeatures => _owner.UntickedFor(File + "#features");
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MakeFeaturesText), nameof(FeaturesOpenText))] public partial bool ConfirmingFeatures { get; set; }
    public int FeatureMakeCount => Features.Count(f => f.Proposed && f.Ticked);
    public bool CanMakeFeatures => FeatureMakeCount > 0;
    public bool HasProposedFeatures => Features.Any(f => f.Proposed);
    public string MakeFeaturesText => ConfirmingFeatures ? $"Draft {FeatureMakeCount} anyway"
        : $"Draft {FeatureMakeCount} feature {(FeatureMakeCount == 1 ? "plan" : "plans")}";
    public bool HasFeaturesOpenQuestions => Plan.OpenQuestions.Count > 0 && HasProposedFeatures;
    public string FeaturesOpenText => "Still open: " + string.Join("; ", OpenQuestionList) + (ConfirmingFeatures ? ". Press again to draft the feature plans anyway." : ".");

    internal void FeatureTickChanged(PlanFeatureRow row)
    {
        if (row.Ticked) UntickedFeatures.Remove(row.Title); else UntickedFeatures.Add(row.Title);
        ConfirmingFeatures = false;
        NotifyFeatures();
    }

    void NotifyFeatures()
    {
        OnPropertyChanged(nameof(FeatureMakeCount)); OnPropertyChanged(nameof(CanMakeFeatures)); OnPropertyChanged(nameof(MakeFeaturesText));
        OnPropertyChanged(nameof(HasProposedFeatures)); OnPropertyChanged(nameof(HasFeaturesOpenQuestions)); OnPropertyChanged(nameof(FeaturesOpenText));
    }

    /// <summary>One press starts a planner on each ticked feature; with questions still open, the first press only says so.</summary>
    [RelayCommand]
    void MakeFeatures()
    {
        var only = Features.Where(f => f.Proposed && f.Ticked).Select(f => f.Title).ToList();
        if (only.Count == 0) return;
        var result = _owner.MakeFeatures(File, only, ConfirmingFeatures);
        ConfirmingFeatures = result.NeedsConfirm;
        NotifyFeatures();
    }
    public IReadOnlyList<PlanLogRow> Log { get; }
    public bool HasLog => Log.Count > 0;
    public bool Editing => Goals.Editing || NotDoing.Editing || Design.Editing;

    /// <summary>Headings of the proposed cards the person unticked (all are ticked to start), kept across the page's reloads.</summary>
    internal HashSet<string> Unticked => _owner.UntickedFor(File);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MakeText))] public partial bool Confirming { get; set; }
    public int MakeCount => Cards.Count(c => c.Proposed && c.Ticked);
    public bool CanMake => MakeCount > 0;
    public bool HasProposed => Cards.Any(c => c.Proposed);
    public string MakeText => Confirming ? $"Make {MakeCount} anyway" : $"Make {MakeCount} {(MakeCount == 1 ? "card" : "cards")}";
    public IReadOnlyList<string> OpenQuestionList => Plan.OpenQuestions.Select(q => q.Question).ToList();
    public bool HasOpenQuestions => Plan.OpenQuestions.Count > 0 && HasProposed;
    public string OpenQuestionsText => "Still open: " + string.Join("; ", OpenQuestionList) + (Confirming ? ". Press again to make the cards anyway." : ".");

    internal void TickChanged(PlanCardRow row)
    {
        if (row.Ticked) Unticked.Remove(row.Title); else Unticked.Add(row.Title);
        Confirming = false;   // a different set of cards: the question is asked again
        NotifyMake();
    }

    void NotifyMake()
    {
        OnPropertyChanged(nameof(MakeCount)); OnPropertyChanged(nameof(CanMake)); OnPropertyChanged(nameof(MakeText));
        OnPropertyChanged(nameof(HasProposed)); OnPropertyChanged(nameof(HasOpenQuestions)); OnPropertyChanged(nameof(OpenQuestionsText));
    }

    /// <summary>One press makes the ticked cards. With questions still open the first press only says so and the second makes them.</summary>
    [RelayCommand]
    void MakeCards()
    {
        var only = Cards.Where(c => c.Proposed && c.Ticked).Select(c => c.Title).ToList();
        if (only.Count == 0) return;
        var result = _owner.Make(File, only, Confirming);
        Confirming = result.NeedsConfirm;
        NotifyMake();
    }

    readonly IReadOnlyList<PlanDoc> _plans;

    public PlanPage(DesignViewModel owner, PlanDoc plan, IReadOnlyDictionary<string, BoardCard> index, IReadOnlyList<AgentReport> reports,
        IReadOnlyList<PlanDoc>? plans = null)
    {
        _owner = owner;
        Plan = plan;
        _plans = plans ?? [];
        string raw;
        try { raw = System.IO.File.ReadAllText(plan.File); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { raw = ""; }
        Goals = new PlanTextPart(owner, "Goals", PlanText.Section(raw, "Goals"), "What is different when it's done, a few lines.");
        // the heading the file uses: the parser takes Non-goals and Out of scope as Not doing too
        var notDoing = new[] { "Not doing", "Non-goals", "Out of scope" }.FirstOrDefault(h => PlanText.Has(raw, h)) ?? "Not doing";
        NotDoing = new PlanTextPart(owner, notDoing, PlanText.Section(raw, notDoing), "What is out, so a card agent doesn't drift into it.") { Label = "Not doing" };
        Design = new PlanTextPart(owner, "Design", plan.Design, "The approach, the parts, what it builds on, risks.");
        Questions = plan.Questions.Select(q => new PlanQuestionRow(owner, q)).ToList();
        Features = plan.Features.Select(f => new PlanFeatureRow(owner, this, plan, f, _plans, index)).ToList();
        var ids = plan.Cards.Where(c => c.Made).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // what has happened since: the made cards' reports, newest first (built here, never written into the plan)
        Log = reports.Where(r => ids.Contains(r.Card)).OrderByDescending(r => r.Date, StringComparer.Ordinal)
            .Select(r => new PlanLogRow(owner, r)).ToList();
        RefreshCards(plan, index);
    }

    /// <summary>Read the cards' rows again from the board, the rest of the page left as it is.</summary>
    public void RefreshCards(PlanDoc plan, IReadOnlyDictionary<string, BoardCard> index)
    {
        Plan = plan;
        // rows that read the same keep their controls (ListSync compares by reference)
        var want = plan.Cards.Select(c => new PlanCardRow(_owner, plan, c, index, this))
            .Select(row => Cards.FirstOrDefault(old => old.Key == row.Key && old.Same(row)) ?? row).ToList();
        ListSync.Sync(Cards, want);
        OnPropertyChanged(nameof(HasCards));
        NotifyMake();
        var (done, total) = DesignViewModel.Count(plan, index);
        var made = plan.Cards.Count(c => c.Made);
        // a project's progress is its features' plans' cards (WOR-0179)
        var featurePlans = plan.IsProject ? plan.Features.Select(f => PlanStore.FeaturePlan(plan, f, _plans)).OfType<PlanDoc>().Distinct().ToList() : [];
        foreach (var fp in featurePlans)
        {
            var (d, t) = DesignViewModel.Count(fp, index);
            done += d; total += t; made += fp.Cards.Count(c => c.Made);
        }
        var state = plan.Status == "agreed" && made > 0 ? (done == total && total > 0 ? "built" : "building") : plan.Status;
        Summary = string.Join(" · ", new[]
        {
            $"{(plan.IsProject ? "Project" : "Feature")} plan, {state}",
            plan.IsProject && plan.Features.Count > 0 ? $"{featurePlans.Count} of {plan.Features.Count} features planned" : "",
            total == 0 ? "" : $"{done} of {total} cards done" + (made < total ? $", {total - made} not made yet" : ""),
            plan.OpenQuestions.Count is var q and > 0 ? $"{q} {(q == 1 ? "question" : "questions")} open" : "",
        }.Where(s => s.Length > 0));
    }

    [RelayCommand]
    void OpenBoard() { if (HasBoard) _owner.ShowBoard(Plan.Board); }
}

/// <summary>A part edited as text in place: shown as markdown, ✎ Edit turns it into a box with Save and Cancel.</summary>
public sealed partial class PlanTextPart(DesignViewModel owner, string name, string markdown, string hint) : ObservableObject
{
    /// <summary>The part's heading in the file, which Save writes under.</summary>
    public string Name { get; } = name;
    /// <summary>What the page calls it.</summary>
    public string Label { get; init; } = name;
    public string Hint { get; } = hint;
    public string Markdown { get; } = markdown.Trim();
    public bool IsEmpty => Markdown.Length == 0;
    public bool HasText => !IsEmpty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Reading))] public partial bool Editing { get; set; }
    public bool Reading => !Editing;
    [ObservableProperty] public partial string Draft { get; set; } = "";

    [RelayCommand]
    void Edit() { Draft = Markdown; Editing = true; }

    [RelayCommand]
    void Save()
    {
        Editing = false; // before the reload Save starts, so the page is rebuilt with the new text
        if (Draft.Trim() == Markdown) return;
        if (!owner.Save(this, Draft)) Editing = true;
    }

    [RelayCommand]
    void Cancel() => Editing = false;
}

/// <summary>A question only the person can answer: its options as buttons, recommended first; a tap writes the answer.</summary>
public sealed partial class PlanQuestionRow : ObservableObject
{
    readonly DesignViewModel _owner;
    public string Question { get; }
    public string Context { get; }
    public bool HasContext => Context.Length > 0;
    public string Answer { get; }
    public bool Answered => Answer.Length > 0;
    /// <summary>An agent's choice, not the person's: shown with Keep and Change (WOR-0178).</summary>
    public bool ByAgent { get; }
    public string? DecidedCard { get; }
    public string Chosen { get; }
    public string DecidedText => $"Decided by {(DecidedCard is null ? "an agent" : DecidedCard + "'s agent")}: {Chosen}. Keep it, or change it and its card goes back with your answer.";
    public IReadOnlyList<PlanOptionRow> Options { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Asking))] public partial bool Changing { get; set; }
    /// <summary>The options show while it's open, or while an answer is being changed.</summary>
    public bool Asking => !Answered || Changing;
    [ObservableProperty] public partial string OwnAnswer { get; set; } = "";

    public PlanQuestionRow(DesignViewModel owner, PlanQuestion q)
    {
        _owner = owner;
        Question = q.Question;
        Context = q.Context;
        Answer = q.Answer;
        ByAgent = q.ByAgent;
        DecidedCard = q.DecidedCard;
        Chosen = q.Pick;
        Options = q.Options.OrderByDescending(o => o.Recommended).Select(o => new PlanOptionRow(this, o)).ToList();
    }

    internal void Pick(string label) => _owner.Answer(this, label);

    [RelayCommand]
    void SendOwn() { if (!string.IsNullOrWhiteSpace(OwnAnswer)) _owner.Answer(this, OwnAnswer); }

    [RelayCommand]
    void Change() => Changing = !Changing;

    [RelayCommand]
    void Keep() => _owner.Keep(this);
}

public sealed partial class PlanOptionRow(PlanQuestionRow question, DecisionOption option)
{
    public string Label => option.Recommended ? option.Label + " (recommended)" : option.Label;
    public string Description => option.Description;
    public bool HasDescription => Description.Length > 0;
    public bool Recommended => option.Recommended;

    [RelayCommand]
    void Pick() => question.Pick(option.Label);
}

/// <summary>A card in the plan. Proposed: its ticket score and what it waits on. Made: its id, then its column, live agent
/// and open blockers as the board has them now; clicking it opens the card's panel.</summary>
public sealed partial class PlanCardRow : ObservableObject
{
    readonly DesignViewModel _owner;
    readonly PlanPage? _page;
    public string Key { get; }
    public string Id { get; }
    /// <summary>Ticked to be made (proposed rows only; on by default).</summary>
    [ObservableProperty] public partial bool Ticked { get; set; } = true;
    partial void OnTickedChanged(bool value) => _page?.TickChanged(this);
    public string Title { get; }
    public bool Made { get; }
    public bool Proposed => !Made;
    public string Heading => Made ? $"{Id}  {Title}" : Title;
    public string Description { get; }
    public string Where { get; }
    public string Done { get; }
    public bool HasWhere => Where.Length > 0;
    public bool HasDone => Done.Length > 0;
    /// <summary>The board's column ("In Progress"), with the board when it isn't the plan's; "not on any board" if it's gone.</summary>
    public string Column { get; }
    public bool OnBoard { get; }
    public bool IsDone { get; }
    public string Agent { get; }
    public bool HasAgent => Agent.Length > 0;
    /// <summary>What it still waits on: a made card's open blockers from the board, a proposed one's Blocked by line.</summary>
    public string Blockers { get; }
    public bool HasBlockers => Blockers.Length > 0;
    public bool BlockerWarning { get; }
    public TicketScore? Quality { get; }
    public bool HasQuality => Quality is not null;
    public string Tip => Made ? $"Open {Id} on the board" : Quality?.Tip ?? "";

    public PlanCardRow(DesignViewModel owner, PlanDoc plan, PlanCard card, IReadOnlyDictionary<string, BoardCard> index, PlanPage? page = null)
    {
        _owner = owner;
        Ticked = page is null || !page.Unticked.Contains(card.Title);
        _page = page;
        Key = card.Made ? card.Id : "#" + card.Title;
        Id = card.Id;
        Title = card.Title;
        Made = card.Made;
        Description = card.Description;
        // code spans read as plain text on a row
        Where = card.Where.Replace("`", "");
        Done = card.Done.Replace("`", "");
        if (Made && index.TryGetValue(card.Id, out var b))
        {
            OnBoard = true;
            IsDone = b.Done;
            Column = b.Column + (plan.Board.Length > 0 && !b.Board.Equals(plan.Board, StringComparison.OrdinalIgnoreCase) ? $" · {b.Board}" : "");
            var open = Core.Blockers.Of(b.Card, index).Where(s => s.Shows).ToList();
            Blockers = string.Join(", ", open.Select(s => s.Chip));
            BlockerWarning = open.Any(s => s.IsWarning);
            Agent = owner.AgentFor?.Invoke(card.Id) is { } a ? $"✦ {a.StateText}" : "";
        }
        else if (Made)
        {
            Column = "not on any board";
            Blockers = Agent = "";
        }
        else
        {
            Column = "";
            Agent = "";
            Quality = card.Score;
            // a proposed card's blockers: those made and not yet cleared, and those not made yet
            var waits = card.BlockedBy.Select(r =>
            {
                var target = plan.Find(r);
                var id = target?.Made == true ? target.Id : target is null && Core.Blockers.ParseIds(r).Count == 1 ? r : null;
                if (id is not null && index.TryGetValue(id, out var bc)) return Core.Blockers.IsCleared(bc) ? null : $"waits on {id}";
                return $"waits on {r}";
            }).OfType<string>().ToList();
            Blockers = string.Join(", ", waits);
        }
    }

    internal bool Same(PlanCardRow o) =>
        Title == o.Title && Column == o.Column && Agent == o.Agent && Blockers == o.Blockers && Description == o.Description
        && Where == o.Where && Done == o.Done && Quality?.Stars == o.Quality?.Stars;

    [RelayCommand]
    void Open() { if (Made) _owner.ShowCard(Id); }
}

/// <summary>A feature of a project plan (WOR-0179). With a plan of its own: that plan's state and its cards' progress, and a
/// click opens it. A planner drafting it: "drafting". Neither: a tick, for Make features to start a planner on it.</summary>
public sealed partial class PlanFeatureRow : ObservableObject
{
    readonly DesignViewModel _owner;
    readonly PlanPage? _page;
    public string Title { get; }
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    /// <summary>Its own plan, found by its link or by title under this project.</summary>
    public PlanDoc? Plan { get; }
    public bool HasPlan => Plan is not null;
    public bool Drafting { get; }
    public bool Proposed => !HasPlan && !Drafting;
    /// <summary>"agreed · 2/5 cards done", "drafting", or "proposed".</summary>
    public string State { get; }
    [ObservableProperty] public partial bool Ticked { get; set; } = true;
    partial void OnTickedChanged(bool value) => _page?.FeatureTickChanged(this);
    public string Tip => HasPlan ? $"Open the plan for {Title}" : Drafting ? "A planner is drafting its plan" : "Tick it and press Draft to have a planner draft its plan";

    public PlanFeatureRow(DesignViewModel owner, PlanPage? page, PlanDoc project, PlanFeature feature, IReadOnlyList<PlanDoc> plans,
        IReadOnlyDictionary<string, BoardCard> index)
    {
        _owner = owner;
        Title = feature.Title;
        Description = feature.Description;
        Ticked = page is null || !page.UntickedFeatures.Contains(feature.Title);
        _page = page;
        Plan = PlanStore.FeaturePlan(project, feature, plans);
        Drafting = Plan is null && feature.LinkSlug.Length > 0 && owner.IsDraftingPlan(feature.LinkSlug);
        if (Plan is { } p)
        {
            var (done, total) = DesignViewModel.Count(p, index);
            State = p.Status + (total > 0 ? $" · {done}/{total} cards done" : " · no cards yet");
        }
        else State = Drafting ? "drafting" : "proposed";
    }

    [RelayCommand]
    void Open() { if (Plan is { } p) _owner.ShowPlan(p.Slug); }
}

public sealed partial class PlanLogRow(DesignViewModel owner, AgentReport report)
{
    public string Text => $"{report.Card}  {report.Title}";
    public string Meta => $"{report.Date} · {(report.Qa ? "QA report" : report.Cnr ? "could not reproduce" : report.Investigation ? "finding" : "done report")}";

    [RelayCommand]
    void Open() => owner.ShowReport(report.File);
}

/// <summary>A part's text as written, for the editor: everything under its <c>##</c> heading up to the next.</summary>
public static class PlanText
{
    public static bool Has(string text, string part) =>
        text.Replace("\r\n", "\n").Split('\n').Any(l => l.StartsWith("## ") && Heading(l).Equals(part, StringComparison.OrdinalIgnoreCase));

    static string Heading(string line) => line[3..].Trim().TrimEnd('#').Trim();

    public static string Section(string text, string part)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ")) continue;
            if (start >= 0) return string.Join("\n", lines[start..i]).Trim();
            if (Heading(lines[i]).Equals(part, StringComparison.OrdinalIgnoreCase)) start = i + 1;
        }
        return start >= 0 ? string.Join("\n", lines[start..]).Trim() : "";
    }
}
