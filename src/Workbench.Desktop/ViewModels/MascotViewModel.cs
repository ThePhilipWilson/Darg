using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>Bench, the mascot: one Claude session for the whole instance. It can see every project's
/// boards, notes and repos (--add-dir) and is told how the board and note files are laid out, so "tag
/// every deity card" is a plain file edit, which the open board picks up by itself. The session starts
/// with the first message and lives until the app closes.</summary>
public partial class MascotViewModel(IReadOnlyList<ProjectConfig> projects) : ObservableObject
{
    static readonly ObservableCollection<TranscriptItem> Nothing = [];
    /// <summary>The instance's projects (updated when Setup saves; a session already running keeps what it was given).</summary>
    public IReadOnlyList<ProjectConfig> Projects { get; set; } = projects;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Items), nameof(HasChat))] public partial AgentViewModel? Agent { get; set; }
    [ObservableProperty] public partial bool Open { get; set; }
    [ObservableProperty] public partial string Compose { get; set; } = "";
    public ObservableCollection<TranscriptItem> Items => Agent?.Items ?? Nothing;
    public bool HasChat => Agent is not null;
    public event Action<AgentViewModel, string, string>? Notify;

    [RelayCommand] void Toggle() => Open = !Open;

    [RelayCommand]
    void Send()
    {
        var text = Compose.Trim();
        if (text.Length == 0) return;
        Compose = "";
        if (Agent is null || Agent.State == AgentState.Exited)
        {
            var home = InstanceConfig.Folder();
            Directory.CreateDirectory(home);
            var a = new AgentViewModel(home, text, name: "Bench", args: Args());
            a.Notify += (what, detail) => Notify?.Invoke(a, what, detail);
            Agent = a;
            return;
        }
        Agent.Compose = text;
        Agent.SendCommand.Execute(null);
    }

    IEnumerable<string> Args()
    {
        var dirs = Projects.SelectMany(p => new[] { p.Boards is null ? null : Path.GetDirectoryName(Path.GetFullPath(p.Boards)), p.NotesFolder() }
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
        sb.AppendLine("The projects open in this Workbench:");
        foreach (var p in Projects)
        {
            sb.Append($"- {p.Name} (id {p.Id})");
            if (p.Boards is not null) sb.Append($"; boards in {Path.GetFullPath(p.Boards)}");
            if (p.NotesFolder() is { } n) sb.Append($"; notes in {n}");
            if (p.Repos.Count > 0) sb.Append($"; repos: {string.Join(", ", p.Repos)}");
            sb.AppendLine();
        }
        sb.AppendLine("""
            Boards: each board is a folder with board.json (columns [{id,name}], prefix, nextId, order {list id: [card ids]}) and items/<ID> - <title>.md. A card file starts "# <ID>: <title>", then "- **Status:** ...", "- **Priority:** ...", "- **Type:** ...", "- **Labels:** a, b", "- **Created:** ...", "- **Done:** ...", then "## Description". Status is Backlog, Completed or a column's name. To add a card: the next ID is past nextId and every ID in items, bump nextId, add the ID to order.backlog, and name the file "<ID> - <title>.md" (slashes as spaces, no : ? * < > | ", at most 60 characters of title).
            Notes: one .md per note, with properties at the top between --- lines (column, colour, tags: [a, b], created, ticket) and the text below.
            Change these files directly; Workbench shows changes within a second. Ask with AskUserQuestion before deleting anything or before changing more than 20 cards. Never commit or push.
            """);
        return sb.ToString();
    }
}
