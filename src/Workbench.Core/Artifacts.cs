using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Workbench.Core;

/// <summary>An HTML page an agent made to show something richer than text (a review, a picker, a
/// comparison). Kept on this machine, never in anyone's cloud: one folder per agent under the project's
/// data, <c>&lt;instance&gt;/projects/&lt;project&gt;/artifacts/&lt;agent&gt;/&lt;name&gt;.html</c>.</summary>
public sealed record Artifact(string Project, string Agent, string Path, string Title, DateTime Modified)
{
    public string File => System.IO.Path.GetFileName(Path);
}

/// <summary>Where artifacts live, and reading them back.</summary>
public static class ArtifactStore
{
    /// <summary>A project's artifacts folder (one subfolder per agent).</summary>
    public static string Folder(string projectId, string? instanceFolder = null) =>
        Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "projects", Safe(projectId), "artifacts");

    /// <summary>The folder one agent writes its artifacts into (made if missing, so it can be --add-dir'd).</summary>
    public static string AgentFolder(string projectId, string agentId, string? instanceFolder = null)
    {
        var dir = Path.Combine(Folder(projectId, instanceFolder), Safe(agentId));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Every artifact in a project, newest first.</summary>
    public static List<Artifact> List(string projectId, string? instanceFolder = null)
    {
        var root = Folder(projectId, instanceFolder);
        var all = new List<Artifact>();
        if (!Directory.Exists(root)) return all;
        foreach (var dir in Directory.EnumerateDirectories(root))
            foreach (var f in Directory.EnumerateFiles(dir, "*.htm*"))
                if (Read(projectId, f) is { } a) all.Add(a);
        return all.OrderByDescending(a => a.Modified).ToList();
    }

    /// <summary>The artifact a file is, if it sits in an agent's artifacts folder.</summary>
    public static Artifact? Read(string projectId, string file)
    {
        if (!File.Exists(file)) return null;
        var agent = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(file))) ?? "";
        return new Artifact(projectId, agent, Path.GetFullPath(file), Title(file), File.GetLastWriteTime(file));
    }

    /// <summary>The page's &lt;title&gt;, else its file name made readable ("art-review" → "art review").</summary>
    public static string Title(string file)
    {
        try
        {
            using var r = new StreamReader(file);
            var head = new char[16384];
            var n = r.ReadBlock(head, 0, head.Length);
            var m = Regex.Match(new string(head, 0, n), @"<title[^>]*>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success && m.Groups[1].Value.Length > 0) return WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\s+", " "));
        }
        catch (IOException) { }
        return Path.GetFileNameWithoutExtension(file).Replace('-', ' ').Replace('_', ' ');
    }

    /// <summary>Whether a path is an artifact (an .html file inside a project's artifacts folder):
    /// returns (project, agent) when it is.</summary>
    public static (string Project, string Agent)? Owner(string path, string? instanceFolder = null)
    {
        string full;
        try { full = Path.GetFullPath(path); } catch (Exception) { return null; }
        if (!full.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !full.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)) return null;
        var projects = Path.GetFullPath(Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "projects")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(projects, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = full[projects.Length..].Split(Path.DirectorySeparatorChar);
        // <project>/artifacts/<agent>/<file>
        return parts.Length == 4 && parts[1].Equals("artifacts", StringComparison.OrdinalIgnoreCase) ? (parts[0], parts[2]) : null;
    }

    /// <summary>What an agent is told about artifacts (appended to its system prompt). Free of the characters
    /// a shell might take as syntax (angle brackets, double quotes, ampersands, pipes): it goes on a command line.</summary>
    public static string Briefing(string folder) =>
        "Artifacts in Workbench: to show the person something richer than text (a review page, a picker, a comparison, " +
        $"a table, a chart), write a self-contained HTML file into {folder}, one file per artifact, with a clear title element. " +
        "Workbench lists it under Artifacts and opens it in the app, and it never leaves this machine; don't use Claude's cloud artifacts. " +
        "Inline your CSS and JS (no CDNs unless the page is useless without one). Show a local image by giving img a src of /file/ followed by " +
        "its absolute path, for example /file/D:/art/card-12.png; only image files are served that way. " +
        "It is two-way: the page can call workbench.send(data) (a string, or anything JSON can carry; it returns a promise of true " +
        "once delivered) and that arrives to you as a message starting From artifact. Use it for picks, approvals, ratings and comments: " +
        "give the page a clear Send button, show on the page that it was sent, and keep the page's own state so the person can send more than once. " +
        "To change an artifact, edit its file; the person reloads it.";

    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

