using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Mobile.ViewModels;

/// <summary>The API calls the Needs you screen makes (AND-0010), over <see cref="DargClient.Call"/>.</summary>
public static class NeedsYouCalls
{
    /// <summary>Everything waiting on Philip, every project's: asks first, then decisions.</summary>
    public static async Task<IReadOnlyList<NeedsYouItem>> NeedsYou(this DargClient client, CancellationToken ct = default) =>
        NeedsYouItem.List(await client.Call("needs-you", null, ct));

    /// <summary>Answers an agent's question: question text to the label picked.</summary>
    public static Task<JsonNode?> AnswerQuestion(this DargClient client, string agent, int at, IReadOnlyDictionary<string, string> answers, CancellationToken ct = default)
    {
        var o = new JsonObject();
        foreach (var (q, a) in answers) o[q] = a;
        return client.Call("answer", new JsonObject { ["id"] = agent, ["at"] = at, ["answers"] = o }, ct);
    }

    /// <summary>Allows or denies the tool an agent asks to use.</summary>
    public static Task<JsonNode?> AnswerPermission(this DargClient client, string agent, int at, bool allow, CancellationToken ct = default) =>
        client.Call("answer", new { id = agent, at, allow }, ct);

    /// <summary>Answers a card's decision with an option's label (its action runs on the PC), with notes if any.</summary>
    public static Task<JsonNode?> Decide(this DargClient client, string project, string card, string answer, string notes = "", CancellationToken ct = default) =>
        client.Call("decide", new { project, card, answer, notes }, ct);
}

/// <summary>The phone's first screen (AND-0010, docs/design/android.md): every agent question and permission ask and every
/// card in For Human Attention with its decision, across projects, each answered with a tap. It reads the desktop's
/// <c>needs-you</c> when shown and every few seconds while it stays on screen (the view runs <see cref="Poll"/>);
/// a row answered goes from the list at once, and the next read confirms it.</summary>
public sealed partial class NeedsYouViewModel(ShellViewModel shell, IReadOnlyList<ProjectInfo>? projects = null) : PageViewModel
{
    public override string Title => "Needs you";

    public ObservableCollection<NeedViewModel> Items { get; } = [];

    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;
    [ObservableProperty] bool _busy;
    [ObservableProperty] bool _empty;

    /// <summary>Keys answered from here whose rows stay hidden until a read no longer lists them (the PC may take a moment).</summary>
    readonly HashSet<string> _answered = [];

    internal DargClient? Client => shell.Client;

    public string Where => shell.Client?.Connection.Address ?? "";

    /// <summary>Puts a fresh read in place: rows still waiting keep their state (a pick half made, notes typed), new ones join, gone ones go.</summary>
    internal void Fill(IReadOnlyList<NeedsYouItem> items)
    {
        _answered.IntersectWith(items.Select(x => x.Key));
        var want = items.Where(x => !_answered.Contains(x.Key)).ToList();
        for (var i = Items.Count - 1; i >= 0; i--)
            if (!want.Any(x => x.Key == Items[i].Key)) Items.RemoveAt(i);
        for (var i = 0; i < want.Count; i++)
        {
            var at = IndexOf(want[i].Key);
            if (at < 0) Items.Insert(i, new NeedViewModel(this, want[i]));
            else if (at != i) Items.Move(at, i);
        }
        Empty = Items.Count == 0;
    }

    int IndexOf(string key)
    {
        for (var i = 0; i < Items.Count; i++) if (Items[i].Key == key) return i;
        return -1;
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (shell.Client is not { } client || Busy) return;
        Busy = true;
        try
        {
            Fill(await client.NeedsYou());
            Bad = false; Status = "";
        }
        catch (DargClientException e) { Bad = true; Status = e.Message; }
        finally { Busy = false; }
    }

    /// <summary>The view's re-read while on screen (a failure keeps the rows and says why above them, as Refresh does).</summary>
    public Task Poll() => Refresh();

    [RelayCommand]
    void OpenProjects()
    {
        var page = new ProjectsViewModel(shell);
        shell.Show(page);
        // the list fetched on connecting shows at once; after that each visit reads it fresh
        if (projects is not null) { page.Fill(projects); projects = null; }
        else _ = page.Refresh();
    }

    /// <summary>A row was answered on the PC's side: it leaves the list now, not at the next read.</summary>
    internal void Answered(NeedViewModel row)
    {
        _answered.Add(row.Key);
        Items.Remove(row);
        Empty = Items.Count == 0;
    }
}

/// <summary>One thing waiting: an agent's question (options to tap, one per question; several questions or a multi-select
/// one send with Send), a permission ask (Allow or Deny), or a card's decision (its options, recommended first, with
/// optional notes that go with the pick).</summary>
public sealed partial class NeedViewModel : ObservableObject
{
    readonly NeedsYouViewModel _owner;

    public NeedsYouItem Item { get; }
    public string Key => Item.Key;
    public IReadOnlyList<NeedQuestionViewModel> Questions { get; }

    public bool IsQuestion => Item.Kind == NeedKind.Question;
    public bool IsPermission => Item.Kind == NeedKind.Permission;
    public bool IsDecision => Item.Kind == NeedKind.Decision;

    /// <summary>The line above it: what kind of thing, which project, which card or agent.</summary>
    public string Kicker => Item.Kind switch
    {
        NeedKind.Decision => $"Decision · {Item.ProjectName} · {Item.Card}",
        NeedKind.Permission => $"Permission · {Item.ProjectName} · {Item.AgentName}",
        _ => $"Question · {Item.ProjectName} · {Item.AgentName}",
    };

