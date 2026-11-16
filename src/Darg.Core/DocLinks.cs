namespace Darg.Core;

/// <summary>Where a link clicked in a doc goes.</summary>
public abstract record DocLink
{
    /// <summary>A doc in the docs folder (its path relative to the folder, with forward slashes).</summary>
    public sealed record Doc(string Path) : DocLink;
    /// <summary>A markdown doc outside the docs folder (elsewhere in the vault, another project's docs, a
    /// repo's README): its full path, to open in Darg rather than hand to another app.</summary>
    public sealed record Elsewhere(string FullPath) : DocLink;
    /// <summary>Something for the system to open: a web or mail link, or a file that isn't a doc (an image, a PDF).</summary>
    public sealed record External(string Target) : DocLink;
    /// <summary>Nowhere: the reason, to show.</summary>
    public sealed record Missing(string Reason) : DocLink;
}

/// <summary>Resolves the links in a docs folder's markdown the way Obsidian and GitHub do: [[Name]],
/// [[folder/Name]], [[Name#heading|text]]; [text](Other.md), (sub/Doc.md#part), (../Up.md), (My%20Notes.md),
/// (&lt;My Notes.md&gt;), (/from/the/root.md), a doc linked without .md; web, mailto and file links; a link to
/// a heading in the same doc stays put. A link may leave the folder, into another repo or the rest of the
/// vault: (../../Other/README.md), (D:/repo/Doc.md), or a [[Name]] found elsewhere in the Obsidian vault the
/// folder sits in, as Obsidian would find it. A markdown doc out there is a <see cref="DocLink.Elsewhere"/>.</summary>
public static class DocLinks
{
    /// <param name="link">The span's target: "wiki:Name" for an Obsidian link, else the markdown link's URL.</param>
    /// <param name="current">The open doc, relative to the folder (forward slashes), or its full path when it
    /// is outside the folder, or null.</param>
    /// <param name="docs">Every doc in the folder, relative, forward slashes.</param>
    /// <param name="folder">The docs folder, to find files that aren't docs.</param>
    public static DocLink Resolve(string link, string? current, IReadOnlyList<string> docs, string folder)
    {
        link = link.Trim();
        if (link.Length == 0) return new DocLink.Missing("The link is empty.");
        if (link.StartsWith("wiki:", StringComparison.Ordinal)) return Wiki(link[5..], current, docs, folder);

        if (link.StartsWith('<') && link.EndsWith('>')) link = link[1..^1].Trim();
        else
        {
            // [text](url "title"): the title isn't part of the target
            var title = link.IndexOf(" \"", StringComparison.Ordinal);
            if (title > 0 && link.EndsWith('"')) link = link[..title].Trim();
        }

        var scheme = Scheme(link);
        if (scheme is "http" or "https" or "mailto" or "obsidian") return new DocLink.External(link);
        if (scheme == "file")
        {
            try { return Local(new Uri(link).LocalPath, docs, folder) ?? new DocLink.Missing($"No file at {new Uri(link).LocalPath}."); }
            catch (UriFormatException) { return new DocLink.Missing($"Can't read the link {link}."); }
        }
        if (scheme is not null && !IsDrive(link)) return new DocLink.External(link);

        var path = link.Split('#')[0];
        if (path.Length == 0) return new DocLink.Missing("That link is to a heading in this doc.");
        try { path = Uri.UnescapeDataString(path); } catch (UriFormatException) { }
        path = path.Replace('\\', '/');

        if (Path.IsPathRooted(path) && IsDrive(path))
            return Local(path, docs, folder) ?? new DocLink.Missing($"No file at {path}.");

        var baseDir = path.StartsWith('/') ? "" : Dir(current);
        if (IsFull(current) && baseDir.Length > 0)
        {
            // the open doc is outside the folder: relative to where it is
            var full = Path.GetFullPath(Path.Combine(baseDir, path));
            return Local(full, docs, folder) ?? new DocLink.Missing($"No file at {full}.");
        }
        var rel = Normalise(baseDir.Length == 0 ? path.TrimStart('/') : baseDir + "/" + path);
        if (rel is null)
        {
            var full = Path.GetFullPath(Path.Combine(folder, baseDir, path));
            return Local(full, docs, folder) ?? new DocLink.Missing($"No file at {full}.");
        }
        return Find(rel, docs, folder) ?? new DocLink.Missing($"No doc at {rel}.");
    }

