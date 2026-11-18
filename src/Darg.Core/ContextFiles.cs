using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>One thing in the Context section (WOR-0145): a file Claude reads, or text Darg adds, with the group it is listed under.
/// <see cref="Path"/> is empty for text made up on the spot; <see cref="Text"/> is set for that only.</summary>
public sealed record ContextItem(string Group, string Name, string Path, string? Text = null);

/// <summary>What a project's agents start with, found so the person can read it in one place: the CLAUDE.md files (user's, then
/// each folder down to the repo), the project's Claude memory, and what Darg itself adds. Read-only.</summary>
public static class ContextFiles
{
    public const string InstructionsGroup = "Instructions";
    public const string MemoryGroup = "Memory";
    public const string DargGroup = "Added by Darg";

    /// <summary>Where Claude Code keeps a folder's memory: ~/.claude/projects/&lt;the folder with every character that isn't a letter or digit made a dash&gt;/memory.</summary>
    public static string MemoryFolder(string folder, string? userHome = null)
    {
        var home = userHome ?? UserHome.Folder;
        var key = Regex.Replace(Path.GetFullPath(folder).TrimEnd('\\', '/'), "[^A-Za-z0-9]", "-");
        return Path.Combine(home, ".claude", "projects", key, "memory");
    }

    public static List<ContextItem> List(ProjectConfig project, string? userHome = null, string? instanceFolder = null)
    {
        var items = new List<ContextItem>();
        var folder = project.WorkingFolder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in DefaultContext.ClaudeFiles(folder, userHome))
            if (seen.Add(f)) items.Add(new ContextItem(InstructionsGroup, Label(f, userHome), f));
        // the project's other repos carry their own CLAUDE.md, read when an agent works there
        foreach (var repo in project.ExistingRepos().Where(r => !string.Equals(Path.GetFullPath(r), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)))
            foreach (var f in DefaultContext.ClaudeFiles(repo, userHome))
                if (seen.Add(f)) items.Add(new ContextItem(InstructionsGroup, Label(f, userHome), f));

        var memory = MemoryFolder(folder, userHome);
        if (Directory.Exists(memory))
            foreach (var f in Directory.EnumerateFiles(memory, "*.md").OrderBy(f => !Path.GetFileName(f).Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
                items.Add(new ContextItem(MemoryGroup, Path.GetFileName(f), f));

        items.Add(new ContextItem(DargGroup, "Default context", "", DefaultContext.Build(project, folder, userHome, instanceFolder)));
        return items;
    }

    static string Label(string file, string? userHome)
    {
        var claude = Path.Combine(userHome ?? UserHome.Folder, ".claude");
        if (file.StartsWith(claude, StringComparison.OrdinalIgnoreCase)) return "User: " + Path.GetFileName(file);
        var dir = Path.GetDirectoryName(file)!;
        if (Path.GetFileName(dir) == ".claude") dir = Path.GetDirectoryName(dir)!;
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        return (name.Length > 0 ? name : dir) + ": " + Path.GetFileName(file);
    }

    /// <summary>The text of an item: its file read afresh, or the text it carries.</summary>
    public static string Read(ContextItem item)
    {
        if (item.Text is not null) return item.Text;
        try { return File.ReadAllText(item.Path); }
        catch (Exception e) { return $"Can't read {item.Path}: {e.Message}"; }
    }
}
