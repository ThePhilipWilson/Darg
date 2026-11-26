using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>One quick question to Claude Haiku: one turn, no tools, plain text back. What chat titles and
/// model-profile picks use.</summary>
public static class Haiku
{
    /// <summary>What claude is started with. <c>--strict-mcp-config</c> with no <c>--mcp-config</c> starts no MCP servers:
    /// without it every question started each of the person's user-scope and plugin servers (npx, uvx, node), about
    /// 1 GB and eight node processes for a one-word answer with no tools to use them (WOR-0207).
    /// <c>--setting-sources ""</c> reads none of the person's settings (WOR-0224): with them every question ran their
    /// SessionStart and Stop hooks, started their plugins and sent their CLAUDE.md files and skill list, 34,000 tokens
    /// for a self-contained prompt; init came 2.4 to 4.2 s after the start and the answer at 4 to 6 s. Without, 4,300
    /// tokens and the answer at about 2 s. Sign-in still works (it isn't a setting). <c>json</c> output arrives as one
    /// line the moment the answer is in, so the 0.7 s claude takes to exit after it isn't waited.</summary>
    public static readonly IReadOnlyList<string> Args = ["-p", "--model", "haiku", "--tools", "", "--output-format", "json", "--strict-mcp-config", "--setting-sources", ""];

    /// <summary>An empty folder of its own that every question is asked from (WOR-0219), so the answer doesn't depend on
    /// where Darg was started: claude loads the CLAUDE.md files, memory and project hooks of the folder it starts in, and
    /// with Darg's own repo around it a card placing's answer swung from 1 in 10 right to 10 in 10. Null when it can't be
    /// made, and then claude starts where Darg is.</summary>
    public static string? Folder()
    {
        try { return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "darg-haiku")).FullName; }
        catch (Exception) { return null; }
    }

    /// <summary>Haiku's reply, or null when Claude isn't found, fails, or takes longer than the timeout. Never throws.</summary>
    public static async Task<string?> Ask(string prompt, TimeSpan timeout)
    {
        var claude = AgentSession.FindClaude();
        if (claude is null) return null;
        Process? p = null;
        try
        {
            var psi = new ProcessStartInfo(claude)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var a in Args) psi.ArgumentList.Add(a);
            if (Folder() is { } folder) psi.WorkingDirectory = folder;
            p = Process.Start(psi)!;
            await p.StandardInput.WriteAsync(prompt);
            p.StandardInput.Close();
            using var cts = new CancellationTokenSource(timeout);
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(cts.Token)) is not null)
                if (Result(line) is { } answer)
                {
                    Reap(p, timeout);
                    p = null; // Reap has it now
                    return answer.Ok ? answer.Text : null;
                }
            await p.WaitForExitAsync(cts.Token);
            return null; // it ended without a result
        }
        catch (Exception)
        {
            try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch (Exception) { }
            return null;
        }
        finally { p?.Dispose(); }
    }

    /// <summary>The answer in a line of claude's <c>--output-format json</c>: its result object; null for any other line.</summary>
    public static (bool Ok, string Text)? Result(string line)
    {
        if (!line.TrimStart().StartsWith('{')) return null;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(line) is not System.Text.Json.Nodes.JsonObject o || (string?)o["type"] != "result") return null;
            var text = o["result"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
            var error = o["is_error"] is System.Text.Json.Nodes.JsonValue e && e.TryGetValue<bool>(out var b) && b;
            return (!error, text);
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>The answer is in: let claude finish exiting by itself, and end it if it hasn't within the timeout.</summary>
    static void Reap(Process p, TimeSpan timeout) => _ = Task.Run(async () =>
    {
        try { await p.WaitForExitAsync().WaitAsync(timeout); }
        catch (Exception) { try { p.Kill(entireProcessTree: true); } catch (Exception) { } }
        finally { p.Dispose(); }
    });
}
