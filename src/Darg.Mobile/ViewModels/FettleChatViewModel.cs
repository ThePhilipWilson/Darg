using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Mobile.ViewModels;

/// <summary>What the phone is showing when Philip talks to Fettle: the section, and the card or doc open in it.</summary>
public sealed record PhoneView(string? Section, string? Card = null, string? Doc = null);

/// <summary>Fettle on the phone (AND-0007): a full-screen chat opened from a project page, sending each message with what
/// the page shows (<see cref="PhoneView"/>), so Fettle knows where he is. Fettle's questions come as tappable options,
/// a permission ask as Allow and Deny, and the mic dictates (sent with <c>voice</c>, so Fettle reads past slips).
/// Everything goes through the desktop's fettle-* commands (<see cref="FettleRemote"/>); the chat is polled while
/// Fettle is at work.</summary>
public sealed partial class FettleChatViewModel : PageViewModel
{
    public static TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(1);

    readonly FettleRemote _remote;
    readonly string _project;
    readonly string _projectName;
    readonly Func<PhoneView> _showing;
    readonly IVoiceInput? _voice;
    readonly DispatcherTimer _timer;
    bool _reading;
    bool _dictated;
    int _nextFrom;

    public override string Title => $"Fettle · {_projectName}";

    public ObservableCollection<FettleRow> Rows { get; } = [];

    /// <summary>The desktop's id for this chat once the first message made it; null until then (a new chat).</summary>
    [ObservableProperty] string? _chatId;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))] string _compose = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendCommand))] bool _sending;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DictateCommand))] bool _listening;
    [ObservableProperty] bool _busy;
    [ObservableProperty] string _status = "";
    [ObservableProperty] bool _bad;

    /// <summary>What Fettle will be told he's looking at, as a dim line above the box.</summary>
    public string Sees
    {
        get
        {
            var v = _showing();
            var line = v.Section is { Length: > 0 } s ? $"{_projectName} › {s}" : _projectName;
            if (v.Card is { Length: > 0 }) line += $", card {v.Card}";
            if (v.Doc is { Length: > 0 }) line += $", doc {v.Doc}";
            return "Fettle sees: " + line;
        }
    }

    public bool CanDictate => _voice?.Available == true;
    public bool Empty => Rows.Count == 0;

    public FettleChatViewModel(Func<string, JsonObject, Task<string>> post, string project, string projectName, Func<PhoneView> showing, IVoiceInput? voice = null)
    {
        _remote = new FettleRemote(post);
        _project = project;
        _projectName = projectName;
        _showing = showing;
        _voice = voice ?? Voice.Input;
        _timer = new DispatcherTimer(PollEvery, DispatcherPriority.Background, (_, _) => _ = Poll());
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Empty));
    }

    /// <summary>The chat as the phone reaches it through its <see cref="DargClient"/>.</summary>
    public static FettleChatViewModel For(DargClient client, ProjectInfo project, Func<PhoneView> showing) =>
        new(async (name, args) => (await client.Call(name, args))?.ToJsonString() ?? "", project.Id, project.Name, showing);

    /// <summary>Called as the chat comes on screen: what Fettle sees may have changed since it was last open.</summary>
    public void Shown() => OnPropertyChanged(nameof(Sees));

    partial void OnComposeChanged(string value)
    {
        if (value.Length == 0) _dictated = false;
    }

    bool CanSend() => !Sending && Compose.Trim().Length > 0;

    /// <summary>Sends what's in the box. While Fettle waits on a single question, the words answer it instead.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task Send()
    {
        var text = Compose.Trim();
        var voice = _dictated;
        await Run(async () =>
        {
            FettleChat chat;
            if (ChatId is { } id && Rows.LastOrDefault(r => r.Pending && r.Kind == "question") is { Questions.Count: 1 } q)
                chat = await _remote.Answer(id, new Dictionary<string, string> { [q.Questions[0].Question] = text });
            else
            {
                var v = _showing();
                chat = await _remote.Say(_project, text, ChatId, v.Section, v.Card, v.Doc, voice);
            }
            Compose = "";
            Take(chat);
        });
    }

    bool CanDictateNow() => !Listening;

    /// <summary>The mic: what's said lands in the box (added to anything typed), for a glance before Send.</summary>
    [RelayCommand(CanExecute = nameof(CanDictateNow))]
    public async Task Dictate()
    {
        if (_voice is null) return;
        Listening = true;
        try
        {
            var said = (await _voice.Listen())?.Trim();
            if (string.IsNullOrEmpty(said)) return;
            Compose = Compose.Trim().Length > 0 ? Compose.TrimEnd() + " " + said : said;
            _dictated = true;
        }
        catch (Exception e) { Fail("Couldn't listen: " + e.Message); }
        finally { Listening = false; }
    }

    /// <summary>Starts a fresh chat; the old one stays on the desktop's chat list.</summary>
    [RelayCommand]
    void NewChat()
    {
        _timer.Stop();
        ChatId = null;
        Rows.Clear();
        _nextFrom = 0;
        Busy = false;
        Status = "";
        Bad = false;
    }

    internal async Task Answer(FettleRow row)
    {
        if (ChatId is not { } id) return;
        var answers = row.Questions.ToDictionary(q => q.Question, q => q.Answer);
        await Run(async () => Take(await _remote.Answer(id, answers)));
    }

    internal async Task Allow(bool allow)
    {
        if (ChatId is not { } id) return;
        await Run(async () => Take(await _remote.Allow(id, allow)));
    }

    async Task Run(Func<Task> work)
    {
        Sending = true; Bad = false; Status = "";
        try { await work(); }
        catch (Exception e) { Fail(e.Message); }
        finally { Sending = false; }
    }

    void Fail(string message) { Bad = true; Status = message; }

    /// <summary>Reads what's new while Fettle works (or a question waits); stops once the turn has ended.</summary>
    public async Task Poll()
    {
        if (_reading || ChatId is not { } id) return;
        _reading = true;
        try { Take(await _remote.Read(id, _nextFrom)); }
        catch (Exception e) { Fail(e.Message); }
        finally { _reading = false; }
    }

    /// <summary>Folds a read of the chat into the rows: entries from its <c>From</c> on are replaced where they changed.</summary>
    void Take(FettleChat chat)
    {
        ChatId = chat.Id;
        foreach (var e in chat.Items)
        {
            var at = Rows.Count;
            for (var i = 0; i < Rows.Count; i++) if (Rows[i].Index >= e.Index) { at = i; break; }
            if (at < Rows.Count && Rows[at].Index == e.Index)
            {
                if (Rows[at].Signature != FettleRow.Sign(e)) Rows[at] = new FettleRow(e, this);
            }
            else Rows.Insert(at, new FettleRow(e, this));
        }
        _nextFrom = chat.NextFrom;
        Busy = chat.Busy;
        if (chat.Busy || chat.NeedsYou) _timer.Start(); else _timer.Stop();
    }
}

