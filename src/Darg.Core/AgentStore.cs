using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darg.Core;

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

/// <summary>What an agent was started for (WOR-0110), so a running one can be recognised: a card in QA shows Run QA
/// unless a QA agent is at work on it, whatever other agents the card has.</summary>
public static class AgentPurposes
{
    public const string Chat = "chat", Work = "work", Qa = "qa", Improve = "improve", Rework = "rework", Flow = "flow";
}

/// <summary>An agent as saved between runs of Darg: enough to list it, show what it did, and
/// carry on (Claude Code keeps the conversation itself; --resume with the session id picks it up).</summary>
public sealed class AgentRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string? CardId { get; set; }
    /// <summary>What it was started for (<see cref="AgentPurposes"/>); null: saved before agents had one.</summary>
    public string? Purpose { get; set; }
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

    /// <summary>Save now (an older save of it still waiting in the background is dropped).</summary>
    public static void Save(string folder, AgentRecord r)
    {
        var path = Path.Combine(folder, r.Id + ".json");
        Forget(path);
        Write(path, r);
    }

    static void Write(string path, AgentRecord r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(r, Json));
        File.Move(tmp, path, overwrite: true); // never a half-written file
    }

    /// <summary>The saved agents, newest first; a file that won't read is skipped, not fatal. Saves still
    /// waiting to be written are written first, so the list has them.</summary>
    public static List<AgentRecord> List(string folder)
    {
        Flush();
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
        Forget(path); // a save still waiting would write it back
        if (File.Exists(path)) File.Delete(path);
    }

    // ---- Saving in the background (WOR-0076). A long transcript takes a few hundred ms to write, and agents save
    // after every turn: on the UI thread that was a freeze each time. The record is made where the agent lives
    // (a snapshot), and written here; only the newest record of each agent waiting is written.

    static readonly object Gate = new();
    static readonly Dictionary<string, AgentRecord> Waiting = [];
    static Task _writer = Task.CompletedTask;
    static bool _writing;
    static string? _inHand;

    /// <summary>Save in the background: returns at once. <see cref="Flush"/> waits for every save to be written.</summary>
    public static void SaveLater(string folder, AgentRecord r)
    {
        var path = Path.Combine(folder, r.Id + ".json");
        lock (Gate)
        {
            Waiting[path] = r;
            if (_writing) return;
            _writing = true;
            _writer = Task.Run(WriteWaiting);
        }
    }

    static void WriteWaiting()
    {
        while (true)
        {
            AgentRecord r;
            string path;
            lock (Gate)
            {
                if (Waiting.Count == 0) { _writing = false; _inHand = null; return; }
                (path, r) = Waiting.First();
                Waiting.Remove(path);
                _inHand = path;
            }
            try { Write(path, r); }
            catch (Exception) { /* a failed save must never take the agent down */ }
        }
    }

    /// <summary>Write every save still waiting, and wait for the one being written (on closing the app).</summary>
    public static void Flush(TimeSpan? timeout = null)
    {
        Task writer;
        lock (Gate) writer = _writer;
        try { writer.Wait(timeout ?? TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
    }

    /// <summary>Drop a waiting save of this file, and wait for it if it's being written now.</summary>
    static void Forget(string path)
    {
        Task writer;
        lock (Gate)
        {
            Waiting.Remove(path);
            if (_inHand != path) return;
            writer = _writer;
        }
        try { writer.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
    }
}
