using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workbench.Core;

/// <summary>One install's settings: which projects it includes. Each instance has its own folder and
/// sees nothing of any other (the work PC's instance knows nothing of home's).</summary>
public sealed class InstanceConfig
{
    public string Name { get; set; } = "home";
    public List<ProjectConfig> Projects { get; set; } = [];

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The instance's folder: WORKBENCH_HOME, else the user's app data folder / Workbench / the
    /// instance name (WORKBENCH_INSTANCE, default "home").</summary>
    public static string Folder()
    {
        var home = Environment.GetEnvironmentVariable("WORKBENCH_HOME");
        if (!string.IsNullOrWhiteSpace(home)) return home;
        var name = Environment.GetEnvironmentVariable("WORKBENCH_INSTANCE");
        if (string.IsNullOrWhiteSpace(name)) name = "home";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Workbench", name);
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
    public List<string> Repos { get; set; } = [];
    /// <summary>Features this project uses; an empty list means all of them.</summary>
    public List<string> Features { get; set; } = [];

    public bool Uses(string feature) => Features.Count == 0 || Features.Contains(feature, StringComparer.OrdinalIgnoreCase);

    /// <summary>Where agents and terminals start: the first repo, else the boards folder, else home.</summary>
    public string WorkingFolder() =>
        Repos.FirstOrDefault(Directory.Exists) ?? (Boards is not null && Directory.Exists(Boards) ? Boards
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}

public static class Features
{
    public const string Board = "board", Agents = "agents", Terminal = "terminal";
}
