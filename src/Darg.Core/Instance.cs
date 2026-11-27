using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darg.Core;

/// <summary>One install's settings: which projects it includes. Each instance has its own folder and
/// sees nothing of any other (the work PC's instance knows nothing of home's).</summary>
public sealed class InstanceConfig
{
    public string Name { get; set; } = "home";
    public List<ProjectConfig> Projects { get; set; } = [];
    /// <summary>This machine's local tools (Forge, ComfyUI, Ollama, Pinokio…); the usual four when not set.</summary>
    public List<ToolConfig>? Tools { get; set; }
    /// <summary>The shell a new console opens (a name from <see cref="Shells"/>, e.g. "Git Bash"); the
    /// platform's usual one when not set or not installed here. The last one picked from the console menu.</summary>
    public string? Shell { get; set; }
    /// <summary>Phone notifications through ntfy (AND-0006): off when not set, so the work instance leaves them out.</summary>
    public PushConfig? Push { get; set; }
    /// <summary>Phone access (AND-0002, <see cref="RemoteServer"/>): false leaves it out of this instance altogether (a
    /// work machine), so nothing listens and no phone can pair. Not set means allowed: it still listens only once a phone is paired.</summary>
    public bool? Remote { get; set; }
    public List<ToolConfig> ToolsOrDefaults() => Tools is { Count: > 0 } t ? t : ToolConfig.Defaults();

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        PropertyNameCaseInsensitive = true, // a hand-edited "github" or "GitHub" reads the same
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The instance's folder: DARG_HOME, else the user's app data folder / Darg / the
    /// instance name (DARG_INSTANCE, default "home").</summary>
    public static string Folder()
    {
        var home = Env("DARG_HOME");
        if (home is not null) return home;
        var name = Env("DARG_INSTANCE") ?? "home";
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        MigrateFromWorkbench(appData);
        var folder = Path.Combine(appData, "Darg", name);
        // the move can fail while an old copy still has the folder open: carry on from where the data is
        var old = Path.Combine(appData, "Workbench", name);
        return !Directory.Exists(folder) && Directory.Exists(old) ? old : folder;
    }

