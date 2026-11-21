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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPage))] public partial PlanPage? Page { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPlans), nameof(Empty))] public partial bool Loaded { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    public bool HasPage => Page is not null;
    public bool HasPlans => Tree.Count > 0;
    public bool Empty => Loaded && Tree.Count == 0;
    public string EmptyText =>
        $"No plans yet. A plan is a markdown file in {Folder}: goals, what it isn't doing, the questions only you can answer, " +
        "the design, and the cards it makes. Each one opens here in its parts.";

    /// <summary>The live agent on a card, if any (the project's agents).</summary>
    public Func<string, AgentViewModel?>? AgentFor { get; set; }
    /// <summary>Open a card's panel on the board; false if no board has it.</summary>
    public Func<string, bool>? OpenCard { get; set; }
    /// <summary>Show a board by name in the Board section.</summary>
    public Action<string>? OpenBoard { get; set; }
    /// <summary>Open a report in the Reports section.</summary>
    public Action<string>? OpenReport { get; set; }
    /// <summary>The project's reports folder, for the Log (null without agents).</summary>
    public string? ReportsFolder { get; set; }

    public DesignViewModel(ProjectConfig config, string folder)
    {
        _config = config;
        Folder = folder;
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

    void Apply(List<PlanDoc> plans, List<BoardCard> cards, List<AgentReport> log)
    {
        var keep = SelectedNode?.Plan.File;
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
        var pick = Flatten(Tree).FirstOrDefault(n => string.Equals(n.Plan.File, keep, StringComparison.OrdinalIgnoreCase)) ?? Tree.FirstOrDefault();
        if (Page is { Editing: true } open && pick is not null && string.Equals(open.File, pick.Plan.File, StringComparison.OrdinalIgnoreCase))
        {
            // a part is being edited: keep its text, refresh only what the board says
            open.RefreshCards(pick.Plan, index);
            _selecting = true; SelectedNode = pick; _selecting = false;
            return;
        }
        _selecting = true; SelectedNode = pick; _selecting = false;
        Page = pick is null ? null : new PlanPage(this, pick.Plan, index, log);
    }

    bool _selecting;
    partial void OnSelectedNodeChanged(PlanNode? value)
    {
        if (_selecting) return;
        // picking another plan reads it fresh, with the board as it is now
        Page = value is null ? null : new PlanPage(this, value.Plan, Blockers.Index(CardIndex.Read(_config.Boards)),
            ReportsFolder is null ? [] : ReportStore.List(ReportsFolder));
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
        Reload();
    }

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
    public IReadOnlyList<PlanLogRow> Log { get; }
    public bool HasLog => Log.Count > 0;
    public bool Editing => Goals.Editing || NotDoing.Editing || Design.Editing;

    public PlanPage(DesignViewModel owner, PlanDoc plan, IReadOnlyDictionary<string, BoardCard> index, IReadOnlyList<AgentReport> reports)
    {
        _owner = owner;
        Plan = plan;
        string raw;
        try { raw = System.IO.File.ReadAllText(plan.File); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { raw = ""; }
        Goals = new PlanTextPart(owner, "Goals", PlanText.Section(raw, "Goals"), "What is different when it's done, a few lines.");
        // the heading the file uses: the parser takes Non-goals and Out of scope as Not doing too
        var notDoing = new[] { "Not doing", "Non-goals", "Out of scope" }.FirstOrDefault(h => PlanText.Has(raw, h)) ?? "Not doing";
        NotDoing = new PlanTextPart(owner, notDoing, PlanText.Section(raw, notDoing), "What is out, so a card agent doesn't drift into it.") { Label = "Not doing" };
        Design = new PlanTextPart(owner, "Design", plan.Design, "The approach, the parts, what it builds on, risks.");
        Questions = plan.Questions.Select(q => new PlanQuestionRow(owner, q)).ToList();
        Features = plan.Features.Select(f => new PlanFeatureRow(owner, f)).ToList();
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
        var want = plan.Cards.Select(c => new PlanCardRow(_owner, plan, c, index))
            .Select(row => Cards.FirstOrDefault(old => old.Key == row.Key && old.Same(row)) ?? row).ToList();
        ListSync.Sync(Cards, want);
        OnPropertyChanged(nameof(HasCards));
        var (done, total) = DesignViewModel.Count(plan, index);
        var made = plan.Cards.Count(c => c.Made);
        var state = plan.Status == "agreed" && made > 0 ? (done == total && total > 0 ? "built" : "building") : plan.Status;
        Summary = string.Join(" · ", new[]
        {
            $"{(plan.IsProject ? "Project" : "Feature")} plan, {state}",
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
        Options = q.Options.OrderByDescending(o => o.Recommended).Select(o => new PlanOptionRow(this, o)).ToList();
    }

    internal void Pick(string label) => _owner.Answer(this, label);

    [RelayCommand]
    void SendOwn() { if (!string.IsNullOrWhiteSpace(OwnAnswer)) _owner.Answer(this, OwnAnswer); }

    [RelayCommand]
    void Change() => Changing = !Changing;
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
    public string Key { get; }
    public string Id { get; }
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

    public PlanCardRow(DesignViewModel owner, PlanDoc plan, PlanCard card, IReadOnlyDictionary<string, BoardCard> index)
    {
        _owner = owner;
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

public sealed partial class PlanFeatureRow(DesignViewModel owner, PlanFeature feature)
{
    public string Title => feature.Title;
    public string Description => feature.Description;
    public bool HasDescription => Description.Length > 0;
    public bool HasLink => feature.Link.Length > 0;

    [RelayCommand]
    void Open() { if (HasLink) owner.ShowPlan(feature.Link); }
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