/// <summary>Something an artifact page sent back with workbench.send().</summary>
public sealed record ArtifactReply(string Project, string Agent, string File, string Text);

/// <summary>Serves artifacts to the app's web view (and any browser on this machine) on localhost, with a
/// small workbench.send() bridge put into each page, and raises <see cref="Reply"/> when a page sends.
/// Routes: /a/&lt;project&gt;/&lt;agent&gt;/&lt;file&gt; (the artifact, and files beside it),
/// /file/&lt;absolute path&gt; (images only), POST /reply/&lt;token&gt;/&lt;project&gt;/&lt;agent&gt;/&lt;file&gt;.
/// The token is only in pages this server hands out, so another site can't post into an agent.</summary>
public sealed class ArtifactServer : IDisposable
{
    public const int PreferredPort = 47817;
    const int MaxReply = 1 << 20;
    static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".avif" };

    readonly HttpListener _http = new();
    readonly string _instance;
    readonly string _token;
    readonly CancellationTokenSource _stop = new();

    public int Port { get; }
    public string Root => $"http://localhost:{Port}/";
    public event Action<ArtifactReply>? Reply;

    ArtifactServer(string instance, int port, string token)
    {
        _instance = instance; Port = port; _token = token;
        _http.Prefixes.Add(Root);
        _http.Start();
        _ = Task.Run(Loop);
    }

    /// <summary>Start serving an instance's artifacts: on the usual port if it's free (so a page left open in
    /// a browser still works after a restart), else on any free one.</summary>
    public static ArtifactServer Start(string? instanceFolder = null, int port = PreferredPort)
    {
        var instance = instanceFolder ?? InstanceConfig.Folder();
        var token = Token(instance);
        try { return new ArtifactServer(instance, port, token); }
        catch (HttpListenerException) { return new ArtifactServer(instance, FreePort(), token); }
    }

    /// <summary>The address a browser or the web view opens an artifact at.</summary>
    public string Url(Artifact a) => $"{Root}a/{Uri.EscapeDataString(a.Project)}/{Uri.EscapeDataString(a.Agent)}/{Uri.EscapeDataString(a.File)}";

    /// <summary>The instance's reply token: kept in the instance folder so open pages keep working across restarts.</summary>
    static string Token(string instance)
    {
        var file = Path.Combine(instance, "artifact-token");
        try { if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: >= 32 } t) return t; } catch (IOException) { }
        var fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        try { Directory.CreateDirectory(instance); File.WriteAllText(file, fresh); } catch (IOException) { }
        return fresh;
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync(); }
            catch (Exception) { if (_stop.IsCancellationRequested || !_http.IsListening) return; continue; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    async Task Handle(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            var parts = ctx.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
            if (parts.Length >= 4 && parts[0] == "a" && ctx.Request.HttpMethod == "GET")
                await ServeArtifact(res, parts[1], parts[2], string.Join('/', parts[3..]));
            else if (parts.Length >= 2 && parts[0] == "file" && ctx.Request.HttpMethod == "GET")
                await ServeImage(res, string.Join('/', parts[1..]));
            else if (parts.Length == 5 && parts[0] == "reply" && ctx.Request.HttpMethod == "POST")
                await TakeReply(ctx, parts[1], parts[2], parts[3], parts[4]);
            else res.StatusCode = 404;
        }
        catch (Exception) { try { res.StatusCode = 500; } catch (Exception) { } }
        finally { try { res.Close(); } catch (Exception) { } }
    }

    async Task ServeArtifact(HttpListenerResponse res, string project, string agent, string rel)
    {
        var dir = Path.GetFullPath(Path.Combine(ArtifactStore.Folder(project, _instance), agent)) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(dir, rel));
        // nothing outside that agent's folder, however the path is spelled
        if (!file.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) { res.StatusCode = 404; return; }
        res.Headers["Cache-Control"] = "no-store";
        if (Path.GetExtension(file).ToLowerInvariant() is ".html" or ".htm")
        {
            var html = WithBridge(await File.ReadAllTextAsync(file), $"/reply/{_token}/{Esc(project)}/{Esc(agent)}/{Esc(Path.GetRelativePath(dir, file).Replace('\\', '/'))}");
            await Write(res, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8");
        }
        else await Write(res, await File.ReadAllBytesAsync(file), ContentType(file));
    }

    async Task ServeImage(HttpListenerResponse res, string path)
    {
        // "/file/D:/x.png" arrives as "D:/x.png"; "/file/home/me/x.png" as "home/me/x.png" (put the root back)
        if (!OperatingSystem.IsWindows() && !path.StartsWith('/')) path = "/" + path;
        string full;
        try { full = Path.GetFullPath(path); } catch (Exception) { res.StatusCode = 404; return; }
        if (!Path.IsPathRooted(path) || !ImageExts.Contains(Path.GetExtension(full)) || !File.Exists(full)) { res.StatusCode = 404; return; }
        await Write(res, await File.ReadAllBytesAsync(full), ContentType(full));
    }

    async Task TakeReply(HttpListenerContext ctx, string token, string project, string agent, string file)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_token)))
        { ctx.Response.StatusCode = 403; return; }
        var buf = new byte[MaxReply + 1];
        int n = 0, r;
        while (n <= MaxReply && (r = await ctx.Request.InputStream.ReadAsync(buf.AsMemory(n, buf.Length - n))) > 0) n += r;
        if (n > MaxReply) { ctx.Response.StatusCode = 413; return; }
        var text = Encoding.UTF8.GetString(buf, 0, n);
        if (Reply is null) { ctx.Response.StatusCode = 503; return; }
        Reply(new ArtifactReply(project, agent, file, text));
        ctx.Response.StatusCode = 204;
    }

    /// <summary>The page with the bridge put in: inside &lt;head&gt; if it has one, else first.</summary>
    public static string WithBridge(string html, string replyPath)
    {
        var script = "<script>window.workbench={send:function(d){return fetch(" + Js(replyPath) +
                     ",{method:'POST',headers:{'Content-Type':'text/plain'},body:typeof d==='string'?d:JSON.stringify(d,null,1)})" +
                     ".then(function(r){return r.ok},function(){return false})}};</script>";
        var head = Regex.Match(html, @"<head(\s[^>]*)?>", RegexOptions.IgnoreCase);
        return head.Success ? html.Insert(head.Index + head.Length, script) : script + html;
    }

    static string Js(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    static string Esc(string s) => string.Join('/', s.Split('/').Select(Uri.EscapeDataString));

    static async Task Write(HttpListenerResponse res, byte[] body, string type)
    {
        res.ContentType = type;
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
    }

    static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8", ".css" => "text/css", ".js" => "text/javascript", ".json" => "application/json",
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml", ".avif" => "image/avif", ".txt" or ".md" => "text/plain; charset=utf-8", _ => "application/octet-stream",
    };

    public void Dispose()
    {
        _stop.Cancel();
        try { _http.Stop(); _http.Close(); } catch (Exception) { }
    }
}
