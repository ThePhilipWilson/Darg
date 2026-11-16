using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workbench.Core;

/// <summary>What an agent's session says, parsed from Claude Code's stream-json output.</summary>
public abstract record AgentEvent;
/// <param name="ApiKeySource">Where Claude Code's login comes from: "none" means no API key, so the
/// person's Claude subscription; anything else names the API key's source (and real per-token cost).</param>
public sealed record SessionStarted(string SessionId, string Model, string Cwd, string ApiKeySource = "none") : AgentEvent
{
    public bool OnSubscription => ApiKeySource is "" or "none";
}
public sealed record AssistantText(string Text) : AgentEvent;
public sealed record ToolCall(string Id, string Name, JsonObject Input) : AgentEvent;
public sealed record ToolResult(string ToolUseId, string Text, bool IsError) : AgentEvent;
/// <summary>The session needs the host's say before a tool runs. AskUserQuestion arrives this way too
/// (RequiresUser is true): it is answered by allowing it with the answers added to its input.</summary>
public sealed record PermissionAsked(string RequestId, string ToolName, JsonObject Input, bool RequiresUser) : AgentEvent;
public sealed record TurnFinished(bool IsError, string Text, double? CostUsd) : AgentEvent;
public sealed record SessionExited(int ExitCode, string Stderr) : AgentEvent;

/// <summary>Turns one line of stream-json into events (none for lines the host doesn't show).</summary>
public static class StreamParser
{
    public static IEnumerable<AgentEvent> Parse(string line)
    {
        JsonObject? m;
        try { m = JsonNode.Parse(line) as JsonObject; }
        catch (JsonException) { yield break; }
        if (m is null) yield break;
        switch ((string?)m["type"])
        {
            case "system" when (string?)m["subtype"] == "init":
                yield return new SessionStarted((string?)m["session_id"] ?? "", (string?)m["model"] ?? "", (string?)m["cwd"] ?? "",
                    (string?)m["apiKeySource"] ?? "none");
                break;
            case "assistant":
                foreach (var c in m["message"]?["content"] as JsonArray ?? [])
                {
                    switch ((string?)c?["type"])
                    {
                        case "text" when !string.IsNullOrWhiteSpace((string?)c["text"]):
                            yield return new AssistantText(((string?)c["text"])!.Trim());
                            break;
                        case "tool_use":
                            yield return new ToolCall((string?)c["id"] ?? "", (string?)c["name"] ?? "",
                                c["input"] as JsonObject ?? new JsonObject());
                            break;
                    }
                }
                break;
            case "user":
                foreach (var c in m["message"]?["content"] as JsonArray ?? [])
                    if ((string?)c?["type"] == "tool_result")
                        yield return new ToolResult((string?)c["tool_use_id"] ?? "", ResultText(c["content"]), (bool?)c["is_error"] ?? false);
                break;
            case "control_request" when ((string?)(m["request"]?["subtype"])) == "can_use_tool":
                var r = m["request"]!;
                yield return new PermissionAsked((string?)m["request_id"] ?? "", (string?)r["tool_name"] ?? "",
                    r["input"] as JsonObject ?? new JsonObject(), (bool?)r["requires_user_interaction"] ?? false);
                break;
            case "result":
                yield return new TurnFinished((bool?)m["is_error"] ?? false, (string?)m["result"] ?? "", (double?)m["total_cost_usd"]);
                break;
        }
    }

    static string ResultText(JsonNode? content) => content switch
    {
        JsonValue v => (string?)v ?? "",
        JsonArray a => string.Join("\n", a.Select(x => (string?)x?["type"] == "text" ? (string?)x?["text"] : null).OfType<string>()),
        _ => "",
    };
}

/// <summary>One agent: a real Claude Code session run headless, spoken to over stream-json. The host
/// answers every permission prompt (--permission-prompt-tool stdio), which is also what makes
/// AskUserQuestion available. The person's own hooks, skills, plugins and settings load as usual.</summary>
public sealed class AgentSession : IAsyncDisposable
{
    readonly Process _proc;
    readonly StringBuilder _stderr = new();
    readonly object _write = new();
    int _nextId;

    public event Action<AgentEvent>? Event;
    public string Cwd { get; }
    public string? SessionId { get; private set; }
    public bool Running => !_proc.HasExited;

