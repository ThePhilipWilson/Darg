using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

public partial class AgentsViewModel(ProjectConfig project) : ObservableObject
{
    public ObservableCollection<AgentViewModel> Agents { get; } = [];
    [ObservableProperty] public partial AgentViewModel? Selected { get; set; }
    [ObservableProperty] public partial string NewPrompt { get; set; } = "";
    public string Folder => project.WorkingFolder();
    /// <summary>True while any agent here waits on the person (shown on the project's tab).</summary>
    [ObservableProperty] public partial bool NeedingYou { get; set; }
    public event Action<AgentViewModel>? AgentNeedsYou;
    /// <summary>Something the person may want to hear about: (agent, what happened, detail).</summary>
    public event Action<AgentViewModel, string, string>? Notify;
    public AgentViewModel? Find(string id) => Agents.FirstOrDefault(a => a.Id == id);
    /// <summary>The newest agent started on a card that hasn't ended.</summary>
    public AgentViewModel? ForCard(string cardId) =>
        Agents.LastOrDefault(a => a.CardId == cardId && a.State != AgentState.Exited);

    [RelayCommand]
    void StartAgent()
    {
        var prompt = NewPrompt.Trim();
        if (prompt.Length == 0) return;
        Start(prompt);
        NewPrompt = "";
    }

    public AgentViewModel Start(string prompt, string? cwd = null, string? cardId = null, string? name = null)
    {
        var agent = new AgentViewModel(string.IsNullOrWhiteSpace(cwd) ? project.WorkingFolder() : cwd, prompt, cardId, name);
        agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AgentViewModel.State)) return;
            NeedingYou = Agents.Any(a => a.State == AgentState.NeedsYou);
            if (agent.State == AgentState.NeedsYou) AgentNeedsYou?.Invoke(agent);
        };
        agent.Notify += (what, detail) => Notify?.Invoke(agent, what, detail);
        Agents.Add(agent);
        Selected = agent;
        return agent;
    }

    /// <summary>The prompt for working on a card: the card itself, where its file is, and the house rules.</summary>
    public static string CardPrompt(string projectName, Card card, string file)
    {
        var lines = new List<string> { $"Work on this card from the {projectName} board.", "", $"{card.Id}: {card.Title}" };
        if (card.Labels.Count > 0) lines.Add($"Labels: {string.Join(", ", card.Labels)}");
        if (card.Priority.Length > 0) lines.Add($"Priority: {card.Priority}");
        lines.Add($"Card file: {file}");
        if (card.Description.Length > 0) { lines.Add(""); lines.Add(card.Description); }
        lines.Add("");
        lines.Add("Read the project's CLAUDE.md first. Use AskUserQuestion when a choice is mine to make. " +
                  "When you stop, say what is done and what is left. Leave the card's Status alone: I move cards.");
        return string.Join(Environment.NewLine, lines);
    }
}

public enum AgentState { Starting, Working, NeedsYou, Idle, Exited }

/// <summary>One agent and its transcript. Session events arrive on a reader thread and are applied on
/// the UI thread.</summary>
public partial class AgentViewModel : ObservableObject
{
    static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    readonly Dictionary<string, ToolItem> _tools = [];
    AgentSession? _session;

