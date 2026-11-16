using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workbench.Core;

/// <summary>A line of an agent's transcript as saved: what kind it was and what it showed.</summary>
public sealed class ItemRecord
{
    /// <summary>user, text, note, tool</summary>
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Bad { get; set; }
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public string? ImagePath { get; set; }
    public string? Removed { get; set; }
    public string? Added { get; set; }
    public string? Preview { get; set; }
    public string? Result { get; set; }
    /// <summary>A tool step's preview images (files), shown again when the transcript comes back.</summary>
    public List<string>? Images { get; set; }
}

/// <summary>An agent as saved between runs of Workbench: enough to list it, show what it did, and
/// carry on (Claude Code keeps the conversation itself; --resume with the session id picks it up).</summary>
public sealed class AgentRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string? CardId { get; set; }
    /// <summary>For a Bench chat: the project it was started in (it lists only there). Null: from before chats had one.</summary>
    public string? Project { get; set; }
    public string? SessionId { get; set; }
    public List<string> Args { get; set; } = [];
    /// <summary>The model, effort and permission mode picked in the chat (null: the person's own settings);
    /// a resumed session starts with them.</summary>
    public string? Model { get; set; }
    public string? Effort { get; set; }
    public string? Mode { get; set; }
    /// <summary>Out of usage, and set to carry on at this time (when the limit resets); null: not waiting.</summary>
    public DateTimeOffset? ContinueAt { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public List<ItemRecord> Items { get; set; } = [];
}

/// <summary>Saved agents, one JSON file each, in the instance's folder: agents/&lt;project id&gt;/&lt;agent id&gt;.json.</summary>
public static class AgentStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Folder(string projectId, string? instanceFolder = null) =>
        Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "agents", projectId);

    public static void Save(string folder, AgentRecord r)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, r.Id + ".json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(r, Json));
        File.Move(tmp, path, overwrite: true); // never a half-written file
    }

    /// <summary>The saved agents, newest first; a file that won't read is skipped, not fatal.</summary>
    public static List<AgentRecord> List(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var list = new List<AgentRecord>();
        foreach (var f in Directory.GetFiles(folder, "*.json"))
            try { if (JsonSerializer.Deserialize<AgentRecord>(File.ReadAllText(f), Json) is { Id.Length: > 0 } r) list.Add(r); }
            catch (Exception e) when (e is JsonException or IOException) { }
        return list.OrderByDescending(r => r.Updated).ToList();
    }

    public static void Delete(string folder, string id)
    {
        var path = Path.Combine(folder, id + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