    /// <summary>A setting by its Darg name, else its old Workbench name (the app was renamed, WOR-0055).</summary>
    static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) v = Environment.GetEnvironmentVariable("WORKBENCH_" + name["DARG_".Length..]);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>The app was called Workbench: its data folder moves to Darg the first time, and a link at
    /// the old name points at the new one so paths saved in agents (artifact folders) still resolve. If the
    /// move fails (an old copy has files open) nothing changes and the old folder is used until it can.
    /// When Darg's folder is there already (the dictation model is downloaded into Darg/models, and was,
    /// before any instance had moved: WOR-0196), each instance folder moves in on its own, linked the same way.</summary>
    public static void MigrateFromWorkbench(string appData)
    {
        var old = Path.Combine(appData, "Workbench");
        var now = Path.Combine(appData, "Darg");
        if (!Directory.Exists(old) || IsLink(old)) return;
        if (!Directory.Exists(now))
        {
            if (Move(old, now)) Link(old, now);
            return;
        }
        string[] inside;
        try { inside = Directory.GetDirectories(old); } catch (Exception) { return; }
        foreach (var dir in inside)
        {
            var to = Path.Combine(now, Path.GetFileName(dir));
            if (IsLink(dir) || Directory.Exists(to) || File.Exists(to)) continue; // moved already, or Darg has its own
            if (Move(dir, to)) Link(dir, to);
        }
    }

    static bool IsLink(string dir)
    {
        try { return new DirectoryInfo(dir).LinkTarget is not null; } catch (Exception) { return true; } // unreadable: leave it be
    }

    static bool Move(string from, string to)
    {
        try { Directory.Move(from, to); return true; } catch (Exception) { return false; }
    }

    static void Link(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); } // a junction would need cmd; a symlink needs developer mode
        catch (Exception) { TryJunction(link, target); }
    }

    static void TryJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            p?.WaitForExit(5000);
        }
        catch (Exception) { }
    }

    public static string FilePath(string? folder = null) => Path.Combine(folder ?? Folder(), "instance.json");

    /// <summary>The instance's settings, or an empty instance (written out, so there is a file to edit)
    /// when there are none yet.</summary>
    public static InstanceConfig Load(string? folder = null)
    {
        var path = FilePath(folder);
        if (!File.Exists(path))
        {
            var fresh = new InstanceConfig();
            fresh.Save(folder);
            return fresh;
        }
        return JsonSerializer.Deserialize<InstanceConfig>(File.ReadAllText(path), Json) ?? new InstanceConfig();
    }

    public void Save(string? folder = null)
    {
        var path = FilePath(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}

/// <summary>A project: where its boards and repos are, and which features it uses.</summary>
public sealed class ProjectConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Tab colour, as #rrggbb.</summary>
    public string Color { get; set; } = "#e0a84a";
    /// <summary>The folder holding the project's boards, one subfolder per board.</summary>
    public string? Boards { get; set; }
    /// <summary>The notes wall's folder; by default "notes" beside the boards folder.</summary>
    public string? Notes { get; set; }
    public List<string> Repos { get; set; } = [];
    /// <summary>The project's docs folder (in the Obsidian vault, usually).</summary>
    public string? Docs { get; set; }
    /// <summary>The gh account this project's GitHub calls sign in as (null: gh's active account).</summary>
    public string? GitHub { get; set; }
    /// <summary>Commands the project runs often (dev servers, tests, a stack's "up"): one click opens a console running one.</summary>
    public List<SavedCommand> Commands { get; set; } = [];
    /// <summary>The dev server the Preview section shows; when not set, one is looked for in the repos.</summary>
    public PreviewConfig? Preview { get; set; }
    /// <summary>Commit and push rules per folder (a repo, or the vault the boards are in): folder to
    /// "push", "commit", "ask" or "never" (<see cref="Core.Commits"/>). A folder with no rule is never committed
    /// by Fettle; a card agent there follows the repo's own CLAUDE.md.</summary>
    public Dictionary<string, string>? Commits { get; set; }
    /// <summary>The MCP servers its agents start (WOR-0208, <see cref="McpServers"/>): those ticked on its Context page, with
    /// their definitions kept, and those on by default it unticked. Not set: only the ones on by default.</summary>
    public McpChoice? Mcp { get; set; }
    /// <summary>The most agents of this project at work at once (WOR-0245, <see cref="AgentCap"/>): one more waits, pending,
    /// and starts by itself once one stops. Null or 0: no cap.</summary>
    public int? MaxAgents { get; set; }

    public string? NotesFolder() => Notes ?? (Boards is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Boards))!, "notes"));
    /// <summary>Features this project uses; an empty list means all of them.</summary>
    public List<string> Features { get; set; } = [];

    public bool Uses(string feature) => Features.Count == 0 || Features.Contains(feature, StringComparer.OrdinalIgnoreCase);

    /// <summary>Plugin folders (HOM-0001, <see cref="PluginStore"/>): each a section on the rail, its page and command owned by
    /// whichever repo holds the folder. Null when it has none, so other projects' entries stay as they were.</summary>
    public List<string>? Plugins { get; set; }

    /// <summary>Where agents and terminals start: the first repo, else the boards folder, else home.</summary>
    public string WorkingFolder() =>
        Repos.FirstOrDefault(Directory.Exists) ?? (Boards is not null && Directory.Exists(Boards) ? Boards
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>The repos that are on this machine, in the order set.</summary>
    public List<string> ExistingRepos() => Repos.Where(Directory.Exists).ToList();

    /// <summary>A repo's short name: its folder's name ("Site", "Workspace").</summary>
    public static string RepoName(string repo) => Path.GetFileName(repo.TrimEnd('\\', '/'));

    /// <summary>The repo with this folder name, or this full path; null when there isn't one here.</summary>
    public string? RepoNamed(string name)
    {
        name = name.Trim().Trim('`', '"', '\'').TrimEnd('\\', '/');
        if (name.Length == 0) return null;
        return ExistingRepos().FirstOrDefault(r => string.Equals(RepoName(r), name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(r).TrimEnd('\\', '/'), Path.IsPathRooted(name) ? Path.GetFullPath(name).TrimEnd('\\', '/') : null,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The repo a card names, for its agent to start in: a label "repo:Workspace" (or "repo/Workspace",
    /// "repo = Workspace"), or a "Repo: Workspace" line in its description ("- **Repo:** Workspace" too).
    /// Null when it names none, or one this project doesn't have.</summary>
    public string? RepoFor(IEnumerable<string> labels, string description)
    {
        foreach (var l in labels)
            if (System.Text.RegularExpressions.Regex.Match(l.Trim(), @"^repo\s*[:/=]\s*(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } m
                && RepoNamed(m.Groups[1].Value) is { } r) return r;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(description,
                     @"^[\s>*-]*\**repo\**\s*:\s*\**\s*(.+?)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline))
            if (RepoNamed(m.Groups[1].Value.Trim('*', ' ')) is { } r) return r;
        return null;
    }

    /// <summary>The project's repos other than the one an agent starts in: each given to it with --add-dir, so
    /// an agent in Site can read and edit Workspace too.</summary>
    public List<string> OtherRepos(string cwd)
    {
        static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
        var here = Norm(cwd);
        return ExistingRepos().Where(r => !string.Equals(Norm(r), here, StringComparison.OrdinalIgnoreCase)
            && !here.StartsWith(Norm(r) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

/// <summary>A command a project runs often. Cwd is relative to the project's first repo unless rooted.</summary>
public sealed class SavedCommand
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string? Cwd { get; set; }

    /// <summary>"Name: command" per line (Setup's text box), and back.</summary>
    public static List<SavedCommand> Parse(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => { var i = l.IndexOf(": ", StringComparison.Ordinal); return i > 0 ? new SavedCommand { Name = l[..i].Trim(), Command = l[(i + 2)..].Trim() } : new SavedCommand { Name = l, Command = l }; })
            .Where(c => c.Command.Length > 0).ToList();

    public static string Render(IEnumerable<SavedCommand> commands) =>
        string.Join(Environment.NewLine, commands.Select(c => c.Name == c.Command ? c.Command : $"{c.Name}: {c.Command}"));
}

public static class Features
{
    public const string Board = "board", Notes = "notes", Docs = "docs", Agents = "agents", Code = "code", GitHub = "github", Preview = "preview", Terminal = "terminal";
    /// <summary>Every feature, in the order the rail shows them (the Setup view's tick boxes).</summary>
    public static readonly string[] All = [Board, Notes, Docs, Agents, Code, GitHub, Preview, Terminal];
}