    public ObservableCollection<TranscriptItem> Items { get; } = [];
    public string Id { get; } = Guid.NewGuid().ToString("N")[..10];
    /// <summary>The board card this agent was started on, if any.</summary>
    public string? CardId { get; }
    public event Action<string, string>? Notify;
    public string Name { get; }
    public string Cwd { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText), nameof(Working), nameof(NeedsYou))] public partial AgentState State { get; set; }
    [ObservableProperty] public partial string Compose { get; set; } = "";
    [ObservableProperty] public partial string Model { get; set; } = "";
    public bool Working => State is AgentState.Working or AgentState.Starting;
    public bool NeedsYou => State == AgentState.NeedsYou;
    public string StateText => State switch
    {
        AgentState.Starting => "starting", AgentState.Working => "working", AgentState.NeedsYou => "needs you",
        AgentState.Idle => "idle", _ => "ended",
    };

    public AgentViewModel(string cwd, string prompt, string? cardId = null, string? name = null, IEnumerable<string>? args = null)
    {
        Cwd = cwd;
        CardId = cardId;
        Name = name ?? (prompt.Length > 40 ? prompt[..40].TrimEnd() + "…" : prompt);
        Items.Add(new UserItem(prompt));
        try
        {
            _session = AgentSession.Start(cwd, extraArgs: args);
            _session.Event += e => Dispatcher.UIThread.Post(() => On(e));
            _session.Send(prompt);
            State = AgentState.Starting;
        }
        catch (Exception e)
        {
            Items.Add(new NoteItem($"Couldn't start Claude: {e.Message}", true));
            State = AgentState.Exited;
        }
    }

    void On(AgentEvent e)
    {
        switch (e)
        {
            case SessionStarted s:
                Model = s.Model;
                State = AgentState.Working;
                break;
            case AssistantText t:
                Items.Add(new TextItem(t.Text));
                break;
            case ToolCall c:
                var item = ToolItem.From(c);
                _tools[c.Id] = item;
                Items.Add(item);
                break;
            case ToolResult r when _tools.TryGetValue(r.ToolUseId, out var tool):
                tool.SetResult(r.Text, r.IsError);
                break;
            case PermissionAsked { ToolName: "AskUserQuestion" } q:
                var question = new QuestionItem(q, this);
                Items.Add(question);
                State = AgentState.NeedsYou;
                Notify?.Invoke("has a question", question.Questions.FirstOrDefault()?.Question ?? "");
                break;
            case PermissionAsked p:
                var perm = new PermissionItem(p, this);
                Items.Add(perm);
                State = AgentState.NeedsYou;
                Notify?.Invoke($"wants to use {p.ToolName}", perm.Summary);
                break;
            case TurnFinished f:
                if (f.IsError) Items.Add(new NoteItem(f.Text, true));
                Items.Add(new NoteItem(f.CostUsd is { } cost ? $"Turn done · ${cost:0.000}" : "Turn done", false));
                State = AgentState.Idle;
                Notify?.Invoke(f.IsError ? "stopped with an error" : "finished",
                    Items.OfType<TextItem>().LastOrDefault()?.Text ?? f.Text);
                break;
            case SessionExited x:
                Items.Add(new NoteItem(x.ExitCode == 0 ? "Session ended" : $"Session ended ({x.ExitCode}) {Last(x.Stderr)}", x.ExitCode != 0));
                State = AgentState.Exited;
                break;
        }
    }

    static string Last(string s) => string.Join(" ", s.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(3));

    [RelayCommand]
    void Send()
    {
        var text = Compose.Trim();
        if (text.Length == 0 || _session is null || State == AgentState.Exited) return;
        Items.Add(new UserItem(text));
        _session.Send(text);
        Compose = "";
        State = AgentState.Working;
    }

    [RelayCommand]
    void Stop() => _session?.Interrupt();

    /// <summary>End the session for good (its project is closing).</summary>
    public void End()
    {
        var s = _session;
        _session = null;
        if (s is not null) _ = s.DisposeAsync().AsTask();
    }

    internal void Answered(TranscriptItem item, Action<AgentSession> respond)
    {
        if (_session is null) return;
        respond(_session);
        State = Items.OfType<IPending>().Any(p => p.Pending) ? AgentState.NeedsYou : AgentState.Working;
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
    [ObservableProperty] public partial string? Result { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }
    [ObservableProperty] public partial bool Done { get; set; }

    public void SetResult(string text, bool error)
    {
        Done = true;
        IsError = error;
        // Successful results are noise in the transcript; errors and short outputs are worth a line.
        if (error || Name is "Bash" or "PowerShell" or "Grep" or "Glob")
            Result = text.Length > 1200 ? text[..1200] + "\n…" : text;
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
            Removed = removed, Added = added, Preview = preview };
    }

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
            var opts = (q?["options"] as JsonArray ?? []).Select(o => new QuestionOption(
                (string?)o?["label"] ?? "", (string?)o?["description"] ?? "", this, text)).ToList();
            Questions.Add(new QuestionGroup((string?)q?["header"] ?? "", text, opts));
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

public sealed partial class QuestionGroup(string header, string question, IReadOnlyList<QuestionOption> options) : ObservableObject
{
    public string Header { get; } = header;
    public string Question { get; } = question;
    public IReadOnlyList<QuestionOption> Options { get; } = options;
    [ObservableProperty] public partial string? Chosen { get; set; }
}

public sealed class QuestionOption(string label, string description, QuestionItem owner, string question)
{
    public string Label { get; } = label;
    public string Description { get; } = description;
    public IRelayCommand Command { get; } = new RelayCommand(() => owner.Pick(question, label));
}
