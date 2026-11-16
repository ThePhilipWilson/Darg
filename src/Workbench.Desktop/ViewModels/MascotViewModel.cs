using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>Bench, the mascot: Claude sessions for the whole instance. It can see every project's
/// boards, notes, docs and repos (--add-dir) and is told how the board and note files are laid out.
/// Every message carries a line saying what Philip is looking at (project, section, open doc or card),
/// so "sort these into folders" means the docs he has open, not every project's. Chats are kept: New
/// chat starts another, and the list switches back to an old one (resuming its session).</summary>
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
    bool _listing;

    static string Store => AgentStore.Folder("~bench");

    /// <summary>The chat list from disk, and the newest chat open (from an earlier run, to carry on).
    /// Chats with no real title yet (older ones were "Bench" or a cut-off first message) get one.</summary>
    public void Restore()
    {
        ListChats();
        if (Chats.FirstOrDefault() is { } newest) SelectedChat = newest;
        foreach (var r in AgentStore.List(Store).Where(r => NeedsTitle(r.Name)))
            if (r.Items.FirstOrDefault(i => i.Kind == "user")?.Text is { } first) _ = Retitle(r.Id, first);
    }

    static bool NeedsTitle(string name) => name is "Bench" or "" || name.EndsWith('…');

    /// <summary>Give a chat a short title (Haiku, else a guess from the words) and save it.</summary>
    async Task Retitle(string id, string firstMessage)
    {
        var title = await ChatTitle.Make(firstMessage);
        if (Agent?.Id == id) { Agent.Name = title; Agent.Save(); }
        else if (AgentStore.List(Store).FirstOrDefault(r => r.Id == id) is { } r) { r.Name = title; AgentStore.Save(Store, r); }
        ListChats();
    }

    void ListChats()
    {
        _listing = true;
        var keep = Agent?.Id;
        Chats.Clear();
        foreach (var r in AgentStore.List(Store)) Chats.Add(new ChatEntry(r.Id, r.Name, r.Updated));
        SelectedChat = Chats.FirstOrDefault(c => c.Id == keep);
        _listing = false;
    }

    partial void OnSelectedChatChanged(ChatEntry? value)
    {
        if (_listing || value is null || value.Id == Agent?.Id) return;
        if (AgentStore.List(Store).FirstOrDefault(r => r.Id == value.Id) is not { } r) return;
        Agent?.Save();
        Agent?.End();
        Agent = Wire(new AgentViewModel(r, Store));
    }

    AgentViewModel Wire(AgentViewModel a)
    {
        a.Notify += (what, detail) => Notify?.Invoke(a, what, detail);
        a.Wrap = WithContext; // typed in the Agents view too, it still hears where Philip is
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
            Agent = Wire(new AgentViewModel(home, sent, name: ChatTitle.Guess(text), args: Args(), shown: text) { StoreFolder = Store });
            Agent.Save();
            ListChats();
            _ = Retitle(Agent.Id, text);
            return;
        }
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
        sb.AppendLine("You are Bench, the assistant inside Workbench, Philip's app for his projects. He talks to you from a small box in the corner, so keep replies short.");
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
        return sb.ToString();
    }
}

public sealed record ChatEntry(string Id, string Name, DateTime Updated)
{
    public string When => Updated.Date == DateTime.Today ? Updated.ToString("HH:mm") : Updated.ToString("d MMM");
    public override string ToString() => $"{Name} · {When}";
}