    /// <summary>What it's about: a decision's card title, an ask's card (when its agent is on one).</summary>
    public string About => Item.Kind == NeedKind.Decision ? Item.CardTitle ?? ""
        : Item.Card is { } c ? $"{c}{(Item.CardTitle is { Length: > 0 } t ? ": " + t : "")}" : "";

    public bool HasAbout => About.Length > 0;
    public bool HasDetail => Item.Detail.Length > 0;

    /// <summary>A permission's tool and what it would run; a decision's context; nothing for a question (its questions say it).</summary>
    public string Heading => Item.Kind == NeedKind.Question ? "" : Item.Title;
    public bool HasHeading => Heading.Length > 0;

    /// <summary>Several questions, or a multi-select one: picks are made first, then Send sends them together.</summary>
    public bool NeedsSend => IsQuestion && (Questions.Count > 1 || Questions.Any(q => q.MultiSelect));

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))] bool _sending;
    [ObservableProperty] string _error = "";
    [ObservableProperty] string _notes = "";

    public NeedViewModel(NeedsYouViewModel owner, NeedsYouItem item)
    {
        _owner = owner;
        Item = item;
        // a decision's question is the heading already: its options alone
        Questions = item.Questions.Select(q => new NeedQuestionViewModel(this, q, item.Kind == NeedKind.Decision)).ToList();
    }

    internal void Picked(NeedQuestionViewModel q)
    {
        SendCommand.NotifyCanExecuteChanged();
        if (!NeedsSend && !q.MultiSelect && q.Chosen is { } label)
            _ = IsDecision ? Decide(label) : SendAnswers();
    }

    bool CanSend() => !Sending && Questions.All(q => q.Answer.Length > 0);

    [RelayCommand(CanExecute = nameof(CanSend))]
    Task Send() => SendAnswers();

    async Task SendAnswers() => await Run(c => c.AnswerQuestion(Item.Agent!, Item.At, Questions.ToDictionary(q => q.Question.Question, q => q.Answer)));

    [RelayCommand]
    Task Allow() => Run(c => c.AnswerPermission(Item.Agent!, Item.At, true));

    [RelayCommand]
    Task Deny() => Run(c => c.AnswerPermission(Item.Agent!, Item.At, false));

    Task Decide(string label) => Run(c => c.Decide(Item.Project, Item.Card!, label, Notes.Trim()));

    async Task Run(Func<DargClient, Task<JsonNode?>> call)
    {
        if (_owner.Client is not { } client || Sending) return;
        Sending = true; Error = "";
        try
        {
            await call(client);
            _owner.Answered(this);
        }
        catch (DargClientException e)
        {
            Error = e.Message;
            foreach (var q in Questions) q.Clear(); // nothing went: the options can be tapped again
        }
        finally { Sending = false; }
    }
}

/// <summary>One question of a row, its options as buttons. A plain question takes one tap; a multi-select one toggles.</summary>
public sealed partial class NeedQuestionViewModel : ObservableObject
{
    readonly NeedViewModel _row;

    public NeedQuestion Question { get; }
    public bool MultiSelect => Question.MultiSelect;
    public IReadOnlyList<NeedChoiceViewModel> Options { get; }

    /// <summary>The question's own words, shown above its options (left out for a decision, whose heading says it).</summary>
    public string Text { get; }
    public bool HasText => Text.Length > 0;
    public string Header => Question.Header;
    public bool HasHeader => Header.Length > 0;

    [ObservableProperty] string? _chosen;

    /// <summary>What goes back for it: the label tapped, or the multi-select picks joined with ", " as the desktop does.</summary>
    public string Answer => MultiSelect ? string.Join(", ", Options.Where(o => o.On).Select(o => o.Option.Label)) : Chosen ?? "";

    public NeedQuestionViewModel(NeedViewModel row, NeedQuestion question, bool isDecision)
    {
        _row = row;
        Question = question;
        Text = isDecision ? "" : question.Question;
        Options = question.Options.Select(o => new NeedChoiceViewModel(this, o)).ToList();
    }

    internal void Tap(NeedChoiceViewModel choice)
    {
        if (MultiSelect) choice.On = !choice.On;
        else
        {
            foreach (var o in Options) o.On = o == choice;
            Chosen = choice.Option.Label;
        }
        _row.Picked(this);
    }

    internal void Clear()
    {
        if (MultiSelect) return; // toggles stay as picked, to send again
        foreach (var o in Options) o.On = false;
        Chosen = null;
    }
}

/// <summary>An option's button: its label, what it means, recommended or not, and (a decision's) what picking it does.</summary>
public sealed partial class NeedChoiceViewModel(NeedQuestionViewModel question, NeedOption option) : ObservableObject
{
    public NeedOption Option { get; } = option;
    public string Label => Option.Label;
    public string Description => Option.Description;
    public bool HasDescription => Description.Length > 0;
    public bool Recommended => Option.Recommended;

    /// <summary>A decision option's effect in a few words ("→ done"), as the card panel's corner says it.</summary>
    public string ActionText => Option.Action switch
    {
        "done" => "→ done", "back" => "→ send back", "stay" => "→ stays", "agent" => "→ to the agent", _ => "",
    };

    public bool HasAction => ActionText.Length > 0;

    [ObservableProperty] bool _on;

    [RelayCommand]
    void Tap() => question.Tap(this);
}
