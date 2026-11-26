using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>Something an API command can't do with what it was given (a project or card that isn't there, a
/// missing argument): the caller gets the message back with a 400, so it can correct itself.</summary>
public sealed class ApiError(string message) : Exception(message);

/// <summary>One command of the Darg API: its name, a line on what it does and what it takes, and the work.</summary>
public sealed record ApiCommand(string Name, string Usage, Func<ApiArgs, Task<string>> Run);

/// <summary>What a command was called with: the JSON object in the body (or the body as "text" when it isn't
/// JSON), with the query string's values added. Read with <see cref="Get"/> and <see cref="Need"/>.</summary>
public sealed class ApiArgs
{
    readonly JsonObject _o;
    public ApiArgs(JsonObject o) => _o = o;

    public static ApiArgs Parse(string body, IEnumerable<KeyValuePair<string, string>>? query = null)
    {
        JsonObject o;
        var t = body.Trim();
        if (t.Length == 0) o = [];
        else
        {
            try { o = JsonNode.Parse(t) as JsonObject ?? new JsonObject { ["text"] = t }; }
            catch (JsonException) { o = new JsonObject { ["text"] = t }; }
        }
        foreach (var (k, v) in query ?? []) o[k] ??= v;
        return new ApiArgs(o);
    }

    /// <summary>A value as text (numbers and booleans as written), or null when it's missing or empty.</summary>
    public string? Get(string name)
    {
        var n = _o[name];
        if (n is null) return null;
        var s = n is JsonValue v && v.TryGetValue<string>(out var str) ? str : n.ToJsonString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    public string Need(string name) => Get(name) ?? throw new ApiError($"Missing \"{name}\".");

    /// <summary>A list of values: a JSON array, or one string split on spaces, commas and lines.</summary>
    public IReadOnlyList<string> List(string name) => _o[name] switch
    {
        JsonArray a => a.Select(x => x?.ToString().Trim() ?? "").Where(x => x.Length > 0).ToList(),
        null => [],
        var n => (Get(name) ?? "").Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList(),
    };
}

/// <summary>The Darg API: commands the app registers (open a card, start an agent, send it a message...)
/// that Fettle, or any script on this machine holding the instance's token, calls over HTTP through
/// <see cref="ArtifactServer"/>: GET /api/&lt;token&gt; lists them, /api/&lt;token&gt;/&lt;name&gt; runs one.
/// The list is what Fettle reads to learn what it can do, so a new command is usable the moment it's registered.</summary>
public sealed class DargApi
{
    readonly Dictionary<string, ApiCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ApiCommand> Commands => _commands.Values;

    public void Add(string name, string usage, Func<ApiArgs, Task<string>> run) => _commands[name] = new ApiCommand(name, usage, run);
    public void Add(string name, string usage, Func<ApiArgs, string> run) => Add(name, usage, a => Task.FromResult(run(a)));

    /// <summary>Forget every command: the window that registered them has closed (WOR-0226).</summary>
    public void Clear() => _commands.Clear();

    /// <summary>The commands as text: one per line, name then usage.</summary>
    public string Help()
    {
        var sb = new StringBuilder();
        foreach (var c in _commands.Values.OrderBy(c => c.Name, StringComparer.Ordinal)) sb.AppendLine($"{c.Name}: {c.Usage}");
        return sb.ToString();
    }

    /// <summary>Run a command: (status, body). 404 for a command that isn't there, 400 for an <see cref="ApiError"/>.</summary>
    public async Task<(int Status, string Body)> Call(string name, ApiArgs args)
    {
        if (!_commands.TryGetValue(name, out var c)) return (404, $"No command \"{name}\". These are:\n{Help()}");
        try { return (200, await c.Run(args)); }
        catch (ApiError e) { return (400, e.Message); }
    }

    /// <summary>JSON for a result: indented a little so it reads in a terminal too.</summary>
    public static string Json(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
