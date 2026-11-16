using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

public partial class AgentsViewModel : ObservableObject
{
    readonly ProjectConfig project;
    readonly string _store;

    /// <summary>A project's agents, with the ones saved by earlier runs listed (newest first) to carry on.</summary>
    public AgentsViewModel(ProjectConfig project)
    {
        this.project = project;
        _store = AgentStore.Folder(project.Id.Length > 0 ? project.Id : "default");
        foreach (var r in AgentStore.List(_store)) Add(new AgentViewModel(r, _store), select: false);
        Selected = Agents.FirstOrDefault();
        // older agents were named by cutting off their first message: give those a proper title
        foreach (var a in Agents.Where(a => a.CardId is null && a.Name.EndsWith('…')).ToList())
            if (a.Items.OfType<UserItem>().FirstOrDefault()?.Text is { } first) _ = Retitle(a, first);
    }

    /// <summary>Give an agent a short title (Haiku, else a guess from the words) and save it.</summary>
    static async Task Retitle(AgentViewModel a, string firstMessage)
    {
        a.Name = await ChatTitle.Make(firstMessage);
        a.Save();
    }

    public ObservableCollection<AgentViewModel> Agents { get; } = [];

    /// <summary>Bench, shared by every project: its chats are listed here too, under their own heading.</summary>
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

    /// <summary>Forget an agent: off the list and its saved file gone (Claude Code's own history stays).</summary>
    [RelayCommand]
    void Forget(AgentViewModel? a)
    {
        if (a is null) return;
        a.End();
        AgentStore.Delete(_store, a.Id);
        Agents.Remove(a);
        if (Selected == a) Selected = Agents.FirstOrDefault();
        if (Shown == a) Shown = Selected;
    }

