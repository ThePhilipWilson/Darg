using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>claude-draw: the canvas agents ask Philip to sketch or mark up on. Its daemon is shared by
/// every Claude Code session on the machine (each session's MCP server starts it on first use), so
/// Darg only reads its state, opens the canvas and starts a blank one; agents do the asking and
/// collecting through their own plugin tools.</summary>
public static class ClaudeDraw
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static int Port => int.TryParse(Environment.GetEnvironmentVariable("CLAUDE_DRAW_PORT"), out var p) ? p : 7331;
    public static string Url => $"http://127.0.0.1:{Port}/";

    public sealed record State(int Canvases, bool Asking, int Queued, int Unread, int Sessions, bool Lan, string? Code, string? LanUrl);

    /// <summary>The daemon's state, or null when it isn't running.</summary>
    public static async Task<State?> Read()
    {
        try
        {
            var s = JsonNode.Parse(await Http.GetStringAsync(Url + "state"));
            if (s is null || (bool?)s["ok"] != true) return null;
            return new State(
                (int?)s["canvases"] ?? 0,
                s["active"] is JsonObject,
                (int?)s["queued"] ?? 0,
                (s["uncollected"] as JsonArray)?.Count ?? 0,
                (s["sessions"] as JsonArray)?.Count ?? 0,
                (bool?)s["lan"] ?? false,
                (string?)s["code"],
                (string?)s["urls"]?["lan"]?[0]?["url"]);
        }
        catch (Exception) { return null; }
    }

    public static string Describe(State? s)
    {
        if (s is null) return "not running: it starts when an agent first asks for a drawing";
        var parts = new List<string>
        {
            s.Canvases switch { 0 => "no canvas open", 1 => "1 canvas open", var n => $"{n} canvases open" },
        };
        if (s.Asking || s.Queued > 0) parts.Add($"{(s.Asking ? 1 : 0) + s.Queued} waiting to be drawn");
        if (s.Unread > 0) parts.Add($"{s.Unread} sent, not yet read by an agent");
        parts.Add(s.Sessions == 1 ? "1 session" : $"{s.Sessions} sessions");
        if (s.Lan && s.LanUrl is not null) parts.Add($"other devices: {s.LanUrl}" + (s.Code is { Length: 6 } c ? $" (code {c[..3]}-{c[3..]})" : ""));
        return string.Join(" · ", parts);
    }

    /// <summary>Put a blank canvas up for Philip to draw on unasked. The drawing waits in the daemon until an
    /// agent collects it. Error text, or null on success.</summary>
    public static async Task<string?> Blank()
    {
        try
        {
            using var r = await Http.PostAsJsonAsync(Url + "blank", new { });
            if (r.IsSuccessStatusCode) return null;
            var body = JsonNode.Parse(await r.Content.ReadAsStringAsync());
            return (string?)body?["error"] ?? $"the daemon said {(int)r.StatusCode}";
        }
        catch (Exception e) { return e.Message; }
    }

    public static void Open()
    {
        try { Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    /// <summary>The daemon script, wherever claude-draw is installed: as a skills folder, or as a plugin.</summary>
    public static string? DaemonScript()
    {
        var claude = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var skill = Path.Combine(claude, "skills", "claude-draw", "server", "daemon.js");
        if (File.Exists(skill)) return skill;
        var plugins = Path.Combine(claude, "plugins");
        if (!Directory.Exists(plugins)) return null;
        try
        {
            return Directory.EnumerateFiles(plugins, "daemon.js", SearchOption.AllDirectories)
                .Where(f => f.Replace('\\', '/').Contains("claude-draw") && f.Replace('\\', '/').EndsWith("server/daemon.js"))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception) { return null; }
    }

    /// <summary>Start the daemon the way the plugin does (node daemon.js). Like everything Darg starts it
    /// closes with Darg; any session that needs it afterwards starts it again on its own. False if
    /// claude-draw or node can't be found.</summary>
    public static bool Start()
    {
        var script = DaemonScript();
        if (script is null) return false;
        try
        {
            var psi = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(script)! };
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(Port.ToString());
            return Process.Start(psi) is not null;
        }
        catch (Exception) { return false; }
    }

    /// <summary>What the Draw button in an agent's compose bar sends: the agent asks through its own
    /// claude-draw tools, so the drawing comes back to that conversation and nobody has to say which.</summary>
    public static string AskToDraw(string about) =>
        about.Trim().Length == 0
            ? "I want to draw something for you. Put a blank canvas up for me with request_drawing (a short prompt such as \"Draw what you want to show me\"), wait for it with collect_drawing, then read what I send and carry on from it."
            : $"I want to draw something for you about this: {about.Trim()}\n\nPut a canvas up for me with request_drawing: if it is about something on screen or an image you can capture or already have, pass it as image_path so I can mark it up; otherwise a blank canvas. Make the prompt say what to draw. Wait for it with collect_drawing, then read what I send and carry on from it.";
}
