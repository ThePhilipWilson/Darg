using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Workbench.Core;

/// <summary>A project made from a folder someone already has: what's there decides the settings, and
/// each finding is said in a line so Setup can show what was worked out.</summary>
public static class ProjectDetect
{
    static readonly string[] Palette = ["#e0a84a", "#6aa5e8", "#a784e0", "#5fbf7f", "#e06464", "#e07ab8", "#4fc1c1", "#c9c9c9"];

    /// <param name="root">The project's root folder.</param>
    /// <param name="others">The instance's other projects (their docs folders show where the vault is).</param>
    /// <param name="ghAccounts">The gh accounts logged in on this machine.</param>
    /// <param name="dataFolder">Where Workbench keeps a new project's board and notes if the folder has none.</param>
    public static (ProjectConfig Project, List<string> Found) FromFolder(string root, IReadOnlyList<ProjectConfig> others,
        IReadOnlyList<string> ghAccounts, string dataFolder)
    {
        root = Path.GetFullPath(root).TrimEnd('\\', '/');
        var found = new List<string>();
        var name = Pretty(Path.GetFileName(root));
        var id = Slug(name);
        var taken = others.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 2; taken.Contains(id); n++) id = $"{Slug(name)}-{n}";
        var p = new ProjectConfig { Id = id, Name = name, Color = Palette[others.Count % Palette.Length] };

        // repos: the folder itself, and git repos one level down (a studio folder holding several)
        if (Repo.IsGit(root)) p.Repos.Add(Fwd(root));
        foreach (var d in SafeDirs(root))
            if (Repo.IsGit(d)) p.Repos.Add(Fwd(d));
        found.Add(p.Repos.Count switch
        {
            0 => "No git repo here; the code view and GitHub page need one.",
            1 => $"Repo: {p.Repos[0]}",
            _ => $"{p.Repos.Count} repos: {string.Join(", ", p.Repos.Select(Path.GetFileName))}",
        });

        // boards: a folder of Tatty-style boards (subfolders with board.json) within two levels
        var boards = FindBoards(root);
        if (boards is not null) { p.Boards = Fwd(boards); found.Add($"Boards found: {p.Boards}"); }
        else
        {
            var tasks = Path.Combine(dataFolder, "projects", id, "tasks");
            NewBoard(Path.Combine(tasks, "Main"), Prefix(name));
            p.Boards = Fwd(tasks);
            found.Add($"No boards in the folder: made an empty one (Main, {Prefix(name)}-) in Workbench's own data, not in your repo.");
        }

        // docs: the vault's Code/<name> beside the other projects' docs, else a docs folder in the project
        var vaultCode = others.Select(o => o.Docs).OfType<string>().Select(d => Path.GetDirectoryName(Path.GetFullPath(d)))
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(Directory.Exists);
        var inVault = vaultCode is null ? null : SafeDirs(vaultCode).FirstOrDefault(d =>
            Path.GetFileName(d).Equals(Path.GetFileName(root), StringComparison.OrdinalIgnoreCase) ||
            Slug(Path.GetFileName(d)) == Slug(name));
        var local = new[] { "docs", "Docs", "doc", "documentation" }.Select(d => Path.Combine(root, d)).FirstOrDefault(Directory.Exists);
        if (inVault is not null) { p.Docs = Fwd(inVault); found.Add($"Docs in the vault: {p.Docs}"); }
        else if (local is not null) { p.Docs = Fwd(local); found.Add($"Docs: {p.Docs}"); }
        else found.Add("No docs folder found; set one if the project has docs.");

        // GitHub account: the repo's owner, when gh is logged in as them
        foreach (var r in p.Repos)
            if (GitHub.RepoOf(r) is { } gh && ghAccounts.FirstOrDefault(a => a.Equals(gh.Owner, StringComparison.OrdinalIgnoreCase)) is { } acct)
            { p.GitHub = acct; found.Add($"GitHub as {acct}"); break; }

        if (p.Repos.Count > 0) p.Commands.Add(new SavedCommand { Name = "Git", Command = "git status" });
        return (p, found);
    }

    static string? FindBoards(string root)
    {
        bool HasBoards(string d) => SafeDirs(d).Any(b => File.Exists(Path.Combine(b, "board.json")));
        if (HasBoards(root)) return root;
        foreach (var d in SafeDirs(root))
        {
            if (Path.GetFileName(d).StartsWith('.') && Path.GetFileName(d) != ".workbench") continue;
            if (HasBoards(d)) return d;
            foreach (var dd in SafeDirs(d)) if (HasBoards(dd)) return dd;
        }
        return null;
    }

    /// <summary>An empty board in Tatty's format.</summary>
    public static void NewBoard(string folder, string prefix)
    {
        if (File.Exists(Path.Combine(folder, "board.json"))) return;
        Directory.CreateDirectory(Path.Combine(folder, "items"));
        var json = new JsonObject
        {
            ["columns"] = new JsonArray(
                new JsonObject { ["id"] = "c1", ["name"] = "To Do" },
                new JsonObject { ["id"] = "c2", ["name"] = "In Progress" },
                new JsonObject { ["id"] = "c3", ["name"] = "Done" }),
            ["prefix"] = prefix, ["version"] = 1, ["nextId"] = 1, ["order"] = new JsonObject { ["backlog"] = new JsonArray() },
        };
        File.WriteAllText(Path.Combine(folder, "board.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    static IEnumerable<string> SafeDirs(string d)
    {
        try { return Directory.GetDirectories(d).Where(x => !Path.GetFileName(x).StartsWith('.') || Path.GetFileName(x) == ".workbench").ToList(); }
        catch (Exception) { return []; }
    }

    static string Fwd(string p) => p.Replace('\\', '/');

    /// <summary>"project-s" or "my_game" as "Project C", "My Game"; a name with capitals stays as it is.</summary>
    public static string Pretty(string folder)
    {
        if (folder.Any(char.IsUpper) && !folder.Contains('_') && !folder.Contains('-')) return folder;
        var words = Regex.Split(folder, @"[-_ ]+").Where(w => w.Length > 0);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public static string Slug(string name)
    {
        var s = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return s.Length > 0 ? s : "project";
    }

    /// <summary>A card prefix from a name: its capitals (Project C → PS), else its first three letters.</summary>
    public static string Prefix(string name)
    {
        var caps = new string(name.Where(char.IsUpper).ToArray());
        if (caps.Length >= 2) return caps.Length > 4 ? caps[..4] : caps;
        var letters = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return letters.Length >= 3 ? letters[..3] : letters.Length > 0 ? letters : "T";
    }
}
