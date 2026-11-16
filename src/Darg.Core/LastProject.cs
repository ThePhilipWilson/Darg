namespace Darg.Core;

/// <summary>The project that was on screen last, so the app opens to it again. Kept in its own file
/// (<c>last-project</c> in the instance folder, holding the project's id) rather than instance.json,
/// which people and Fettle edit: switching tabs shouldn't rewrite their file under them.</summary>
public static class LastProject
{
    public static string FilePath(string? instanceFolder = null) => Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "last-project");

    /// <summary>The id saved last, or null when there is none or it can't be read.</summary>
    public static string? Read(string? instanceFolder = null)
    {
        try
        {
            var path = FilePath(instanceFolder);
            return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } id ? id : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void Write(string? projectId, string? instanceFolder = null)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return;
        try
        {
            var path = FilePath(instanceFolder);
            if (Read(instanceFolder) == projectId) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, projectId);
        }
        catch (IOException) { } // a missed save only means opening on another project next time
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The project among <paramref name="projects"/> whose id was saved last (ids compare
    /// ignoring case), else the first one; null when there are none.</summary>
    public static T? Pick<T>(IReadOnlyList<T> projects, Func<T, string> id, string? instanceFolder = null) where T : class =>
        Read(instanceFolder) is { } last && projects.FirstOrDefault(p => string.Equals(id(p), last, StringComparison.OrdinalIgnoreCase)) is { } found
            ? found : projects.FirstOrDefault();
}
