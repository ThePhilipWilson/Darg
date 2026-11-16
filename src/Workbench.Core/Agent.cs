using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workbench.Core;

/// <summary>What an agent's session says, parsed from Claude Code's stream-json output.</summary>
public abstract record AgentEvent;
/// <param name="ApiKeySource">Where Claude Code's login comes from: "none" means no API key, so the
/// person's Claude subscription; anything else names the API key's source (and real per-token cost).</param>
public sealed record SessionStarted(string SessionId, string Model, string Cwd, string ApiKeySource = "none", string PermissionMode = "") : AgentEvent
{
    public bool OnSubscription => ApiKeySource is "" or "none";
}
/// <summary>The session's permission mode changed (asked for by the host, or by the agent itself, as when it leaves plan mode).</summary>
public sealed record ModeChanged(string Mode) : AgentEvent;
/// <summary>The session now runs on this model (its full name), after the host asked for a change.</summary>
public sealed record ModelChanged(string Model) : AgentEvent;
/// <summary>The session refused something the host asked of it (a model, mode or effort it won't take).</summary>
public sealed record ControlFailed(string RequestId, string Error) : AgentEvent;

/// <summary>One of the choices for a session setting: what Claude Code takes, and what the person reads.</summary>
public sealed record AgentChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>What can be changed in a running session (and given at the start of one): model, effort and
/// permission mode. "default" leaves it to the person's own Claude settings. Bypass permissions is left out:
/// a session refuses it unless it was launched with --dangerously-skip-permissions.</summary>
public static class AgentChoices
{
    public const string Default = "default";

    public static readonly IReadOnlyList<AgentChoice> Models =
    [
        new(Default, "Model: default"), new("opus", "Opus"), new("fable", "Fable"), new("sonnet", "Sonnet"), new("haiku", "Haiku"),
    ];
    public static readonly IReadOnlyList<AgentChoice> Efforts =
    [
        new(Default, "Effort: default"), new("low", "Effort: low"), new("medium", "Effort: medium"), new("high", "Effort: high"),
        new("xhigh", "Effort: xhigh"), new("max", "Effort: max"),
    ];
    public static readonly IReadOnlyList<AgentChoice> Modes =
    [
        new(Default, "Ask before acting"), new("acceptEdits", "Accept edits"), new("auto", "Auto"), new("plan", "Plan"),
        new("dontAsk", "Don't ask (deny unallowed)"),
    ];

    /// <summary>The model list with the default named, e.g. "Model: default (Opus 5.5)"; just "Model: default"
    /// when it isn't known.</summary>
    public static IReadOnlyList<AgentChoice> ModelsWithDefault(string? defaultModel) =>
        string.IsNullOrWhiteSpace(defaultModel) ? Models
            : [new(Default, $"Model: default ({ModelName(defaultModel)})"), .. Models.Skip(1)];

