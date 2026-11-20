using System.Text;

namespace Darg.Core;

/// <summary>Where a project's working files live (WOR-0144): reports, mistakes, artifacts, its own flows, and the
/// board and notes Darg made, all in a <c>.darg</c> folder at the top of the project's first repo that is on this
/// machine, ignored by git through <c>.git/info/exclude</c> (a project opts in to committing it by deleting that line).
/// A project with no repo here keeps the folder in the instance's data, <c>&lt;instance&gt;/projects/&lt;id&gt;</c>.
/// Agents work in worktrees but this folder is in the main checkout only, so they are always given absolute paths.</summary>
public static class ProjectData
{
    public const string FolderName = ".darg";

    static readonly object Gate = new();
    static (string File, DateTime Stamp, InstanceConfig Config)? _cached;
    static readonly HashSet<string> Ensured = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The project's folder for working files: <c>&lt;first repo&gt;/.darg</c>, else the old data folder.
    /// A repo's .darg is made, and put in .git/info/exclude, the first time it's asked for.</summary>
    public static string Root(string projectId, string? instanceFolder = null)
    {
        var instance = instanceFolder ?? InstanceConfig.Folder();
        if (RepoOf(projectId, instance) is { } repo) return Ensure(repo);
        return LegacyRoot(projectId, instance);
    }

