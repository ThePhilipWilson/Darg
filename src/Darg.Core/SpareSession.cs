using System.Text;

namespace Darg.Core;

/// <summary>A Claude session started ahead of time and kept idle (WOR-0235), so the next new chat's first message goes to
/// a process that has already started up: node, the person's SessionStart hooks and MCP servers run, and the initialize
/// answered, all before any message arrives (checked on Claude Code 2.1.326). Nothing is sent to it, so it costs no model
/// call; a chat takes it only when what it would have started with is exactly what the spare did, else it starts afresh.
///
/// "Exactly" is <see cref="KeyFor"/>: the folder, the API address, every argument, and the contents of the files the
/// arguments name (<see cref="FileArgs"/>): the --mcp-config and the briefing are files at fixed paths whose contents
/// change (a server ticked on the Context page, a project added in Setup), so the args alone would match a stale spare.</summary>
public sealed class SpareSession
{
    /// <summary>Flags whose next argument is a file Claude Code reads as it starts: what's in it is part of the match.</summary>
    public static readonly IReadOnlyList<string> FileArgs = ["--mcp-config", "--append-system-prompt-file", "--settings"];

    public AgentSession Session { get; }
    /// <summary>What it was started with (<see cref="KeyFor"/>).</summary>
    public string Key { get; }
    /// <summary>Its own id in agents.log until a chat takes it ("spare-…").</summary>
    public string Id { get; }

    SpareSession(AgentSession session, string key, string id) { Session = session; Key = key; Id = id; }

    /// <summary>What a session started in <paramref name="cwd"/> with <paramref name="args"/> and the API at
    /// <paramref name="apiUrl"/> would be, as one string: two keys equal means one session can stand in for the other.</summary>
    public static string KeyFor(string cwd, IReadOnlyList<string> args, string? apiUrl)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFullPath(cwd).TrimEnd('\\', '/')).Append('\n').Append(apiUrl).Append('\n');
        for (var i = 0; i < args.Count; i++)
        {
            sb.Append(args[i]).Append('\n');
            if (i + 1 < args.Count && FileArgs.Contains(args[i]))
            {
                string text;
                try { text = File.Exists(args[i + 1]) ? File.ReadAllText(args[i + 1]) : "(missing)"; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { text = "(unreadable " + Guid.NewGuid() + ")"; } // never matches
                sb.Append("<<").Append(text).Append(">>\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>Start one: its process starts and answers initialize; nothing more is sent until a chat takes it.</summary>
    public static SpareSession Start(string cwd, IReadOnlyList<string> args, string? apiUrl, string? key = null)
    {
        var id = "spare-" + Guid.NewGuid().ToString("N")[..6];
        var session = AgentSession.Start(cwd, extraArgs: args, apiUrl: apiUrl, timings: new AgentTimings(id));
        return new SpareSession(session, key ?? KeyFor(cwd, args, apiUrl), id);
    }

    /// <summary>Whether it is still running and was started as <paramref name="key"/> says a new session would be.</summary>
    public bool Fits(string key) => Session.Running && Key == key;

    /// <summary>Why it can't stand in for <paramref name="key"/>, for agents.log; null when it can.</summary>
    public string? Misfit(string key) => !Session.Running ? "its process has ended" : Key != key ? "what a new chat starts with has changed" : null;

    /// <summary>End it (not taken, or no longer fits).</summary>
    public void End(string why)
    {
        Session.Timings?.Note($"spare ended: {why}");
        _ = Session.DisposeAsync().AsTask();
    }
}