    /// <summary>[[Name]]: a doc with that name anywhere in the folder (nearest the current doc first), or
    /// that path from the folder or the current doc.</summary>
    static DocLink Wiki(string target, string? current, IReadOnlyList<string> docs, string folder)
    {
        var name = target.Split('#')[0].Split('^')[0].Trim().Replace('\\', '/');
        if (name.Length == 0) return new DocLink.Missing("That link is to a heading in this doc.");
        var dir = Dir(current);

        // a path: from the current doc's folder, then from the root
        if (name.Contains('/'))
        {
            foreach (var from in new[] { dir, "" })
                if (Normalise(from.Length == 0 ? name : from + "/" + name) is { } rel && Find(rel, docs, folder) is { } hit) return hit;
            // Obsidian also matches a partial path at the end of one
            var tail = "/" + Strip(name);
            var ends = docs.Where(d => ("/" + Strip(d)).EndsWith(tail, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ends.Count > 0) return new DocLink.Doc(Nearest(ends, dir));
            return InVault(name, current, folder) ?? new DocLink.Missing($"No doc called {name} in this folder or its vault.");
        }

        // "Name" or "Name.md" (and "v1.2", whose dot isn't an extension)
        var named = docs.Where(d => Strip(Path.GetFileName(d)).Equals(Strip(name), StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count > 0) return new DocLink.Doc(Nearest(named, dir));

        // ![[image.png]] style: a file that isn't markdown, anywhere in the folder
        if (Path.HasExtension(name) && !IsMarkdown(name) && Directory.Exists(folder))
        {
            try
            {
                var file = Directory.EnumerateFiles(folder, Path.GetFileName(name), SearchOption.AllDirectories).FirstOrDefault();
                if (file is not null) return new DocLink.External(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return InVault(name, current, folder) ?? new DocLink.Missing($"No doc called {name} in this folder or its vault.");
    }

    /// <summary>A [[Name]] that isn't in the docs folder: searched for in the vault the folder is in (the nearest
    /// folder at or above it holding .obsidian), as Obsidian would, the one nearest the open doc first. A markdown
    /// doc is an Elsewhere; another file (an image) opens in its own app. Folders starting with a dot are skipped.</summary>
    static DocLink? InVault(string name, string? current, string folder)
    {
        if (Vault(folder) is not { } vault) return null;
        var file = Path.GetFileName(name);
        var md = IsMarkdown(file) || !LooksLikeFileType(Path.GetExtension(file)); // "v1.2" is a doc called v1.2
        var pattern = md && !IsMarkdown(file) ? file + ".md" : file;
        List<string> hits;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
            hits = Directory.EnumerateFiles(vault, pattern, options)
                .Select(f => Path.GetRelativePath(vault, f).Replace('\\', '/'))
                .Where(f => !f.Split('/').Any(part => part.StartsWith('.')))
                .Where(f => !name.Contains('/') || ("/" + Strip(f)).EndsWith("/" + Strip(name.TrimStart('/')), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        if (hits.Count == 0) return null;
        var open = current is null ? folder : Path.GetDirectoryName(Path.GetFullPath(Path.Combine(folder, current))) ?? folder;
        var here = Path.GetRelativePath(vault, open).Replace('\\', '/');
        var at = here == "." ? [] : here.Split('/');
        int Shared(string hit) => Dir(hit).Split('/').Zip(at).TakeWhile(p => p.First.Equals(p.Second, StringComparison.OrdinalIgnoreCase)).Count();
        var best = hits.OrderByDescending(Shared).ThenBy(h => h.Count(c => c == '/')).ThenBy(h => h, StringComparer.OrdinalIgnoreCase).First();
        var hit = Path.GetFullPath(Path.Combine(vault, best));
        return md ? new DocLink.Elsewhere(hit) : new DocLink.External(hit);
    }

    /// <summary>The Obsidian vault a folder is in: the nearest folder at or above it holding .obsidian, or null.</summary>
    public static string? Vault(string folder)
    {
        try
        {
            for (var d = new DirectoryInfo(Path.GetFullPath(folder)); d is not null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, ".obsidian"))) return d.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }

    static bool IsMarkdown(string name) => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>".png" or ".pdf" is a file type; the ".2" of "v1.2" isn't.</summary>
    static bool LooksLikeFileType(string ext) => ext.Length is >= 2 and <= 6 && ext.Skip(1).All(char.IsAsciiLetterOrDigit) && ext.Skip(1).Any(char.IsAsciiLetter);

    /// <summary>A path from the root of a drive (D:/x, //server/x, or /x on Mac and Linux), as an outside doc's is.</summary>
    static bool IsFull(string? doc) => doc is not null && (IsDrive(doc) || doc.StartsWith("//", StringComparison.Ordinal) || (Path.IsPathRooted(doc) && !OperatingSystem.IsWindows()));

    /// <summary>A path relative to the folder: the doc itself, the doc with .md added, or a file that isn't a doc.</summary>
    static DocLink? Find(string rel, IReadOnlyList<string> docs, string folder)
    {
        var hit = docs.FirstOrDefault(d => d.Equals(rel, StringComparison.OrdinalIgnoreCase))
                  ?? docs.FirstOrDefault(d => d.Equals(rel + ".md", StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return new DocLink.Doc(hit);
        var full = Path.Combine(folder, rel);
        if (File.Exists(full)) return new DocLink.External(Path.GetFullPath(full));
        if (Directory.Exists(full)) return new DocLink.External(Path.GetFullPath(full));
        return null;
    }

    /// <summary>An absolute path: a doc if it's inside the folder, a markdown doc elsewhere (linked with or
    /// without its .md), else the file itself.</summary>
    static DocLink? Local(string full, IReadOnlyList<string> docs, string folder)
    {
        full = Path.GetFullPath(full);
        var rel = Path.GetRelativePath(folder, full).Replace('\\', '/');
        if (rel != ".." && !rel.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(rel) && Find(rel, docs, folder) is { } hit) return hit;
        if (IsMarkdown(full) && File.Exists(full)) return new DocLink.Elsewhere(full);
        if (!File.Exists(full) && !Directory.Exists(full) && File.Exists(full + ".md")) return new DocLink.Elsewhere(full + ".md");
        return File.Exists(full) || Directory.Exists(full) ? new DocLink.External(full) : null;
    }

    /// <summary>The doc nearest the current one: in its folder, then the shallowest.</summary>
    static string Nearest(List<string> hits, string dir) =>
        hits.OrderByDescending(h => Dir(h).Equals(dir, StringComparison.OrdinalIgnoreCase))
            .ThenBy(h => h.Count(c => c == '/')).ThenBy(h => h, StringComparer.OrdinalIgnoreCase).First();

    static string Strip(string name) => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;

    static string Dir(string? doc)
    {
        if (string.IsNullOrEmpty(doc)) return "";
        var slash = doc.LastIndexOf('/');
        return slash < 0 ? "" : doc[..slash];
    }

    /// <summary>Folds "." and ".." out of a relative path; null if it climbs out of the folder.</summary>
    static string? Normalise(string path)
    {
        var parts = new List<string>();
        foreach (var p in path.Split('/'))
        {
            if (p.Length == 0 || p == ".") continue;
            if (p == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(p);
        }
        return string.Join('/', parts);
    }

    static string? Scheme(string link)
    {
        var colon = link.IndexOf(':');
        if (colon < 1) return null;
        var s = link[..colon];
        return s.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.') && char.IsAsciiLetter(s[0]) ? s.ToLowerInvariant() : null;
    }

    /// <summary>C:\ or C:/ (a Windows drive, not a URL scheme).</summary>
    static bool IsDrive(string s) => s.Length >= 3 && char.IsAsciiLetter(s[0]) && s[1] == ':' && s[2] is '/' or '\\';
}
