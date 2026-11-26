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
    public const string Chat = "chat", Work = "work", Qa = "qa", Improve = "improve", Rework = "rework", Flow = "flow",
        /// <summary>A planner (WOR-0176): drafts a plan in docs/design/ and stays beside its Design page.</summary>
        Plan = "plan";
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
    /// <summary>For a planner (WOR-0176): the plan file it drafted, so the Design page finds its chat again.</summary>
    public string? Plan { get; set; }
    public string? SessionId { get; set; }
    public List<string> Args { get; set; } = [];
    /// <summary>The model, effort and permission mode picked in the chat (null: the person's own settings);
    /// a resumed session starts with them.</summary>
    public string? Model { get; set; }
    public string? Effort { get; set; }
    public string? Mode { get; set; }
    /// <summary>The model / effort above was picked by hand, so Optimise tokens leaves it alone (WOR-0159).</summary>
    public bool ModelByHand { get; set; }
    public bool EffortByHand { get; set; }
    /// <summary>Out of usage, and set to carry on at this time (when the limit resets); null: not waiting.</summary>
    public DateTimeOffset? ContinueAt { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public List<ItemRecord> Items { get; set; } = [];

    /// <summary>A copy with lists of its own (the items themselves are shared: nothing changes one once made).</summary>
    internal AgentRecord Clone()
    {
        var c = (AgentRecord)MemberwiseClone();
        c.Args = [.. Args];
        c.Items = [.. Items];
        return c;
    }
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

    /// <summary>Save now. An older save of it still waiting in the background is dropped; one being written as this
    /// is called would land after this and undo it, so then this save waits its turn behind it instead (WOR-0232:
    /// waiting for the writer here held the UI thread up to 10 s).</summary>
    public static void Save(string folder, AgentRecord r)
    {
        var path = Path.Combine(folder, r.Id + ".json");
        lock (Gate)
        {
            Waiting.Remove(path);
            Deleted.Remove(path);
            if (_inHand == path) { Queue(path, r); return; }
        }
        Write(path, r);
    }

    static void Write(string path, AgentRecord r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(r, Json));
        File.Move(tmp, path, overwrite: true); // never a half-written file
    }

    /// <summary>The saved agents, newest first; a file that won't read is skipped, not fatal. Saves still waiting
    /// to be written (and the one being written) are listed as they will be, without waiting for the writer, which
    /// stays busy as long as agents keep saving (WOR-0232: waiting here froze the window for up to 10 s).</summary>
    public static List<AgentRecord> List(string folder)
    {
        // what's in the background first: a save written while the files are read is then in one or the other
        Dictionary<string, AgentRecord> pending = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> deleted = new(StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            foreach (var (path, r) in Waiting) if (In(folder, path)) pending[Path.GetFullPath(path)] = r;
            if (_inHand is { } p && _inHandRecord is { } h && In(folder, p) && !Deleted.Contains(p)) pending.TryAdd(Path.GetFullPath(p), h);
            foreach (var d in Deleted) if (In(folder, d)) deleted.Add(Path.GetFullPath(d));
        }
        var list = pending.Values.Select(Copy).ToList();
        if (!Directory.Exists(folder)) return list.OrderByDescending(r => r.Updated).ToList();
        foreach (var f in Directory.GetFiles(folder, "*.json"))
        {
            var full = Path.GetFullPath(f);
            if (pending.ContainsKey(full) || deleted.Contains(full)) continue; // newer in hand, or going
            try { if (JsonSerializer.Deserialize<AgentRecord>(File.ReadAllText(f), Json) is { Id.Length: > 0 } r) list.Add(r); }
            catch (Exception e) when (e is JsonException or IOException) { }
        }
        return list.OrderByDescending(r => r.Updated).ToList();
    }

    /// <summary>Remove a saved agent: a save of it waiting is dropped, and one being written now is deleted once it
    /// lands, so it can't bring the agent back. Never waits for the writer (WOR-0232).</summary>
    public static void Delete(string folder, string id)
    {
        var path = Path.Combine(folder, id + ".json");
        lock (Gate)
        {
            Waiting.Remove(path);
            if (_inHand == path) Deleted.Add(path); // the writer deletes it once written
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    static bool In(string folder, string path) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);

    /// <summary>A record handed out by <see cref="List"/> that is still waiting to be written: a copy, so a caller
    /// changing it (a new title) doesn't change what the writer is writing.</summary>
    static AgentRecord Copy(AgentRecord r) => r.Clone();

    // ---- Saving in the background (WOR-0076). A long transcript takes a few hundred ms to write, and agents save
    // after every turn: on the UI thread that was a freeze each time. The record is made where the agent lives
    // (a snapshot), and written here; only the newest record of each agent waiting is written.

    static readonly object Gate = new();
    static readonly Dictionary<string, AgentRecord> Waiting = [];
    /// <summary>Deleted while being written: the writer deletes the file again once its write lands.</summary>
    static readonly HashSet<string> Deleted = new(StringComparer.OrdinalIgnoreCase);
    static Task _writer = Task.CompletedTask;
    static bool _writing;
    static string? _inHand;
    static AgentRecord? _inHandRecord;
    /// <summary>For tests: called on the writer's thread with each file's path just before it's written.</summary>
    internal static Action<string>? BeforeWrite;

    /// <summary>Save in the background: returns at once. <see cref="Flush"/> waits for every save to be written.</summary>
    public static void SaveLater(string folder, AgentRecord r)
    {
        var path = Path.Combine(folder, r.Id + ".json");
        lock (Gate)
        {
            Deleted.Remove(path); // saved again after a delete: this save stands
            Queue(path, r);
        }
    }

    /// <summary>Under <see cref="Gate"/>: put a record in the queue and make sure the writer is running.</summary>
    static void Queue(string path, AgentRecord r)
    {
        Waiting[path] = r;
        if (_writing) return;
        _writing = true;
        _writer = Task.Run(WriteWaiting);
    }

    static void WriteWaiting()
    {
        while (true)
        {
            AgentRecord r;
            string path;
            lock (Gate)
            {
                if (Waiting.Count == 0) { _writing = false; _inHand = null; _inHandRecord = null; return; }
                (path, r) = Waiting.First();
                Waiting.Remove(path);
                _inHand = path;
                _inHandRecord = r;
            }
            BeforeWrite?.Invoke(path);
            try { Write(path, r); }
            catch (Exception) { /* a failed save must never take the agent down */ }
            lock (Gate)
            {
                if (Deleted.Remove(path))
                    try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
                _inHand = null;
                _inHandRecord = null;
            }
        }
    }

    /// <summary>Write every save still waiting, and wait for the one being written. Only on closing the app (and in
    /// tests): while agents keep saving the writer doesn't finish, so this waits out its whole timeout.</summary>
    public static void Flush(TimeSpan? timeout = null)
    {
        Task writer;
        lock (Gate) writer = _writer;
        try { writer.Wait(timeout ?? TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
    }
}
