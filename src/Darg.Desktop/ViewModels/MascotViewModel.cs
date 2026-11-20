using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

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
    public event Action<AgentViewModel>? AsksSettled;

    /// <summary>Saved chats, newest first, to switch between.</summary>
    public ObservableCollection<ChatEntry> Chats { get; } = [];
    [ObservableProperty] public partial ChatEntry? SelectedChat { get; set; }
    bool _listing, _shown;
    /// <summary>The project on screen: its chats are the ones listed. Null: no project (every chat lists).</summary>
    public string? ProjectId { get; private set; }
    /// <summary>The chat each project had open, kept (and running) while another project is on screen.</summary>
    readonly Dictionary<string, AgentViewModel?> _open = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Chats left mid-turn (+ New chat, another chat picked), still running until that turn ends (WOR-0157).</summary>
    readonly HashSet<AgentViewModel> _finishing = [];
    /// <summary>The chat the phone is in, per project (AND-0007): kept running whatever the desktop shows.</summary>
    readonly Dictionary<string, AgentViewModel> _phone = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>The chats running or open now: the one on screen, each other project's, and those finishing a turn.</summary>
    public IEnumerable<AgentViewModel> Live() => _open.Values.Append(Agent).Concat(_finishing).Concat(_phone.Values).OfType<AgentViewModel>().Distinct();

    /// <summary>Held open somewhere other than the desktop's box: another project's, or the phone's.</summary>
    bool HeldElsewhere(AgentViewModel a) => _open.Values.Contains(a) || _phone.Values.Contains(a);

    /// <summary>A chat is no longer on screen. Its session ends, unless another project has it open or it's mid-turn:
    /// ending it then cut Fettle off after the work and before the reply, which came back to nothing (WOR-0157: a
    /// request, + New chat to type the next, and the first one's card was made with no word said). A chat mid-turn
    /// runs on, saving and notifying as ever, and its session ends once the turn has (unless it's back on screen).</summary>
    void LetGo(AgentViewModel left)
    {
        left.Save();
        if (HeldElsewhere(left)) return; // another project, or the phone, has it open: it carries on there
        if (!left.Working && !left.NeedsYou) { left.End(); return; }
        if (!_finishing.Add(left)) return;
        left.TurnEnded += Ended;
        // posted: not from inside the session's own event
        void Ended(bool _) => Dispatcher.UIThread.Post(() =>
        {
            if (left.Working || left.NeedsYou) return; // carried on (a message from the Agents view): wait for that turn
            left.TurnEnded -= Ended;
            _finishing.Remove(left);
            if (left != Agent && !HeldElsewhere(left)) left.End();
        });
    }

    static bool NeedsTitle(string name) => name is "Bench" or "" || name.EndsWith('…');

    /// <summary>Give a chat a short title (Haiku, else a guess from the words) and save it.</summary>
    async Task Retitle(string id, string firstMessage)
    {
        var title = await Task.Run(() => ChatTitle.Make(firstMessage)); // Haiku's process starts off the UI thread (WOR-0076)
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
        if (Agent is { } left) LetGo(left);
        Agent = Load(r);
    }

    /// <summary>A new chat's model profile (WOR-0034): by a "kind: fettle" rule, else by what was asked.</summary>
    Task<ProfilePick?> ProfileFor(string text, string? project = null)
    {
        var job = new AgentJob(JobKinds.Fettle, text, Project: project ?? ProjectId);
        return Task.Run(() => ModelProfiles.Pick(ModelProfiles.Load(), job)); // off the UI thread, as in AgentsViewModel.Start
    }

    AgentViewModel Wire(AgentViewModel a)
    {
        a.Notify += (what, detail) => Notify?.Invoke(a, what, detail);
        a.AsksSettled += () => AsksSettled?.Invoke(a);
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
        if (Agent is { } left) LetGo(left);
        Agent = null;
        ListChats();
    }

    /// <summary>Forget the open chat (Claude Code keeps its own history).</summary>
    [RelayCommand]
    void Forget()
    {
        if (Agent is not null) ForgetChat(Agent.Id);
    }

    /// <summary>A chat was forgotten (its id): a view showing it lets go.</summary>
    public event Action<string>? ChatForgotten;

    /// <summary>Forget a chat by id, open or not (the × in the Agents view's Fettle chats): its session ends
    /// wherever it's open, in any project, and its saved file goes. Claude Code keeps its own history.</summary>
    public void ForgetChat(string id)
    {
        foreach (var a in Live().Where(a => a.Id == id).ToList()) a.End();
        _finishing.RemoveWhere(a => a.Id == id);
        foreach (var k in _open.Where(kv => kv.Value?.Id == id).Select(kv => kv.Key).ToList()) _open[k] = null;
        AgentStore.Delete(Store, id);
        if (Agent?.Id == id) Agent = null;
        ListChats();
        ChatForgotten?.Invoke(id);
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
        // no session to carry on (never began, or saved without one): a new chat, not a message into the void (WOR-0133)
        if (Agent is null || (Agent.State is AgentState.Exited or AgentState.Saved && Agent.SessionId is null))
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

    /// <summary>A message from the phone (AND-0007, the fettle-say command): into <paramref name="chatId"/>, or a new chat
    /// in <paramref name="projectId"/> when there's none or it has no session to carry on. <paramref name="sent"/> carries
    /// the phone's own context line (<see cref="PhoneFettle.Message"/>), not the desktop's. The phone's chat stays
    /// running while the desktop shows other chats or projects; starting another lets the last one go, as + New chat does.</summary>
    public AgentViewModel PhoneSay(string projectId, string? chatId, string text, string sent)
    {
        var chat = chatId is null ? null : Find(chatId) ?? throw new ApiError($"No Fettle chat \"{chatId}\".");
        if (chat is { Working: true }) throw new ApiError($"Fettle is mid-turn in \"{chat.Name}\"; send when it's done.");
        if (chat is { NeedsYou: true }) throw new ApiError($"Fettle is waiting on an answer in \"{chat.Name}\"; answer it with fettle-answer.");
        if (chat is null || (chat.State is AgentState.Exited or AgentState.Saved && chat.SessionId is null))
        {
            var home = InstanceConfig.Folder();
            Directory.CreateDirectory(home);
            chat = Wire(new AgentViewModel(home, sent, name: ChatTitle.Guess(text), args: Args(), shown: text, profile: ProfileFor(text, projectId))
                { StoreFolder = Store, Project = projectId });
            chat.Save();
            _ = Retitle(chat.Id, text);
        }
        else
        {
            chat.Project ??= projectId;
            chat.Say(text, sent);
        }
        if (_phone.TryGetValue(projectId, out var last) && last != chat)
        {
            _phone.Remove(projectId);
            if (last != Agent) LetGo(last);
        }
        _phone[projectId] = chat;
        ListChats();
        return chat;
    }

    /// <summary>A chat by id: a live one, else a saved one (loaded, not yet running).</summary>
    public AgentViewModel? Find(string chatId) =>
        Live().FirstOrDefault(a => a.Id == chatId) ?? (AgentStore.List(Store).FirstOrDefault(r => r.Id == chatId) is { } r ? Load(r) : null);

    IEnumerable<string> Args()
    {
        var dirs = Projects.SelectMany(p => new[] { p.Boards is null ? null : Path.GetDirectoryName(Path.GetFullPath(p.Boards)), p.NotesFolder(), p.Docs }
                .Concat(p.Repos.Cast<string?>()))
            .OfType<string>().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs) { yield return "--add-dir"; yield return d; }
        // WOR-0138: the briefing, with the whole command list, is far past what a .cmd shim's command line takes
        // (8191 characters), so it goes in a file; claude reads it whole
        var file = Path.Combine(InstanceConfig.Folder(), "fettle-briefing.md");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Briefing());
        yield return "--append-system-prompt-file";
        yield return file;
    }

    string Briefing()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Fettle, the assistant inside Darg, Philip's app for his projects: a small round amber creature who lives in the corner of the window. He talks to you from a little box there, so keep replies short.");
        // WOR-0089: the box is small; full detail is what agents are for
        sb.AppendLine("Replies are the answer, or a line confirming what you did, and nothing more: one to three short sentences, a short list only when he asked for one. Never paste command output, file contents, diffs or long lists into a reply (he can open a step to see it); say what you found or changed in your own words. No headings, no recap of the steps you took, no offers of more help.");
        sb.AppendLine("You are for quick answers and instructions followed. When something needs the long version (a review, a write-up, an investigation, a change across many files or a lot of code), say so in a line and offer to start an agent on it (start-agent or work-on in the Darg API below), asking with AskUserQuestion; don't do the long job in this box.");
        sb.AppendLine(FettleRules.ChangeRequests); // WOR-0132
        sb.AppendLine(FettleRules.SayWhatYouDo); // WOR-0157
        sb.AppendLine("Your manner is dry and wry: understated British humour, the odd quip when one comes naturally (\"Done. Three cards, none of them on fire.\"), never at the expense of the answer or of brevity. No exclamation marks, no cheerleading, no apologising for being an AI. When something's wrong or risky, say so plainly and drop the jokes.");
        sb.AppendLine("Each of his messages starts with [Philip is looking at: ...]: the project, the section and what's open there. Read his words against it: \"these\", \"them\", \"here\", \"the docs\", \"this card\" mean what's in that context, and changes stay inside it (that project, that folder) unless he names something else. If his words could reach further than what he's looking at, ask with AskUserQuestion first.");
        sb.AppendLine(Decisions.Briefing); // WOR-0078: only questions reach him
        // WOR-0059: you can see every project, but a question is about the one it was asked from
        sb.AppendLine("Everything he asks is about the project in that line, the one he's asking from, unless he names another project or says all or every. \"What are the agents doing?\", \"what's on the board?\", \"any notes about X?\" mean that project's agents, board and notes: look only there, and pass that project to API calls that take one. You can see every project, but looking across them is for when he asks.");
        sb.AppendLine("The projects open in this Darg:");
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
            To make a card, call the Darg API's new-card command (one call: it picks the ID, writes the file and board.json, and has Haiku move the card to its story board); write card files by hand only if the API isn't there.
            Once a card is made, your reply says so and links it: new-card's answer has a link field, markdown like [WOR-0160](card:WOR-0160), to put in the reply as it is ("Made [WOR-0160](card:WOR-0160): GitHub page."); clicking it opens the card. Write any card you name in a reply the same way, [ID](card:ID), never in backticks; a card made by hand gets one too.
            Boards: each board is a folder with board.json (columns [{id,name}], prefix, nextId, order {list id: [card ids]}) and items/<ID> - <title>.md. A card file starts "# <ID>: <title>", then "- **Status:** ...", "- **Priority:** ...", "- **Type:** ...", "- **Labels:** a, b", "- **Created:** ...", "- **Done:** ...", then "## Description". Status is Backlog, Completed or a column's name. Only if new-card can't be used, to add a card by hand: the next ID is past nextId and every ID in items, bump nextId, add the ID to order.backlog, and name the file "<ID> - <title>.md" (slashes as spaces, no : ? * < > | ", at most 60 characters of title).
            Notes: one .md per note, with properties at the top between --- lines (column, colour, tags: [a, b], created, ticket) and the text below.
            Docs are Obsidian notes: moving one breaks [[links]] to it unless you update them too.
            Change these files directly; Darg shows changes within a second. Ask with AskUserQuestion before deleting anything or before changing more than 20 files.
            """);
        // commit and push rules are per folder: Darg's own repo is pushed freely, the live site's needs a yes
        var rules = Projects.SelectMany(Commits.Describe).Distinct().ToList();
        if (rules.Count > 0)
            sb.AppendLine("Commits and pushes, per folder (a folder's rule covers what's inside it; the deepest folder's wins): " + string.Join(" ", rules) +
                          " Anywhere else, never commit or push.");
        else sb.AppendLine("Never commit or push.");
        if (ArtifactHost.Server is { } server)
            sb.AppendLine($$"""
                Darg API: for anything that isn't a file edit (showing Philip a card, doc, section or agent, questions about the board (cards lists cards by column, label or text with the agent on each; card reads one whole, so don't grep card files for that), listing agents and reading their transcripts, starting an agent or putting one on a card, sending an agent a message, closing agents, running a flow or a saved command), use the API with curl. The environment variable DARG_API is the API's address with its token in it: run a command with POST $DARG_API/NAME and its arguments as a JSON object in the body (curl -s -X POST "$DARG_API/NAME" -d '{"project":"x"}'). Don't read the token file or fetch a list first; the commands are below, and only these exist. A 400 answer says what was wrong; correct it and try again.
                The commands, each as name: what it does and takes:
                {{server.Api.Help().TrimEnd()}}
                When he asks for a change while looking at an agent ("X should also do Y", "make it handle Z", "and fix the colours"), don't send it to that agent and don't make a card yet: first ask with AskUserQuestion whether it's a new ticket or an amendment to that agent's task, with options "New ticket" (a card on that project's board, saying what he asked) and "Amend the agent's task" (send his words to the agent with say). Never pick one yourself, even when one looks obvious. Skip the question only when his words already say which ("tell the agent...", "make a ticket for...").
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
