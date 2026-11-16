using System.Diagnostics;
using System.Text;

namespace Workbench.Core;

/// <summary>One quick question to Claude Haiku: one turn, no tools, plain text back. What chat titles and
/// model-profile picks use.</summary>
public static class Haiku
{
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
            foreach (var a in new[] { "-p", "--model", "haiku", "--tools", "", "--output-format", "text" }) psi.ArgumentList.Add(a);
            p = Process.Start(psi)!;
            await p.StandardInput.WriteAsync(prompt);
            p.StandardInput.Close();
            using var cts = new CancellationTokenSource(timeout);
            var output = await p.StandardOutput.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch (Exception) { }
            return null;
        }
        finally { p?.Dispose(); }
    }
}
