using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Darg.Core;

/// <summary>One agent session's clock (WOR-0206): when each step of its start and of every turn happened, as a line
/// each in agents.log beside perf.log, so time to the first tool and turn time can be read per agent and set beside a
/// plain <c>claude -p</c>. The clock starts when the agent is made, so a session's first turn counts the model pick
/// (Haiku's, before the process even starts) as well as Claude Code's own start-up (hooks, MCP servers).
///
/// A line: "2026-11-25 02:30:01.123 68b84a2989 +7.31 s turn 1 +4.02 s · init · mcp failed: tabby, unity-mcp".
/// The first figure counts from the start, the second from the turn's message being sent.</summary>
public sealed class AgentTimings
{
    /// <summary>Where lines go: agents.log in the instance folder, read on every line (a test changes DARG_HOME); null: nowhere.</summary>
    public static Func<string?> LogFile { get; set; } = () =>
    {
        try { return Path.Combine(InstanceConfig.Folder(), "agents.log"); }
        catch (Exception) { return null; }
    };

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly object _lock = new();
    readonly Action<string>? _sink;
    readonly bool _toFile;

    public string AgentId { get; }

    int _turn;                 // turns begun on this session
    bool _inTurn;
    int _queued;               // messages sent mid-turn, each its own turn after this one
    TimeSpan _turnAt;          // when the turn's message was sent
    TimeSpan? _pickStart, _pickTook, _initAt, _reply, _tool, _result;
    string? _toolName;

    /// <summary>What the last turn to end took, said for its "Turn done" line: "14.2 s (model pick 1.1 s · init at 6.3 s ·
    /// first tool at 9.0 s)". Empty until a turn ends.</summary>
    public string LastTurn { get; private set; } = "";

    /// <param name="sink">Takes each line too (tests).</param>
    /// <param name="toFile">False: lines go only to the sink, never to agents.log.</param>
    public AgentTimings(string agentId, Action<string>? sink = null, bool toFile = true) { AgentId = agentId; _sink = sink; _toFile = toFile; }

    TimeSpan Now => _clock.Elapsed;
    static string S(TimeSpan t) => $"{t.TotalSeconds:0.00} s";

    void Mark(string what)
    {
        var at = Now;
        var turn = _inTurn ? $" turn {_turn} +{S(at - _turnAt)}" : "";
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {AgentId} +{S(at)}{turn} · {what}";
        _sink?.Invoke(line);
        if (_toFile && LogFile() is { } file) Writer.Add(file, line);
    }

    /// <summary>The model profile is being picked (a rule's is at once, Haiku's takes seconds).</summary>
    public void PickStarted() { lock (_lock) { _pickStart = Now; Mark("model pick start"); } }

    public void PickEnded(string? profile, string? why)
    {
        lock (_lock)
        {
            _pickTook = _pickStart is { } s ? Now - s : null;
            Mark($"model pick end{(_pickTook is { } t ? $" ({S(t)})" : "")} · {(profile is null ? "none, own settings" : $"{profile}: {why}")}");
        }
    }

    /// <summary>The claude process has been started; <paramref name="spawn"/> is how long Process.Start took.</summary>
    public void ProcessStarted(TimeSpan spawn, int args) { lock (_lock) Mark($"process start (spawn {spawn.TotalMilliseconds:0} ms, {args} args)"); }

    /// <summary>A message went to the session: a turn starts, or follows the one running.</summary>
    public void Sent()
    {
        lock (_lock)
        {
            if (_inTurn) { _queued++; Mark("message sent mid-turn"); return; }
            BeginTurn("message sent");
        }
    }

    void BeginTurn(string what)
    {
        _turn++;
        _inTurn = true;
        _turnAt = Now;
        _reply = _tool = _result = null;
        _toolName = null;
        Mark(what);
    }

    /// <summary>The host answered a permission ask or question.</summary>
    public void Answered(string requestId) { lock (_lock) Mark($"permission answered ({requestId})"); }

    public void Exited(int code) { lock (_lock) { _inTurn = false; Mark($"process exit {code}"); } }