    /// <summary>The old place: <c>&lt;instance&gt;/projects/&lt;id&gt;</c>.</summary>
    public static string LegacyRoot(string projectId, string? instanceFolder = null) =>
        Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "projects", Safe(projectId));

    public static string Safe(string id) => string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>The project's first repo that exists here, or null (also for a project the instance doesn't list).</summary>
    public static string? RepoOf(string projectId, string instanceFolder)
    {
        var config = Config(instanceFolder);
        var p = config?.Projects.FirstOrDefault(x => string.Equals(x.Id, projectId, StringComparison.OrdinalIgnoreCase));
        return p?.Repos.FirstOrDefault(Directory.Exists) is { } r ? Path.GetFullPath(r).TrimEnd('\\', '/') : null;
    }

    /// <summary>The instance's config, read again only when instance.json changes (paths are asked for constantly).</summary>
    static InstanceConfig? Config(string instanceFolder)
    {
        var file = InstanceConfig.FilePath(instanceFolder);
        try
        {
            if (!File.Exists(file)) return null;
            var stamp = File.GetLastWriteTimeUtc(file);
            lock (Gate)
            {
                if (_cached is { } c && c.File == file && c.Stamp == stamp) return c.Config;
                var config = InstanceConfig.Load(instanceFolder);
                _cached = (file, stamp, config);
                return config;
            }
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return null; }
    }

    static string Ensure(string repo)
    {
        var folder = Path.Combine(repo, FolderName);
        lock (Gate)
        {
            if (Ensured.Contains(folder)) return folder;
            // only when .darg is new: a project that opted in (deleted the exclude line, committed .darg) keeps it so
            var isNew = !Directory.Exists(folder);
            try
            {
                Directory.CreateDirectory(folder);
                if (isNew) Exclude(repo);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Ensured.Add(folder);
        }
        return folder;
    }

    /// <summary>Adds <c>.darg/</c> to the repo's .git/info/exclude unless a line for it is already there. Works for a
    /// worktree or submodule too (a .git file pointing at the real folder). Returns whether it wrote one.</summary>
    public static bool Exclude(string repo)
    {
        var git = GitDir(repo);
        if (git is null) return false;
        var file = Path.Combine(git, "info", "exclude");
        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        if (text.Split('\n').Any(l => l.Trim() is ".darg" or ".darg/" or "/.darg" or "/.darg/")) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.AppendAllText(file, (text.Length > 0 && !text.EndsWith('\n') ? "\n" : "") + $"{FolderName}/\n");
        return true;
    }

    /// <summary>The folder git keeps its info in: the repo's .git, or what a .git file points to.</summary>
    static string? GitDir(string repo)
    {
        var dotGit = Path.Combine(repo, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (!File.Exists(dotGit)) return null;
        var line = File.ReadLines(dotGit).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;
        var dir = line["gitdir:".Length..].Trim();
        dir = Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(repo, dir));
        // a linked worktree's own folder holds a commondir pointer: info/exclude is shared, in the main .git
        var common = Path.Combine(dir, "commondir");
        if (File.Exists(common))
            dir = Path.GetFullPath(Path.Combine(dir, File.ReadAllText(common).Trim()));
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>Moves what's in each project's old data folder into its repo's .darg, once: run at start, before anything
    /// opens a path (a live agent would hold the old ones). A board or notes folder that was in the old folder is
    /// repointed in the config; one set elsewhere (the vault's Tatty) stays. Returns what moved, in a line, or "".</summary>
    public static string Migrate(string? instanceFolder = null)
    {
        var instance = instanceFolder ?? InstanceConfig.Folder();
        InstanceConfig config;
        try { config = InstanceConfig.Load(instance); } catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return ""; }
        var moved = new List<string>();
        var changed = false;
        foreach (var p in config.Projects)
        {
            if (p.Repos.FirstOrDefault(Directory.Exists) is not { } repo) continue;
            var old = LegacyRoot(p.Id, instance);
            if (!Directory.Exists(old)) continue;
            var target = Ensure(Path.GetFullPath(repo).TrimEnd('\\', '/'));
            try
            {
                var n = MoveInto(old, target);
                // repointed whether or not anything went this time: a board left by an earlier start may have just gone
                changed |= Repoint(p, old, target);
                if (n > 0)
                {
                    moved.Add($"{p.Name} ({n})");
                    Rewrite(instance, p.Id, old, target);
                }
                TryDelete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        if (changed) config.Save(instance);
        return moved.Count == 0 ? "" : $"Moved projects' working files into .darg in their repos: {string.Join(", ", moved)}.";
    }

    /// <summary>Every entry of <paramref name="from"/> into <paramref name="to"/>, merging a folder that's in both and
    /// keeping the file already there; returns how many entries went. The instance's data is usually on C: and repos
    /// on another drive, where <see cref="Directory.Move"/> refuses (WOR-0152: nothing moved, so reports, artifacts and
    /// notes vanished from view): a folder then goes file by file. An entry that can't move (a file held open) stays
    /// behind for the next start, and the rest still go.</summary>
    static int MoveInto(string from, string to)
    {
        var n = 0;
        Directory.CreateDirectory(to);
        foreach (var entry in Directory.GetFileSystemEntries(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(entry));
            try
            {
                if (Directory.Exists(entry))
                {
                    if (!Directory.Exists(dest) && SameVolume(entry, dest))
                        try { Directory.Move(entry, dest); n++; continue; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // held open, or a mount point: go file by file
                    if (MoveInto(entry, dest) > 0) n++;
                    TryDelete(entry);
                }
                else if (!File.Exists(dest)) { File.Move(entry, dest); n++; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return n;
    }

    static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    static void TryDelete(string dir)
    {
        try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Boards and notes the config put in the old folder now point into .darg; anywhere else is left.</summary>
    static bool Repoint(ProjectConfig p, string old, string target)
    {
        var any = false;
        string? Swap(string? path)
        {
            if (path is null) return null;
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            var prefix = Path.GetFullPath(old).TrimEnd('\\', '/');
            if (!full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return path;
            var moved = target + full[prefix.Length..];
            // not all of it could move (a file held open): keep pointing where it is until the next start takes the rest
            if (Directory.Exists(full) || !Directory.Exists(moved)) return path;
            any = true;
            return moved.Replace('\\', '/');
        }
        p.Boards = Swap(p.Boards);
        p.Notes = Swap(p.Notes);
        return any;
    }

    /// <summary>Saved agents carry the old folders in their saved arguments (--add-dir, the report path): point them at the new ones.</summary>
    static void Rewrite(string instance, string projectId, string old, string target)
    {
        var agents = AgentStore.Folder(projectId, instance);
        if (!Directory.Exists(agents)) return;
        string[] Forms(string p) => [p, p.Replace('\\', '/'), p.Replace("\\", "\\\\")];
        var from = Forms(Path.GetFullPath(old).TrimEnd('\\', '/'));
        var to = Forms(Path.GetFullPath(target).TrimEnd('\\', '/'));
        foreach (var file in Directory.GetFiles(agents, "*.json"))
        {
            try
            {
                var text = File.ReadAllText(file);
                var next = text;
                for (var i = 0; i < from.Length; i++) next = next.Replace(from[i], to[i], StringComparison.OrdinalIgnoreCase);
                if (next != text) File.WriteAllText(file, next, new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
