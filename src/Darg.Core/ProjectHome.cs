namespace Darg.Core;

/// <summary>What a project's Home page is made from (WOR-0054): its README, found where people keep one, and
/// short words for when things happened.</summary>
public static class ProjectHome
{
    static readonly string[] Names = ["README.md", "Readme.md", "readme.md", "README.markdown", "README.txt", "README"];

    /// <summary>The project's README: the first repo's (then each other repo's, in order), else one at the top
    /// of its docs folder, else its docs folder's index.md. Null when it has none.</summary>
    public static string? Readme(ProjectConfig project)
    {
        foreach (var repo in project.ExistingRepos())
            if (In(repo) is { } r) return r;
        if (project.Docs is { Length: > 0 } docs && Directory.Exists(docs))
            return In(docs) ?? (File.Exists(Path.Combine(docs, "index.md")) ? Path.Combine(docs, "index.md") : null);
        return null;
    }

    /// <summary>The README in a folder, matched without regard to case (README.md, readme.md, Readme.markdown…).</summary>
    public static string? In(string folder)
    {
        try
        {
            var files = Directory.GetFiles(folder);
            foreach (var name in Names)
                if (files.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", "4 days ago", then the date.</summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var d = now - when;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min ago";
        if (d < TimeSpan.FromHours(24) && when.LocalDateTime.Date == now.LocalDateTime.Date) return $"{(int)d.TotalHours} h ago";
        var days = (now.LocalDateTime.Date - when.LocalDateTime.Date).Days;
        if (days <= 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        return when.LocalDateTime.ToString(when.Year == now.Year ? "d MMM" : "d MMM yyyy");
    }
}