    /// <summary>A stream-json line from the session, before it becomes events.</summary>
    public void Saw(JsonObject m)
    {
        lock (_lock)
        {
            var type = Str(m["type"]);
            var subtype = Str(m["subtype"]);
            switch (type)
            {
                case "control_response" when Str(m["response"]?["request_id"]) == "init":
                    Mark("initialized (control)");
                    break;
                case "system" when subtype == "init":
                    _initAt ??= Now;
                    Mark("init" + McpLine(m["mcp_servers"] as JsonArray));
                    break;
                case "system" when subtype is "hook_started" or "hook_response":
                    Mark($"{(subtype == "hook_started" ? "hook start" : "hook end")} {Str(m["hook_name"]) ?? Str(m["hook_event"]) ?? "?"}"
                        + (Str(m["outcome"]) is { } o && o != "success" ? $" ({o})" : ""));
                    break;
                case "system" when subtype is not null and not "status":
                    Mark("system " + subtype);
                    break;
                case "assistant":
                    var content = m["message"]?["content"] as JsonArray ?? [];
                    if (_reply is null) { _reply = Now - _turnAt; Mark("first reply"); }
                    if (_tool is null && content.FirstOrDefault(c => Str(c?["type"]) == "tool_use") is { } use)
                    {
                        _tool = Now - _turnAt;
                        _toolName = Str(use["name"]);
                        Mark($"first tool call {_toolName}");
                    }
                    break;
                case "user" when _result is null && _tool is not null
                                 && (m["message"]?["content"] as JsonArray ?? []).Any(c => Str(c?["type"]) == "tool_result"):
                    _result = Now - _turnAt;
                    Mark($"first tool result ({S(_result.Value - _tool.Value)} after the call)");
                    break;
                case "control_request" when Str(m["request"]?["subtype"]) == "can_use_tool":
                    Mark($"permission asked {Str(m["request"]?["tool_name"])} ({Str(m["request_id"])})");
                    break;
                case "result":
                    EndTurn(m);
                    break;
            }
        }
    }

    void EndTurn(JsonObject m)
    {
        var took = Now - _turnAt;
        var cli = Number(m["duration_ms"]) is { } d ? $" · Claude Code says {d / 1000:0.00} s, API {Number(m["duration_api_ms"]) / 1000 ?? 0:0.00} s" : "";
        Mark($"turn end ({S(took)}{(_tool is { } t ? $", first tool at {S(t)}" : ", no tools")}){cli}");
        // the first turn counts from the agent being made, model pick and start-up and all: that's what was waited
        var parts = new List<string>();
        var first = _turn == 1;
        if (first && _pickTook is { } p) parts.Add($"model pick {S(p)}");
        if (first && _initAt is { } i) parts.Add($"init at {S(i)}");
        if (_tool is { } tool) parts.Add($"first tool at {S(first ? tool + _turnAt : tool)}");
        var total = first ? Now : took;
        LastTurn = S(total) + (parts.Count > 0 ? $" ({string.Join(" · ", parts)})" : "");
        _inTurn = false;
        if (_queued > 0) { _queued--; BeginTurn("next message's turn"); }
    }

    static string McpLine(JsonArray? servers)
    {
        if (servers is null || servers.Count == 0) return "";
        var byStatus = servers.OfType<JsonObject>().GroupBy(s => Str(s["status"]) ?? "?").ToDictionary(g => g.Key, g => g.Select(s => Str(s["name"]) ?? "?").ToList());
        var sb = new StringBuilder($" · {servers.Count} mcp");
        foreach (var (status, names) in byStatus.Where(k => k.Key != "connected").OrderBy(k => k.Key))
            sb.Append($" · {status}: {string.Join(", ", names)}");
        return sb.ToString();
    }

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static double? Number(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

    /// <summary>Appends lines in order on a thread of its own, so the UI thread (a pick, a message sent) never waits on the disk.</summary>
    public static class Writer
    {
        static readonly Channel<(string File, string Line)> Lines = Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions { SingleReader = true });
        static int _pending;
        static readonly Task Loop = Task.Run(Drain);

        public static void Add(string file, string line)
        {
            _ = Loop;
            Interlocked.Increment(ref _pending);
            Lines.Writer.TryWrite((file, line));
        }

        /// <summary>Wait (a little) until every line added so far is on disk.</summary>
        public static async Task Flush(int ms = 3000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (Volatile.Read(ref _pending) > 0 && DateTime.UtcNow < end) await Task.Delay(20);
        }

        static async Task Drain()
        {
            await foreach (var (file, line) in Lines.Reader.ReadAllAsync())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    var f = new FileInfo(file);
                    if (f.Exists && f.Length > 1_000_000) File.Move(file, Path.ChangeExtension(file, ".old.log"), overwrite: true);
                    File.AppendAllText(file, line + Environment.NewLine);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                finally { Interlocked.Decrement(ref _pending); }
            }
        }
    }
}