    AgentSession(Process proc, string cwd) { _proc = proc; Cwd = cwd; }

    /// <summary>The claude executable: CLAUDE_PATH, else claude on PATH, else ~/.local/bin/claude.</summary>
    public static string? FindClaude()
    {
        var env = Environment.GetEnvironmentVariable("CLAUDE_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var names = OperatingSystem.IsWindows() ? new[] { "claude.exe", "claude.cmd" } : ["claude"];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var n in names)
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, n))) return Path.Combine(dir, n);
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", names[0]);
        return File.Exists(local) ? local : null;
    }

    public static AgentSession Start(string cwd, string? model = null, IEnumerable<string>? extraArgs = null)
    {
        var claude = FindClaude() ?? throw new FileNotFoundException("Couldn't find claude. Set CLAUDE_PATH or put it on PATH.");
        var psi = new ProcessStartInfo(claude)
        {
            WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
                                  "--permission-prompt-tool", "stdio" })
            psi.ArgumentList.Add(a);
        if (model is not null) { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model); }
        foreach (var a in extraArgs ?? []) psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var s = new AgentSession(proc, cwd);
        proc.Start();
        proc.StandardInput.AutoFlush = true;
        _ = Task.Run(s.ReadLoop);
        _ = Task.Run(async () => { string? l; while ((l = await proc.StandardError.ReadLineAsync()) is not null) lock (s._stderr) s._stderr.AppendLine(l); });
        s.SendRaw(new JsonObject { ["type"] = "control_request", ["request_id"] = "init", ["request"] = new JsonObject { ["subtype"] = "initialize" } });
        return s;
    }

    async Task ReadLoop()
    {
        string? line;
        while ((line = await _proc.StandardOutput.ReadLineAsync()) is not null)
            foreach (var e in StreamParser.Parse(line))
            {
                if (e is SessionStarted st) SessionId = st.SessionId;
                Event?.Invoke(e);
            }
        await _proc.WaitForExitAsync();
        string err; lock (_stderr) err = _stderr.ToString();
        Event?.Invoke(new SessionExited(_proc.ExitCode, err));
    }

    void SendRaw(JsonObject msg)
    {
        lock (_write) _proc.StandardInput.WriteLine(msg.ToJsonString());
    }

    /// <summary>A message from the person, as a new turn.</summary>
    public void Send(string text, IEnumerable<string>? imagePaths = null)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };
        foreach (var p in imagePaths ?? [])
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = MediaType(p), ["data"] = Convert.ToBase64String(File.ReadAllBytes(p)) },
            });
        SendRaw(new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = content } });
    }

    public void Allow(string requestId, JsonObject input) => Respond(requestId,
        new JsonObject { ["behavior"] = "allow", ["updatedInput"] = input.DeepClone() });

    public void Deny(string requestId, string message) => Respond(requestId,
        new JsonObject { ["behavior"] = "deny", ["message"] = message });

    /// <summary>Answer an AskUserQuestion: its input with "answers" (question text to chosen label) added.</summary>
    public void Answer(string requestId, JsonObject input, IReadOnlyDictionary<string, string> answers)
    {
        var withAnswers = (JsonObject)input.DeepClone();
        var a = new JsonObject();
        foreach (var (q, v) in answers) a[q] = v;
        withAnswers["answers"] = a;
        Allow(requestId, withAnswers);
    }

    /// <summary>Stop the turn in progress (the session stays open for the next message).</summary>
    public void Interrupt() => SendRaw(new JsonObject
    {
        ["type"] = "control_request", ["request_id"] = $"int-{Interlocked.Increment(ref _nextId)}",
        ["request"] = new JsonObject { ["subtype"] = "interrupt" },
    });

    void Respond(string requestId, JsonObject response) => SendRaw(new JsonObject
    {
        ["type"] = "control_response",
        ["response"] = new JsonObject { ["subtype"] = "success", ["request_id"] = requestId, ["response"] = response },
    });

    static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/png",
    };

    public async ValueTask DisposeAsync()
    {
        try { _proc.StandardInput.Close(); } catch (InvalidOperationException) { }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) { try { _proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        _proc.Dispose();
    }
}