/// <summary>One entry of the chat as the phone draws it: his words on the right, Fettle's on the left, steps and notes
/// as a dim line, a question with its options as buttons, a permission ask with Allow and Deny.</summary>
public sealed partial class FettleRow : ObservableObject
{
    readonly FettleChatViewModel _chat;

    public int Index { get; }
    public string Kind { get; }
    public string Text { get; }
    public string? Detail { get; }
    public bool Pending { get; }
    public string Signature { get; }
    public IReadOnlyList<QuestionRow> Questions { get; }

    public bool IsUser => Kind == "user";
    public bool IsFettle => Kind == "fettle";
    public bool IsStep => Kind is "tool" or "note";
    public bool IsQuestion => Kind == "question";
    public bool IsPermission => Kind == "permission";
    public bool PermissionWaiting => IsPermission && Pending;
    public string PermissionText => Pending ? $"Fettle wants to use {Text}" : $"Fettle asked to use {Text}";

    /// <summary>A question with more than one part, or more than one pick, needs a Send once each part is answered;
    /// a single one-pick question goes the moment an option is tapped.</summary>
    public bool NeedsSendButton => Pending && (Questions.Count > 1 || Questions.Any(q => q.MultiSelect));

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SendAnswersCommand))] bool _ready;

    public FettleRow(FettleEntry e, FettleChatViewModel chat)
    {
        _chat = chat;
        Index = e.Index;
        Kind = e.Kind;
        Text = e.Kind == "fettle" ? Plain(e.Text) : e.Text;
        Detail = e.Detail;
        Pending = e.Pending;
        Signature = Sign(e);
        Questions = e.Questions.Select(q => new QuestionRow(q, Pending, this)).ToList();
    }

    public static string Sign(FettleEntry e) =>
        $"{e.Kind}\u0001{e.Text}\u0001{e.Detail}\u0001{e.Pending}\u0001{string.Join("\u0002", e.Questions.Select(q => q.Chosen))}";

    /// <summary>Fettle writes markdown; the phone shows it as text: links as their words, no bold or code marks.</summary>
    public static string Plain(string md)
    {
        var s = Regex.Replace(md, @"\[([^\]]+)\]\([^)]+\)", "$1");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
        s = Regex.Replace(s, @"(?<![\w`])`([^`\n]+)`", "$1");
        s = Regex.Replace(s, @"^#{1,6}\s+", "", RegexOptions.Multiline);
        return s.Trim();
    }

    internal void Picked()
    {
        Ready = Questions.All(q => q.Answer.Length > 0);
        if (Ready && !NeedsSendButton) _ = _chat.Answer(this);
    }

    [RelayCommand(CanExecute = nameof(Ready))]
    Task SendAnswers() => _chat.Answer(this);

    [RelayCommand]
    Task Allow() => _chat.Allow(true);

    [RelayCommand]
    Task Deny() => _chat.Allow(false);
}

/// <summary>One question of a Fettle ask: its options to tap, or what was chosen once answered.</summary>
public sealed class QuestionRow
{
    readonly FettleRow _row;

    public string Header { get; }
    public string Question { get; }
    public bool MultiSelect { get; }
    public bool Open { get; }
    public string? Chosen { get; }
    public string ChosenLine => $"Answered: {Chosen}";
    public bool Answered => !Open && Chosen is { Length: > 0 };
    public IReadOnlyList<OptionRow> Options { get; }

    /// <summary>What goes back to Fettle: the tapped label, or the tapped labels joined for a many-pick question.</summary>
    public string Answer => string.Join(", ", Options.Where(o => o.Picked).Select(o => o.Label));

    public QuestionRow(FettleQuestion q, bool open, FettleRow row)
    {
        _row = row;
        Header = q.Header;
        Question = q.Question;
        MultiSelect = q.MultiSelect;
        Open = open;
        Chosen = q.Chosen;
        Options = q.Options.Select(o => new OptionRow(o.Label, o.Description, this)).ToList();
    }

    internal void Tapped(OptionRow option)
    {
        if (!Open) return;
        if (MultiSelect) option.Picked = !option.Picked;
        else foreach (var o in Options) o.Picked = ReferenceEquals(o, option);
        _row.Picked();
    }
}

public sealed partial class OptionRow(string label, string description, QuestionRow question) : ObservableObject
{
    public string Label { get; } = label;
    public string Description { get; } = description;
    public bool HasDescription => Description.Length > 0;
    public bool Open => question.Open;

    [ObservableProperty] bool _picked;

    [RelayCommand]
    void Tap() => question.Tapped(this);
}
