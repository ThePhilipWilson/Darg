using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Workbench.Core;

/// <summary>A project's live preview: the dev server that serves it and where it answers. Set in
/// instance.json as "preview"; when not set, a repo with a package.json "dev" script is used.</summary>
public sealed class PreviewConfig
{
    /// <summary>The command that starts the dev server (npm run dev, dotnet watch …).</summary>
    public string? Command { get; set; }
    /// <summary>Where the command runs: relative to the project's first repo unless rooted.</summary>
    public string? Cwd { get; set; }
    /// <summary>Where the site answers. If the server prints another local address, that one wins.</summary>
    public string? Url { get; set; }
}

public static partial class Preview
{
    /// <summary>The preview for a project: its own setting, else the first repo whose package.json has a
    /// "dev" script (with the framework's usual port), else none.</summary>
    public static PreviewConfig? For(ProjectConfig p)
    {
        if (p.Preview is { } set && (set.Command is { Length: > 0 } || set.Url is { Length: > 0 }))
        {
            var root = p.Repos.FirstOrDefault(Directory.Exists) ?? p.WorkingFolder();
            return new PreviewConfig
            {
                Command = set.Command, Url = set.Url,
                Cwd = set.Cwd is null ? root : Path.IsPathRooted(set.Cwd) ? set.Cwd : Path.Combine(root, set.Cwd),
            };
        }
        foreach (var repo in p.Repos.Where(Directory.Exists))
            if (Detect(repo) is { } found) return found;
        return null;
    }

    /// <summary>A folder's dev server, read from its package.json.</summary>
    public static PreviewConfig? Detect(string folder)
    {
        var file = Path.Combine(folder, "package.json");
        if (!File.Exists(file)) return null;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(file));
            var scripts = json?["scripts"] as JsonObject;
            if (scripts?["dev"] is null) return null;
            var deps = string.Join(' ', new[] { "dependencies", "devDependencies" }
                .SelectMany(k => (json?[k] as JsonObject)?.Select(x => x.Key) ?? []));
            var dev = scripts["dev"]!.ToString();
            int port = Has("astro") ? 4321 : Has("next") ? 3000 : Has("@sveltejs/kit") || Has("vite") ? 5173 : 3000;
            return new PreviewConfig { Command = "npm run dev", Cwd = folder, Url = $"http://localhost:{port}/" };

            bool Has(string name) => deps.Split(' ').Contains(name) || dev.StartsWith(name.TrimStart('@').Split('/')[0], StringComparison.Ordinal);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The first local address in a line of server output (ANSI colours removed), as servers
    /// print them: "Local  http://localhost:4321/", "listening on http://127.0.0.1:5000", "[::1]:8080".</summary>
    public static string? FirstLocalUrl(string line)
    {
        var m = LocalUrl().Match(Ansi().Replace(line, ""));
        if (!m.Success) return null;
        var url = m.Value.TrimEnd('.', ',', ')', ';');
        return url.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost");
    }

    /// <summary>Whether anything answers at the address (any HTTP status counts: the server is up).</summary>
    public static async Task<bool> Answers(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return true;
        }
        catch (Exception) { return false; }
    }

    [GeneratedRegex(@"https?://(localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1?\])(:\d+)?(/[^\s""'<>]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex LocalUrl();
    [GeneratedRegex(@"\x1b\[[0-9;?]*[A-Za-z]")]
    private static partial Regex Ansi();
}

/// <summary>A dev server Workbench started: its output, and the address it said it serves on. Stopping
/// it ends the whole tree (npm starts node, which may start more).</summary>
public sealed class DevServer : IDisposable
{
    readonly Process _process;
    readonly ChildProcesses.ProcessGroup _group;
    readonly StringBuilder _log = new();
    public string? Url { get; private set; }
    public event Action<string>? Line;
    public event Action<string>? UrlFound;
    public event Action<int>? Exited;
    public bool Running => !_process.HasExited;
    public string Log { get { lock (_log) return _log.ToString(); } }

    public DevServer(string command, string cwd)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/s", "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        psi.WorkingDirectory = cwd;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true; // a server that reads keys (Vite's "press h") gets no console
        psi.StandardOutputEncoding = psi.StandardErrorEncoding = Encoding.UTF8;
        psi.Environment["BROWSER"] = "none";   // the preview is here; no browser tab as well
        psi.Environment["FORCE_COLOR"] = "0";
        psi.Environment["NO_COLOR"] = "1";
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);
        _process.Start();
        _group = ChildProcesses.Group(_process); // straight away, before cmd gets to start npm, so all of it is caught
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    void OnLine(string? line)
    {
        if (line is null) return;
        lock (_log)
        {
            _log.AppendLine(line);
            if (_log.Length > 200_000) _log.Remove(0, _log.Length - 150_000);
        }
        Line?.Invoke(line);
        if (Url is null && Preview.FirstLocalUrl(line) is { } url) { Url = url; UrlFound?.Invoke(url); }
    }

    public void Stop()
    {
        _group.Dispose();
    }

    public void Dispose() { Stop(); _process.Dispose(); }
}
