namespace Workbench.Core;

/// <summary>Where a link clicked in a doc goes.</summary>
public abstract record DocLink
{
    /// <summary>A doc in the docs folder (its path relative to the folder, with forward slashes).</summary>
    public sealed record Doc(string Path) : DocLink;
    /// <summary>Something for the system to open: a web or mail link, or a file that isn't a doc (an image, a PDF).</summary>
    public sealed record External(string Target) : DocLink;
    /// <summary>Nowhere: the reason, to show.</summary>
    public sealed record Missing(string Reason) : DocLink;
}

/// <summary>Resolves the links in a docs folder's markdown the way Obsidian and GitHub do: [[Name]],
/// [[folder/Name]], [[Name#heading|text]]; [text](Other.md), (sub/Doc.md#part), (../Up.md), (My%20Notes.md),
/// (&lt;My Notes.md&gt;), (/from/the/root.md), a doc linked without .md; web, mailto and file links; a link to
/// a heading in the same doc stays put.</summary>
public static class DocLinks
{
    /// <param name="link">The span's target: "wiki:Name" for an Obsidian link, else the markdown link's URL.</param>
    /// <param name="current">The open doc, relative to the folder (forward slashes), or null.</param>
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
        var rel = Normalise(baseDir.Length == 0 ? path.TrimStart('/') : baseDir + "/" + path);
        if (rel is null) return Local(Path.GetFullPath(Path.Combine(folder, baseDir, path)), docs, folder) ?? new DocLink.Missing($"{path} is outside the docs folder.");
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
            return new DocLink.Missing($"No doc called {name} in this folder.");
        }

        // "Name" or "Name.md" (and "v1.2", whose dot isn't an extension)
        var named = docs.Where(d => Strip(Path.GetFileName(d)).Equals(Strip(name), StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count > 0) return new DocLink.Doc(Nearest(named, dir));

        // ![[image.png]] style: a file that isn't markdown, anywhere in the folder
        if (Path.HasExtension(name) && Directory.Exists(folder))
        {
            try
            {
                var file = Directory.EnumerateFiles(folder, Path.GetFileName(name), SearchOption.AllDirectories).FirstOrDefault();
                if (file is not null) return new DocLink.External(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new DocLink.Missing($"No doc called {name} in this folder.");
    }

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

    /// <summary>An absolute path: a doc if it's inside the folder, else the file itself.</summary>
    static DocLink? Local(string full, IReadOnlyList<string> docs, string folder)
    {
        var rel = Path.GetRelativePath(folder, full).Replace('\\', '/');
        if (!rel.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(rel) && Find(rel, docs, folder) is { } hit) return hit;
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