    /// <summary>The model a session started in <paramref name="cwd"/> with no --model gets, as Claude Code
    /// picks it: ANTHROPIC_MODEL, else "model" in the project's .claude/settings.local.json, its
    /// .claude/settings.json, then the person's own settings.json. Null when none says (the account's default,
    /// which only a running session reports).</summary>
    public static string? DefaultModel(string? cwd, string? home = null)
    {
        if (Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") is { Length: > 0 } env) return env;
        home ??= Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var files = new List<string>();
        if (!string.IsNullOrEmpty(cwd))
        {
            files.Add(Path.Combine(cwd, ".claude", "settings.local.json"));
            files.Add(Path.Combine(cwd, ".claude", "settings.json"));
        }
        files.Add(Path.Combine(home, "settings.json"));
        foreach (var f in files)
        {
            try
            {
                if (File.Exists(f) && JsonNode.Parse(File.ReadAllText(f)) is JsonObject o
                    && o["model"] is JsonValue v && v.TryGetValue<string>(out var m) && m.Trim().Length > 0) return m.Trim();
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>A model id or alias as a person says it: "claude-opus-5-5" is "Opus 5.5", "opus[1m]" is
    /// "Opus (1M)", "claude-haiku-4-5-20251001" is "Haiku 4.5". Anything else is shown as given.</summary>
    public static string ModelName(string model)
    {
        var s = model.Trim();
        var suffix = "";
        if (s.EndsWith(']') && s.LastIndexOf('[') is var b and > 0)
        {
            suffix = $" ({s[(b + 1)..^1].ToUpperInvariant()})";
            s = s[..b];
        }
        if (s.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) s = s["claude-".Length..];
        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count == 0) return model;
        if (parts.Count > 1 && parts[^1].Length == 8 && parts[^1].All(char.IsDigit)) parts.RemoveAt(parts.Count - 1); // a date stamp
        if (!parts[0].All(char.IsLetter)) return model;
        var name = char.ToUpperInvariant(parts[0][0]) + parts[0][1..].ToLowerInvariant();
        var version = parts.Skip(1).ToList();
        if (version.Count > 0 && !version.All(p => p.All(char.IsDigit))) return model;
        return name + (version.Count > 0 ? " " + string.Join('.', version) : "") + suffix;
    }

    /// <summary>The flags that start (or resume) a session with these choices; a default adds nothing.</summary>
    public static IEnumerable<string> LaunchArgs(string? model, string? effort, string? mode)
    {
        if (model is { Length: > 0 } and not Default) { yield return "--model"; yield return model; }
        if (effort is { Length: > 0 } and not Default) { yield return "--effort"; yield return effort; }
        if (mode is { Length: > 0 } and not Default) { yield return "--permission-mode"; yield return mode; }
    }

    /// <summary>The choice for a value: a known one, else a made-up entry so a value set elsewhere still shows.</summary>
    public static AgentChoice Find(IReadOnlyList<AgentChoice> list, string? value) =>
        list.FirstOrDefault(c => string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase))
        ?? (string.IsNullOrEmpty(value) || value == "manual" ? list[0] : new AgentChoice(value, value));
}
public sealed record AssistantText(string Text) : AgentEvent;
public sealed record ToolCall(string Id, string Name, JsonObject Input) : AgentEvent;
/// <param name="Images">Images the tool returned in its result (an MCP tool's image content), as file bytes.</param>
public sealed record ToolResult(string ToolUseId, string Text, bool IsError, IReadOnlyList<byte[]>? Images = null) : AgentEvent;
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
                    (string?)m["apiKeySource"] ?? "none", (string?)m["permissionMode"] ?? "");
                break;
            case "system" when (string?)m["subtype"] == "status" && (string?)m["permissionMode"] is { Length: > 0 } mode:
                yield return new ModeChanged(mode);
                break;
            case "control_response":
                var resp = m["response"];
                if ((string?)resp?["subtype"] == "error")
                    yield return new ControlFailed((string?)resp["request_id"] ?? "", (string?)resp["error"] ?? "refused");
                else if ((string?)resp?["response"]?["model"] is { Length: > 0 } model)
                    yield return new ModelChanged(model);
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
                        yield return new ToolResult((string?)c["tool_use_id"] ?? "", ResultText(c["content"]), (bool?)c["is_error"] ?? false,
                            ResultImages(c["content"]));
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

    static List<byte[]> ResultImages(JsonNode? content)
    {
        var images = new List<byte[]>();
        foreach (var block in content as JsonArray ?? [])
            if ((string?)block?["type"] == "image" && (string?)block?["source"]?["data"] is { Length: > 0 } b64)
                try { images.Add(Convert.FromBase64String(b64)); } catch (FormatException) { }
        return images;
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

    /// <summary>Switch the session's model ("default" for the person's own). Takes effect from the next turn.</summary>
    public void SetModel(string model) => Control(new JsonObject
    {
        ["subtype"] = "set_model", ["model"] = model == AgentChoices.Default ? null : model,
    });

    /// <summary>Switch the session's effort level ("default" for the person's own).</summary>
    public void SetEffort(string effort) => Control(new JsonObject
    {
        ["subtype"] = "apply_flag_settings",
        ["settings"] = new JsonObject { ["effortLevel"] = effort == AgentChoices.Default ? null : effort },
    });

    /// <summary>Switch the session's permission mode (default, acceptEdits, auto, plan, dontAsk).</summary>
    public void SetPermissionMode(string mode) => Control(new JsonObject { ["subtype"] = "set_permission_mode", ["mode"] = mode });

    void Control(JsonObject request) => SendRaw(new JsonObject
    {
        ["type"] = "control_request", ["request_id"] = $"set-{Interlocked.Increment(ref _nextId)}", ["request"] = request,
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
