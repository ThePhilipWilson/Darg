using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>Fettle (once called Bench), the mascot: Claude sessions for the whole instance. It can see every project's
/// boards, notes, docs and repos (--add-dir) and is told how the board and note files are laid out.
/// Every message carries a line saying what Philip is looking at (project, section, open doc or card),
/// so "sort these into folders" means the docs he has open, not every project's. Chats are kept: New
/// chat starts another, and the list switches back to an old one (resuming its session). Each chat
/// belongs to the project it was started in and lists only there, like a project's own agents;
/// switching project tabs switches Bench to that project's chat, leaving the other one running.</summary>
public partial class MascotViewModel(IReadOnlyList<ProjectConfig> projects) : ObservableObject
{
    static readonly ObservableCollection<TranscriptItem> Nothing = [];
    /// <summary>The instance's projects (updated when Setup saves; a session already running keeps what it was given).</summary>
    public IReadOnlyList<ProjectConfig> Projects { get; set; } = projects;
    /// <summary>Set by the app: a line saying what Philip is looking at right now.</summary>
    public Func<string>? Context { get; set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Items), nameof(HasChat))] public partial AgentViewModel? Agent { get; set; }
    [ObservableProperty] public partial bool Open { get; set; }
    [ObservableProperty] public partial string Compose { get; set; } = "";
    public ObservableCollection<TranscriptItem> Items => Agent?.Items ?? Nothing;
    public bool HasChat => Agent is not null;
    public event Action<AgentViewModel, string, string>? Notify;

    /// <summary>Saved chats, newest first, to switch between.</summary>
    public ObservableCollection<ChatEntry> Chats { get; } = [];
    [ObservableProperty] public partial ChatEntry? SelectedChat { get; set; }
    bool _listing, _shown;
    /// <summary>The project on screen: its chats are the ones listed. Null: no project (every chat lists).</summary>
    public string? ProjectId { get; private set; }
    /// <summary>The chat each project had open, kept (and running) while another project is on screen.</summary>
    readonly Dictionary<string, AgentViewModel?> _open = new(StringComparer.OrdinalIgnoreCase);

    static string Store => AgentStore.Folder("~bench");

    /// <summary>Tidy the saved chats at start-up. Chats with no real title yet (older ones were "Bench" or a
    /// cut-off first message) get one. Chats from before chats had a project get the one whose name they
    /// mention; one that names none stays in every project's list. The list itself comes with ShowProject.</summary>
    public void Restore()
    {
        foreach (var r in AgentStore.List(Store))
        {
            if (r.Project is null && Guess(r) is { } p) { r.Project = p; AgentStore.Save(Store, r); }
            if (NeedsTitle(r.Name) && r.Items.FirstOrDefault(i => i.Kind == "user")?.Text is { } first) _ = Retitle(r.Id, first);
        }
    }

    string? Guess(AgentRecord r)
    {
        var text = r.Name + " " + r.Items.FirstOrDefault(i => i.Kind == "user")?.Text;
        var named = Projects.Where(p => Regex.IsMatch(text, $@"\b{Regex.Escape(p.Name)}\b", RegexOptions.IgnoreCase)).ToList();
        return named.Count == 1 ? named[0].Id : null;
    }

    bool Mine(string? project) => ProjectId is null || project is null || string.Equals(project, ProjectId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The project on screen changed: Fettle shows that project's chats, and the chat it had open
    /// (the newest saved one, the first time). The chat left behind keeps running and still notifies.</summary>
    public void ShowProject(string? projectId)
    {
        if (_shown && string.Equals(projectId, ProjectId, StringComparison.OrdinalIgnoreCase)) return;
        _shown = true; // no save here: a chat saves after every turn, and saving bumps its time in the list
        if (ProjectId is not null) _open[ProjectId] = Agent;
        ProjectId = projectId;
        if (projectId is not null && _open.TryGetValue(projectId, out var kept)) Agent = kept;
        else Agent = AgentStore.List(Store).FirstOrDefault(r => Mine(r.Project)) is { } newest ? Load(newest) : null;
        ListChats();
    }

    /// <summary>The project a Bench chat belongs to (for a notification about it), or null.</summary>
    public string? ProjectOf(string chatId) =>
        Live().FirstOrDefault(a => a.Id == chatId)?.Project ?? AgentStore.List(Store).FirstOrDefault(r => r.Id == chatId)?.Project;

    /// <summary>Open a chat by id, in whatever project is on screen (after the caller has switched to its project).</summary>
    public void OpenChat(string chatId)
    {
        if (Agent?.Id != chatId && Chats.FirstOrDefault(c => c.Id == chatId) is { } c) SelectedChat = c;
    }

    /// <summary>Save every live chat, each project's (on closing, so nothing since the last turn is lost).</summary>
    public void SaveAll() { foreach (var a in Live()) a.Save(); }

    /// <summary>A saved chat as an agent: the live one if it's already open somewhere (an untagged chat lists in every project).</summary>
    AgentViewModel Load(AgentRecord r) => Live().FirstOrDefault(a => a.Id == r.Id) ?? Wire(new AgentViewModel(r, Store));

    /// <summary>The chats running or open now: the one on screen and each other project's.</summary>
    public IEnumerable<AgentViewModel> Live() => _open.Values.Append(Agent).OfType<AgentViewModel>().Distinct();

    static bool NeedsTitle(string name) => name is "Bench" or "" || name.EndsWith('…');

    /// <summary>Give a chat a short title (Haiku, else a guess from the words) and save it.</summary>
    async Task Retitle(string id, string firstMessage)
    {
        var title = await ChatTitle.Make(firstMessage);
        if (Live().FirstOrDefault(a => a.Id == id) is { } live) { live.Name = title; live.Save(); }
        else if (AgentStore.List(Store).FirstOrDefault(r => r.Id == id) is { } r) { r.Name = title; AgentStore.Save(Store, r); }
        ListChats();
    }

    void ListChats()
    {
        _listing = true;
        var keep = Agent?.Id;
        Chats.Clear();
        foreach (var r in AgentStore.List(Store).Where(r => Mine(r.Project))) Chats.Add(new ChatEntry(r.Id, r.Name, r.Updated));
        SelectedChat = Chats.FirstOrDefault(c => c.Id == keep);
        _listing = false;
    }

    partial void OnSelectedChatChanged(ChatEntry? value)
    {
        if (_listing || value is null || value.Id == Agent?.Id) return;
        if (AgentStore.List(Store).FirstOrDefault(r => r.Id == value.Id) is not { } r) return;
        Agent?.Save();
        if (Agent is { } left && !_open.Values.Contains(left)) left.End(); // not if another project has it open too
        Agent = Load(r);
    }

    /// <summary>A new chat's model profile (WOR-0034): by a "kind: fettle" rule, else by what was asked.</summary>
    Task<ProfilePick?> ProfileFor(string text) => ModelProfiles.Pick(ModelProfiles.Load(), new AgentJob(JobKinds.Fettle, text, Project: ProjectId));

    AgentViewModel Wire(AgentViewModel a)
    {
        a.Notify += (what, detail) => Notify?.Invoke(a, what, detail);
        // typed in the Agents view too, it still hears where Philip is, and an untagged chat joins the project it carries on in
        a.Wrap = text => { a.Project ??= ProjectId; return WithContext(text); };
        return a;
    }

    string WithContext(string text)
    {
        var where = Context?.Invoke() ?? "";
        return where.Length > 0 ? $"[Philip is looking at: {where}]{Environment.NewLine}{Environment.NewLine}{text}" : text;
    }

    /// <summary>Start a new chat. The current one is kept in the list.</summary>
    [RelayCommand]
    void Fresh()
    {
        Agent?.Save();
        Agent?.End();
        Agent = null;
        ListChats();
    }

    /// <summary>Forget the open chat (Claude Code keeps its own history).</summary>
    [RelayCommand]
    void Forget()
    {
        if (Agent is null) return;
        Agent.End();
        AgentStore.Delete(Store, Agent.Id);
        Agent = null;
        ListChats();
    }

    [RelayCommand] void Toggle() => Open = !Open;

    /// <summary>Fettle is in the middle of something in the chat on screen.</summary>
    public bool Busy => Agent?.State is AgentState.Starting or AgentState.Working or AgentState.NeedsYou;

    /// <summary>A job from a button elsewhere (Organise on the notes wall): a new chat with this message,
    /// shown as <paramref name="shown"/>, with the box opened. The chat that was open stays in the list.
    /// False, and nothing sent, while the open chat is busy: ending it would cut Fettle off mid-turn.</summary>
    public bool Ask(string shown, string text)
    {
        if (Busy) return false;
        Fresh();
        var home = InstanceConfig.Folder();
        Directory.CreateDirectory(home);
        Agent = Wire(new AgentViewModel(home, WithContext(text), name: ChatTitle.Guess(shown), args: Args(), shown: shown, profile: ProfileFor(text)) { StoreFolder = Store, Project = ProjectId });
        Agent.Save();
        ListChats();
        Open = true;
        return true;
    }

    [RelayCommand]
    void Send()
    {
        var text = Compose.Trim();
        if (text.Length == 0) return;
        Compose = "";
        var sent = WithContext(text);
        if (Agent is null || (Agent.State == AgentState.Exited && Agent.SessionId is null))
        {
            var home = InstanceConfig.Folder();
            Directory.CreateDirectory(home);
            // a quick title from the words now, a better one from Haiku a few seconds later
            Agent = Wire(new AgentViewModel(home, sent, name: ChatTitle.Guess(text), args: Args(), shown: text, profile: ProfileFor(text)) { StoreFolder = Store, Project = ProjectId });
            Agent.Save();
            ListChats();
            _ = Retitle(Agent.Id, text);
            return;
        }
        Agent.Project ??= ProjectId; // as in Wire: an untagged chat joins the project it carries on in
        Agent.Say(text, sent);
    }

    IEnumerable<string> Args()
    {
        var dirs = Projects.SelectMany(p => new[] { p.Boards is null ? null : Path.GetDirectoryName(Path.GetFullPath(p.Boards)), p.NotesFolder(), p.Docs }
                .Concat(p.Repos.Cast<string?>()))
            .OfType<string>().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs) { yield return "--add-dir"; yield return d; }
        yield return "--append-system-prompt";
        yield return Briefing();
    }

    string Briefing()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Fettle, the assistant inside Workbench, Philip's app for his projects: a small round amber creature who lives in the corner of the window. He talks to you from a little box there, so keep replies short.");
        sb.AppendLine("Your manner is dry and wry: understated British humour, the odd quip when one comes naturally (\"Done. Three cards, none of them on fire.\"), never at the expense of the answer or of brevity. No exclamation marks, no cheerleading, no apologising for being an AI. When something's wrong or risky, say so plainly and drop the jokes.");
        sb.AppendLine("Each of his messages starts with [Philip is looking at: ...]: the project, the section and what's open there. Read his words against it: \"these\", \"them\", \"here\", \"the docs\", \"this card\" mean what's in that context, and changes stay inside it (that project, that folder) unless he names something else. If his words could reach further than what he's looking at, ask with AskUserQuestion first.");
        sb.AppendLine("The projects open in this Workbench:");
        foreach (var p in Projects)
        {
            sb.Append($"- {p.Name} (id {p.Id})");
            if (p.Boards is not null) sb.Append($"; boards in {Path.GetFullPath(p.Boards)}");
            if (p.NotesFolder() is { } n) sb.Append($"; notes in {n}");
            if (p.Docs is { } d) sb.Append($"; docs in {d}");
            if (p.Repos.Count > 0) sb.Append($"; repos: {string.Join(", ", p.Repos)}");
            sb.AppendLine();
        }
        sb.AppendLine("""
            Boards: each board is a folder with board.json (columns [{id,name}], prefix, nextId, order {list id: [card ids]}) and items/<ID> - <title>.md. A card file starts "# <ID>: <title>", then "- **Status:** ...", "- **Priority:** ...", "- **Type:** ...", "- **Labels:** a, b", "- **Created:** ...", "- **Done:** ...", then "## Description". Status is Backlog, Completed or a column's name. To add a card: the next ID is past nextId and every ID in items, bump nextId, add the ID to order.backlog, and name the file "<ID> - <title>.md" (slashes as spaces, no : ? * < > | ", at most 60 characters of title).
            Notes: one .md per note, with properties at the top between --- lines (column, colour, tags: [a, b], created, ticket) and the text below.
            Docs are Obsidian notes: moving one breaks [[links]] to it unless you update them too.
            Change these files directly; Workbench shows changes within a second. Ask with AskUserQuestion before deleting anything or before changing more than 20 files. Never commit or push.
            """);
        if (ArtifactHost.Server is { } server)
            sb.AppendLine($$"""
                Workbench API: for anything that isn't a file edit (showing Philip a card, doc, section or agent, listing agents and reading their transcripts, starting an agent or putting one on a card, sending an agent a message, closing agents, running a flow or a saved command), use the API with curl. TOKEN is the text of the file artifact-token in your working folder. GET {{server.Root}}api/TOKEN lists the commands and what each takes; read it before your first call in a chat. Run one with POST {{server.Root}}api/TOKEN/NAME and its arguments as a JSON object in the body (curl -s -X POST URL -d '{"project":"x"}'). A 400 answer says what was wrong; correct it and try again.
                Starting agents and flows costs Philip sessions: do it only when he asks. Closing an agent that is working or needs him stops what it was doing: unless he named it, ask with AskUserQuestion first. Prefer show to telling him where something is.
                """);
        return sb.ToString();
    }
}

public sealed record ChatEntry(string Id, string Name, DateTime Updated)
{
    public string When => Updated.Date == DateTime.Today ? Updated.ToString("HH:mm") : Updated.ToString("d MMM");
    public override string ToString() => $"{Name} · {When}";
}
