using System.Text;

namespace Darg.Core;

/// <summary>What a new Claude agent starts with by default, written out so the person can read it: the arguments
/// Darg adds to every agent, and the CLAUDE.md files Claude Code loads for the agent's folder.</summary>
public static class DefaultContext
{
    /// <summary>The folder and file names are shown as they would be for a real agent, with a placeholder for its id.</summary>
    const string AgentPlaceholder = "<agent id>";

    public static string Build(ProjectConfig project, string folder, string? userHome = null, string? instanceFolder = null)
    {
        var artifactsDir = ArtifactStore.Folder(project.Id.Length > 0 ? project.Id : "default", instanceFolder)
            .Replace('\\', '/') + "/" + AgentPlaceholder;
        var sb = new StringBuilder();
        sb.AppendLine($"# Default Claude context: {project.Name}");
        sb.AppendLine();
        sb.AppendLine($"Every new agent in this project starts in `{folder}`, with these added by Darg:");
        sb.AppendLine();
        sb.AppendLine("## Command-line arguments");
        sb.AppendLine();
        sb.AppendLine($"- `--add-dir {artifactsDir}`: the agent's own artifacts folder, so it can write pages there.");
        foreach (var r in project.OtherRepos(folder))
            sb.AppendLine($"- `--add-dir {r}`: the project's {ProjectConfig.RepoName(r)} repo, so it can work there too.");
        var mistakes = MistakeLog.PathFor(project.Id, instanceFolder).Replace('\\', '/');
        sb.AppendLine($"- `--add-dir {Path.GetDirectoryName(mistakes)!.Replace('\\', '/')}`: the project's mistake log folder, so it can append to the log.");
        sb.AppendLine("- `--append-system-prompt`, with this text:");
        sb.AppendLine();
        sb.AppendLine("```text");
        sb.AppendLine(ArtifactStore.Briefing(artifactsDir) + " " + MistakeLog.Briefing(mistakes));
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## CLAUDE.md files Claude Code loads");
        sb.AppendLine();
        sb.AppendLine("Read from disk when a session starts, the user's first, then from the root down to the working folder.");
        sb.AppendLine();
        var files = ClaudeFiles(folder, userHome);
        if (files.Count == 0) sb.AppendLine("None found.");
        foreach (var file in files)
        {
            var lines = File.ReadLines(file).Count();
            sb.AppendLine($"- `{file}` ({lines} lines)");
        }
        return sb.ToString();
    }

    /// <summary>The CLAUDE.md and CLAUDE.local.md files (and the .claude/CLAUDE.md variants) that exist for a folder,
    /// in the order Claude Code reads them: the user's file, then each ancestor from the root down to the folder.</summary>
    public static List<string> ClaudeFiles(string folder, string? userHome = null)
    {
        var found = new List<string>();
        var home = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var user = Path.Combine(home, ".claude", "CLAUDE.md");
        if (File.Exists(user)) found.Add(user);

        var chain = new List<string>();
        for (var dir = Path.GetFullPath(folder); dir is not null; dir = Path.GetDirectoryName(dir))
            chain.Insert(0, dir); // root first
        foreach (var dir in chain)
        {
            foreach (var candidate in new[] { Path.Combine(dir, "CLAUDE.md"), Path.Combine(dir, "CLAUDE.local.md"), Path.Combine(dir, ".claude", "CLAUDE.md") })
            {
                if (File.Exists(candidate) && !found.Contains(candidate, StringComparer.OrdinalIgnoreCase)) found.Add(candidate);
            }
        }
        return found;
    }

    /// <summary>Writes the report to default-context.md in the instance folder and returns its path.</summary>
    public static string Write(ProjectConfig project, string folder, string? instanceFolder = null)
    {
        var dir = instanceFolder ?? InstanceConfig.Folder();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "default-context.md");
        File.WriteAllText(path, Build(project, folder, instanceFolder: instanceFolder), new UTF8Encoding(false));
        return path;
    }
}
