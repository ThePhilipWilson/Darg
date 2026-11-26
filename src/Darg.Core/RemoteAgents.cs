using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>One call to the desktop's Darg API from somewhere else (the phone, AND-0005): a command and its arguments
/// in, its JSON answer out. A refused call throws, with a message fit to show as it is. The phone's client
/// (<c>DargClient.Call</c>) is one; a test passes the app's own <see cref="DargApi"/>.</summary>
public delegate Task<JsonNode?> ApiCall(string command, object args);

/// <summary>An agent as the API's <c>agents</c> command lists it, for a list on a small screen: what it is, what state
/// it's in and what it's doing now.</summary>
public sealed record RemoteAgent(string Project, string ProjectId, string Id, string Name, string Status, string Doing, bool Open, string? Card, string? Flow)
{
    public bool NeedsYou => Status == "needsyou";
    public bool Working => Status is "working" or "starting";

    /// <summary>The state in a word or two, as the desktop list says it.</summary>
    public string StateText => Status switch
    {
        "needsyou" => "Needs you",
        "working" => "Working",
        "starting" => "Starting",
        "idle" => "Done",
        "exited" => "Ended",
        "saved" => "Saved",
        _ => Status,
    };

    /// <summary>Needs you first, then at work, then the rest: the order the desktop's list keeps.</summary>
    public int Rank => Status switch { "needsyou" => 0, "working" or "starting" => 1, "idle" => 2, "exited" => 3, _ => 4 };

    public static RemoteAgent From(JsonObject o) => new(
        Str(o["project"]) ?? "", Str(o["projectId"]) ?? "", Str(o["id"]) ?? "", Str(o["name"]) ?? "", Str(o["state"]) ?? "",
        Str(o["doing"]) ?? "", Bool(o["open"]), Str(o["card"]), Str(o["flow"]));

    /// <summary>The <c>agents</c> answer as a list, in <see cref="Rank"/> order (ties keep the desktop's order).</summary>
    public static IReadOnlyList<RemoteAgent> List(JsonNode? answer) =>
        answer is JsonArray a ? a.OfType<JsonObject>().Select(From).Select((x, i) => (x, i)).OrderBy(t => t.x.Rank).ThenBy(t => t.i).Select(t => t.x).ToList() : [];

    internal static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
    internal static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

/// <summary>One entry of an agent's transcript as the API's <c>agent</c> command gives it: index, kind (user, agent, note,
/// tool, permission, question) and full text, a step's tool, result and diff, an ask's state.</summary>
public sealed record TranscriptEntry(int Index, string Kind, string Text, string? Tool = null, string? Result = null, bool Done = true,
    bool Error = false, bool Pending = false, string? Removed = null, string? Added = null, string? Preview = null)
{
    /// <summary>Lines of an agent's reply shown before "show more" on a phone (AND-0005): long output readable on a small screen
    /// means a screenful per entry, the rest a tap away.</summary>
    public const int FoldLines = 12;

    /// <summary>A step still running, an ask still waiting or a reply still being written (WOR-0234): its entry will change,
    /// so a refresh reads it again.</summary>
    public bool Settled => Kind switch { "tool" or "agent" => Done, "permission" or "question" => !Pending, _ => true };

    /// <summary>A step as one line: a tick, cross or dots, the tool and what it ran on.</summary>
    public string StepLine => Kind == "tool" ? $"{(!Done ? "…" : Error ? "✗" : "✓")} {Tool}: {Text}" : Text;

    /// <summary>Whether there's more to a step than its line (a result, a diff, a written file's start).</summary>
    public bool HasDetail => Result is not null || Removed is not null || Added is not null || Preview is not null;

    /// <summary>Characters shown before "show more", however few lines they make: a long paragraph wraps to a screenful too.</summary>
    public const int FoldChars = 1200;

    /// <summary>The text cut to a screenful (<paramref name="lines"/> lines or <see cref="FoldChars"/> characters, at a line's end
    /// where it can), with how many lines were left out, counting a line cut short; whole and 0 when it fits.</summary>
    public static (string Shown, int Hidden) FoldText(string text, int lines = FoldLines)
    {
        var all = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var keep = new List<string>();
        var chars = 0;
        foreach (var line in all)
        {
            if (keep.Count == lines || (keep.Count > 0 && chars + line.Length > FoldChars)) break;
            keep.Add(line);
            chars += line.Length + 1;
        }
        var hidden = all.Length - keep.Count;
        if (keep[0].Length > FoldChars)
        {
            // one paragraph longer than a screen: cut it at a word, and count its rest as a line left out
            var cut = keep[0].LastIndexOf(' ', FoldChars);
            keep[0] = keep[0][..(cut > FoldChars / 2 ? cut : FoldChars)] + " …";
            hidden++;
        }
        return (string.Join("\n", keep), hidden);
    }

    public static TranscriptEntry From(JsonObject o) => new(
        o["i"] is JsonValue v && v.TryGetValue<int>(out var i) ? i : -1, RemoteAgent.Str(o["kind"]) ?? "", RemoteAgent.Str(o["text"]) ?? "",
        RemoteAgent.Str(o["tool"]), RemoteAgent.Str(o["result"]), o["done"] is null || RemoteAgent.Bool(o["done"]), RemoteAgent.Bool(o["error"]),
        RemoteAgent.Bool(o["pending"]), RemoteAgent.Str(o["removed"]), RemoteAgent.Str(o["added"]), RemoteAgent.Str(o["preview"]));
}

/// <summary>One agent's transcript kept up to date from afar (AND-0005): each <see cref="Refresh"/> asks the API's <c>agent</c>
/// command only for entries from the first that could still change (a step still running, an ask still waiting) or else the
/// first not yet seen, so a phone polling a long transcript fetches a few entries, not all of it. <see cref="Send"/> nudges
/// the agent, mid-turn too.</summary>
public sealed class AgentFeed(ApiCall call, string id)
{
    readonly List<TranscriptEntry> _entries = [];