    void Add(AgentViewModel agent, bool select)
    {
        agent.StoreFolder ??= _store;
        agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AgentViewModel.State)) return;
            NeedingYou = Agents.Any(a => a.State == AgentState.NeedsYou);
            if (agent.State == AgentState.NeedsYou) AgentNeedsYou?.Invoke(agent);
        };
        agent.Notify += (what, detail) => Notify?.Invoke(agent, what, detail);
        if (select) { Agents.Insert(0, agent); Selected = agent; }
        else Agents.Add(agent);
    }

    public void SaveAll() { foreach (var a in Agents) a.Save(); }
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
        var agent = new AgentViewModel(string.IsNullOrWhiteSpace(cwd) ? project.WorkingFolder() : cwd, prompt, cardId, name ?? ChatTitle.Guess(prompt)) { StoreFolder = _store };
        Add(agent, select: true);
        agent.Save();
        if (name is null) _ = Retitle(agent, prompt); // a card's agent is already named for the card
        return agent;
    }

    /// <summary>The prompt for working on a card: the card itself, where its file is, and the house rules.</summary>
    /// <param name="alongside">Other agents at work in the same folder: cards started in the same batch and
    /// agents already busy there.</param>
    /// <param name="repo">The folder, when it is a git repository: then the agent is told how to decide on,
    /// and use, a worktree of its own.</param>
    public static string CardPrompt(string projectName, Card card, string file, IReadOnlyList<string>? alongside = null, string? repo = null)
    {
        var lines = new List<string> { $"Work on this card from the {projectName} board.", "", $"{card.Id}: {card.Title}" };
        if (card.Labels.Count > 0) lines.Add($"Labels: {string.Join(", ", card.Labels)}");
        if (card.Priority.Length > 0) lines.Add($"Priority: {card.Priority}");
        lines.Add($"Card file: {file}");
        if (card.Description.Length > 0) { lines.Add(""); lines.Add(card.Description); }
        lines.Add("");
        lines.Add("Read the project's CLAUDE.md first. Use AskUserQuestion when a choice is mine to make. " +
                  "When you stop, say what is done and what is left. Leave the card's Status alone: I move cards.");
        if (alongside is { Count: > 0 })
        {
            lines.Add("");
            lines.Add($"Other agents are working on {string.Join(", ", alongside)} in the same folder at the same time. " +
                      "Change only what this card needs, expect files you didn't touch to change under you, " +
                      "and stage and commit only the files you changed yourself.");
            if (repo is not null) lines.AddRange(WorktreeRules(card.Id.ToLowerInvariant(), repo));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Whether a card's agent needs its own worktree is its call: only it knows, once it has read
    /// the card, whether it will build or edit code. These are the rules it decides by and works to.</summary>
    static IEnumerable<string> WorktreeRules(string branch, string repo)
    {
        var dir = $".claude/worktrees/{branch}";
        yield return "";
        yield return "Decide for yourself, once you know what the card needs, whether to work in a git worktree of your own. " +
                     "Use one if you will edit source code, build, or run tests or the app: another agent building or editing here " +
                     "at the same moment can break your build, lock its output, or end up in your commit. Stay in this folder " +
                     "if you will only read, answer a question, or edit notes, docs or card files the other cards don't touch. " +
                     "I don't want to be asked: make the call, and say in your last message which you chose and why.";
        yield return $"To use one: add `.claude/worktrees/` to `.git/info/exclude` if it isn't there; `git worktree add {dir} -b {branch} HEAD` " +
                     "(from local HEAD, so unpushed commits come too); then EnterWorktree with that path (without the tool, work there by " +
                     $"absolute path). Untracked local files such as CLAUDE.local.md aren't in the worktree: read them from {repo}. " +
                     "Commit on your branch.";
        yield return $"When the work is done: rebase your branch onto the branch {repo} is on, then in {repo} run `git merge --ff-only {branch}` " +
                     "and push if the project's rules say to. If the merge is refused (another agent's uncommitted change in the way, or a conflict " +
                     $"you can't settle), leave the branch and say so. Once merged, step out before cleaning up (Windows won't delete a folder " +
                     $"a session is still in): ExitWorktree with keep, then `cd {repo}`, then `git -C {repo} worktree remove {dir}` " +
                     $"and `git -C {repo} branch -d {branch}`.";
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
    public event Action<string, string>? Notify;
    [ObservableProperty] public partial string Name { get; set; }
    public string Cwd { get; }
    /// <summary>Claude Code's id for the conversation: what --resume takes.</summary>
    public string? SessionId { get; private set; }
    /// <summary>Where this agent is saved (set by its owner); null: not saved.</summary>
    public string? StoreFolder { get; set; }
    readonly List<string> _args;
    DateTime _created = DateTime.Now;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateText), nameof(Working), nameof(NeedsYou))] public partial AgentState State { get; set; }
    [ObservableProperty] public partial string Compose { get; set; } = "";
    [ObservableProperty] public partial string Model { get; set; } = "";
    /// <summary>How the session is paid for: "on your subscription", or which API key it uses.</summary>
    [ObservableProperty] public partial string Billing { get; set; } = "";
    bool OnSubscription { get; set; } = true;
    public bool Working => State is AgentState.Working or AgentState.Starting;
    public bool NeedsYou => State == AgentState.NeedsYou;
    public string StateText => State switch
    {
        AgentState.Starting => "starting", AgentState.Working => "working", AgentState.NeedsYou => "needs you",
        AgentState.Idle => "idle", AgentState.Saved => "saved · send to carry on", _ => "ended",
    };

    /// <param name="shown">What the transcript shows for the first message, when what's sent carries more
    /// (Bench sends a line of context ahead of the person's words).</param>
    public AgentViewModel(string cwd, string prompt, string? cardId = null, string? name = null, IEnumerable<string>? args = null, string? shown = null)
    {
        Cwd = cwd;
        CardId = cardId;
        _args = args?.ToList() ?? [];
        var visible = shown ?? prompt;
        Name = name ?? (visible.Length > 40 ? visible[..40].TrimEnd() + "…" : visible);
        Items.Add(new UserItem(visible));
        Begin(prompt, resume: false);
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
        State = AgentState.Working;
    }

    /// <summary>An agent from an earlier run of Workbench: its transcript, and its session to resume.</summary>
    public AgentViewModel(AgentRecord r, string storeFolder)
    {
        Id = r.Id; Name = r.Name; Cwd = r.Cwd; CardId = r.CardId; SessionId = r.SessionId;
        _args = r.Args; _created = r.Created; StoreFolder = storeFolder;
        foreach (var i in r.Items) Items.Add(FromRecord(i));
        State = AgentState.Saved;
    }

    /// <summary>Start (or resume) the Claude session and send it a message.</summary>
    void Begin(string message, bool resume)
    {
        try
        {
            var args = resume && SessionId is not null ? ["--resume", SessionId, .. _args] : _args;
            _session = AgentSession.Start(Cwd, extraArgs: args);
            _session.Event += e => Dispatcher.UIThread.Post(() => On(e));
            _session.Send(message);
            State = AgentState.Starting;
        }
        catch (Exception e)
        {
            Items.Add(new NoteItem($"Couldn't start Claude: {e.Message}", true));
            State = AgentState.Exited;
        }
    }

    /// <summary>Write this agent to its store (after every turn and on exit).</summary>
    public void Save()
    {
        if (StoreFolder is null) return;
        try
        {
            AgentStore.Save(StoreFolder, new AgentRecord
            {
                Id = Id, Name = Name, Cwd = Cwd, CardId = CardId, SessionId = SessionId, Args = _args,
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
        switch (e)
        {
            case SessionStarted s:
                Model = s.Model;
                SessionId = s.SessionId;
                OnSubscription = s.OnSubscription;
                Billing = s.OnSubscription ? "on your subscription" : $"API key ({s.ApiKeySource})";
                State = AgentState.Working;
                Save();
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
                tool.SetResult(r, Cwd);
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
                // Claude Code reports a cost on every turn, priced as if on the API; on a subscription
                // nothing is charged per turn, so only an API-key session shows it
                Items.Add(new NoteItem(!OnSubscription && f.CostUsd is { } cost ? $"Turn done · ${cost:0.000} (API key)" : "Turn done", false));
                State = AgentState.Idle;
                Save();
                Notify?.Invoke(f.IsError ? "stopped with an error" : "finished",
                    Items.OfType<TextItem>().LastOrDefault()?.Text ?? f.Text);
                break;
            case SessionExited x:
                // a session we closed ourselves (resume later) isn't news; one that died is
                if (x.ExitCode != 0) Items.Add(new NoteItem($"Session ended ({x.ExitCode}) {Last(x.Stderr)}", true));
                State = SessionId is not null && x.ExitCode == 0 ? AgentState.Saved : AgentState.Exited;
                _session = null;
                Save();
                break;
        }
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
            Removed = removed, Added = added, Preview = preview, InputText = c.Input.ToJsonString() };
    }

    /// <summary>A tool line from a saved transcript (its image read again from disk if it's still there).</summary>
    public static ToolItem Restore(ItemRecord r)
    {
        var image = AgentViewModel.IsImage(r.ImagePath) && File.Exists(r.ImagePath) ? TryLoad(r.ImagePath!) : null;
        var item = new ToolItem
        {
            Name = r.Name ?? "", Summary = r.Summary ?? "", Image = image, ImagePath = image is null ? null : r.ImagePath,
            Removed = r.Removed, Added = r.Added, Preview = r.Preview, Result = r.Result, IsError = r.Bad, Done = true,
        };
        foreach (var p in r.Images ?? []) if (File.Exists(p)) item.AddPreview(p);
        return item;
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
