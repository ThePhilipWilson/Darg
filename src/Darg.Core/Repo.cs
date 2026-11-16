using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>A repo's state as git reports it: branch, how far it is from its upstream, and changed files
/// (repo-relative path, forward slashes, to the two-letter porcelain code).</summary>
public sealed record RepoStatus(string Branch, int Ahead, int Behind, IReadOnlyDictionary<string, string> Changed)
{
    public static readonly RepoStatus None = new("", 0, 0, new Dictionary<string, string>());
}

/// <summary>A commit as the Home page lists it: short hash, subject line, author, when.</summary>
public sealed record Commit(string Hash, string Subject, string Author, DateTimeOffset When);

/// <summary>Reading a repo for the code view: its files (git's own list, so ignored folders stay out)
/// and its status. Read-only: nothing here changes a repo.</summary>
public static class Repo
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", "Library", "Temp", "Logs", ".vs", ".idea", "__pycache__" };

    public static bool IsGit(string root) => Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"));

    /// <summary>Repo-relative paths with forward slashes: tracked files plus untracked ones git doesn't
    /// ignore. Outside git, a walk that leaves out the usual build and cache folders.</summary>
    public static List<string> Files(string root, int max = 200_000)
    {
        if (IsGit(root))
        {
            var (code, output) = Git(root, "ls-files", "--cached", "--others", "--exclude-standard", "-z");
            if (code == 0)
                return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct().Take(max).ToList();
        }
        var list = new List<string>();
        var stack = new Stack<string>([root]);
        while (stack.Count > 0 && list.Count < max)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                    if (!Skip.Contains(Path.GetFileName(d))) stack.Push(d);
                list.AddRange(Directory.GetFiles(dir).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')));
            }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    public static RepoStatus Status(string root)
    {
        if (!IsGit(root)) return RepoStatus.None;
        var (code, output) = Git(root, "status", "--porcelain=v1", "-b", "-z", "--untracked-files=all");
        return code == 0 ? ParseStatus(output) : RepoStatus.None;
    }

    /// <summary>`git status --porcelain=v1 -b -z` output, parsed.</summary>
    public static RepoStatus ParseStatus(string output)
    {
        var entries = output.Split('\0');
        string branch = ""; int ahead = 0, behind = 0;
        var changed = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.Length < 3) continue;
            if (e.StartsWith("## "))
            {
                var head = e[3..];
                var dots = head.IndexOf("...", StringComparison.Ordinal);
                var bracket = head.IndexOf(" [", StringComparison.Ordinal);
                branch = dots >= 0 ? head[..dots] : bracket >= 0 ? head[..bracket] : head;
                if (branch.StartsWith("No commits yet on ")) branch = branch["No commits yet on ".Length..];
                if (bracket >= 0)
                    foreach (var part in head[(bracket + 2)..].TrimEnd(']').Split(", "))
                    {
                        if (part.StartsWith("ahead ")) int.TryParse(part[6..], out ahead);
                        if (part.StartsWith("behind ")) int.TryParse(part[7..], out behind);
                    }
                continue;
            }
            var xy = e[..2];
            changed[e[3..]] = xy;
            if (xy[0] is 'R' or 'C') i++; // a rename's old path follows; skip it
        }
        return new RepoStatus(branch, ahead, behind, changed);
    }

    /// <summary>The last commits on the checked-out branch, newest first; none outside git or in a repo without any.</summary>
    public static List<Commit> Log(string root, int count = 5)
    {
        if (!IsGit(root)) return [];
        var (code, output) = Git(root, "log", $"-n{count}", "--format=%h%x1f%s%x1f%an%x1f%cI%x1e");
        return code == 0 ? ParseLog(output) : [];
    }

    /// <summary>`git log --format=%h%x1f%s%x1f%an%x1f%cI%x1e` output, parsed.</summary>
    public static List<Commit> ParseLog(string output)
    {
        var list = new List<Commit>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = record.Split('\x1f');
            if (f.Length < 4 || !DateTimeOffset.TryParse(f[3], out var when)) continue;
            list.Add(new Commit(f[0], f[1], f[2], when));
        }
        return list;
    }

    /// <summary>A file's text for viewing, or null with a reason when it's binary or too big.</summary>
    public static (string? Text, string Why) Read(string path, long maxBytes = 2_000_000)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return (null, "The file isn't there any more.");
        if (info.Length > maxBytes) return (null, $"{info.Length / 1024:N0} KB is too big to show here.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0)) return (null, "A binary file.");
        return (Encoding.UTF8.GetString(bytes).TrimStart('﻿'), "");
    }

    static (int Code, string Output) Git(string root, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.quotepath=off");
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(30_000)) { p.Kill(); return (-1, ""); }
            return (p.ExitCode, output.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, ""); } // no git on PATH
    }
}