    public string Id { get; } = id;
    public string Name { get; private set; } = "";
    public string Project { get; private set; } = "";
    /// <summary>The state word from the API: starting, working, needsyou, idle, exited, saved.</summary>
    public string Status { get; private set; } = "";
    /// <summary>The desktop's longer state line ("Working · 12 steps").</summary>
    public string StateText { get; private set; } = "";
    public string Activity { get; private set; } = "";
    public string? Card { get; private set; }
    public bool Working => Status is "working" or "starting";
    public bool NeedsYou => Status == "needsyou";
    public IReadOnlyList<TranscriptEntry> Entries => _entries;

    /// <summary>Read what changed. Returns the index from which <see cref="Entries"/> changed (its count when nothing did),
    /// so a view replaces from there rather than redrawing everything.</summary>
    public async Task<int> Refresh()
    {
        var from = _entries.FindIndex(e => !e.Settled);
        if (from < 0) from = _entries.Count;
        var changedFrom = from;
        var reset = false;
        while (true)
        {
            if (await call("agent", new { id = Id, since = from }) is not JsonObject o) throw new ApiError("The agent didn't come back as an object.");
            Name = RemoteAgent.Str(o["name"]) ?? Name;
            Project = RemoteAgent.Str(o["project"]) ?? Project;
            Status = RemoteAgent.Str(o["status"]) ?? Status;
            StateText = RemoteAgent.Str(o["state"]) ?? StateText;
            Activity = RemoteAgent.Str(o["activity"]) ?? "";
            Card = RemoteAgent.Str(o["card"]);
            var got = (o["items"] as JsonArray ?? []).OfType<JsonObject>().Select(TranscriptEntry.From).ToList();
            // a desktop from before since (no total) answers its last entries: take them as they come, no paging
            var total = o["total"] is JsonValue t && t.TryGetValue<int>(out var tn) ? tn : from + got.Count;
            var next = o["next"] is JsonValue x && x.TryGetValue<int>(out var xn) ? xn : total;
            if (total < from && !reset)
            {
                // fewer entries than read before: a different transcript (the app restarted on another one). Read it afresh.
                _entries.Clear();
                from = changedFrom = 0;
                reset = true;
                continue;
            }
            if (from < _entries.Count) _entries.RemoveRange(from, _entries.Count - from);
            _entries.AddRange(got);
            if (got.Count == 0 || next >= total) break;
            from = next;
        }
        return changedFrom;
    }

    /// <summary>Send the agent a message, as typing in its box on the desktop does; while it's at work it reads it mid-turn.</summary>
    public async Task<string> Send(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return "";
        return (await call("say", new { id = Id, text, queue = true }))?.ToString() ?? "";
    }
}
