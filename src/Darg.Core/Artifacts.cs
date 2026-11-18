using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

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
        Path.Combine(ProjectData.Root(projectId, instanceFolder), "artifacts");

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
            foreach (var f in Directory.EnumerateFiles(dir))
                if (IsArtifact(f) && Read(projectId, f) is { } a) all.Add(a);
        return all.OrderByDescending(a => a.Modified).ToList();
    }

    /// <summary>The artifact a file is, if it sits in an agent's artifacts folder.</summary>
    public static Artifact? Read(string projectId, string file)
    {
        if (!File.Exists(file)) return null;
        var agent = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(file))) ?? "";
        return new Artifact(projectId, agent, Path.GetFullPath(file), Title(file), File.GetLastWriteTime(file));
    }

    /// <summary>The ending of an image review: rows of pictures that Darg shows as a picker of its own
    /// (<see cref="ArtifactServer.ReviewPage"/>), so an agent doesn't write one each time.</summary>
    public const string ReviewSuffix = ".review.json";

    /// <summary>An HTML page or an image review.</summary>
    public static bool IsArtifact(string file) =>
        file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || IsReview(file);

    public static bool IsReview(string file) => file.EndsWith(ReviewSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The page's &lt;title&gt; (a review's "title"), else its file name made readable ("art-review" → "art review").</summary>
    public static string Title(string file)
    {
        if (IsReview(file))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject o && o["title"] is JsonValue v && v.TryGetValue<string>(out var t) && t.Trim().Length > 0)
                    return Regex.Replace(t.Trim(), @"\s+", " ");
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
            var bare = Path.GetFileName(file);
            return bare[..^ReviewSuffix.Length].Replace('-', ' ').Replace('_', ' ');
        }
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
        if (!IsArtifact(full)) return null;
        var instance = instanceFolder ?? InstanceConfig.Folder();
        // a project with a repo keeps its artifacts in <repo>/.darg/artifacts/<agent>/<file>
        if (InstanceConfigOrNull(instance) is { } config)
            foreach (var p in config.Projects)
            {
                if (ProjectData.RepoOf(p.Id, instance) is null) continue;
                var own = Path.GetFullPath(Folder(p.Id, instance)) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(own, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = full[own.Length..].Split(Path.DirectorySeparatorChar);
                return rest.Length == 2 ? (p.Id, rest[0]) : null;
            }
        var projects = Path.GetFullPath(Path.Combine(instance, "projects")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(projects, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = full[projects.Length..].Split(Path.DirectorySeparatorChar);
        // <project>/artifacts/<agent>/<file>
        return parts.Length == 4 && parts[1].Equals("artifacts", StringComparison.OrdinalIgnoreCase) ? (parts[0], parts[2]) : null;
    }

    static InstanceConfig? InstanceConfigOrNull(string instance)
    {
        try { return File.Exists(InstanceConfig.FilePath(instance)) ? InstanceConfig.Load(instance) : null; }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }

    /// <summary>What an agent is told about artifacts (appended to its system prompt). Free of the characters
    /// a shell might take as syntax (angle brackets, double quotes, ampersands, pipes): it goes on a command line.</summary>
    public static string Briefing(string folder) =>
        "Artifacts in Darg: to show the person something richer than text (a review page, a picker, a comparison, " +
        $"a table, a chart), write a self-contained HTML file into {folder}, one file per artifact, with a clear title element. " +
        "Darg lists it under Artifacts and opens it in the app, and it never leaves this machine; don't use Claude's cloud artifacts. " +
        "Inline your CSS and JS (no CDNs unless the page is useless without one). Show a local image by giving img a src of /file/ followed by " +
        "its absolute path, for example /file/D:/art/card-12.png; only image files are served that way. " +
        "It is two-way: the page can call darg.send(data) (a string, or anything JSON can carry; it returns a promise of true " +
        "once delivered) and that arrives to you as a message starting From artifact. Use it for picks, approvals, ratings and comments: " +
        "give the page a clear Send button, show on the page that it was sent, and keep the page's own state so the person can send more than once. " +
        "To change an artifact, edit its file; the person reloads it. " +
        "For picking between images (art candidates, renders, variants) don't write a page: Darg has a picker of its own. " +
        $"Write a JSON file whose name ends in {ReviewSuffix} into that folder, holding an object with title, an optional note, " +
        "an optional folder (relative image paths are taken from it), and rows: a list of objects each with id, an optional note, " +
        "an optional original (the image in use now, shown first) and candidates (a list of image paths). " +
        "The person picks one image per row, or none for another pass, adds a note per row, and sends. The reply is one line per row: " +
        "the id, a colon, then the picked file's name, or original, or NEEDS PASS, then optionally a vertical bar and the note.";

    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

/// <summary>Something an artifact page sent back with darg.send().</summary>
public sealed record ArtifactReply(string Project, string Agent, string File, string Text);

/// <summary>Serves artifacts to the app's web view (and any browser on this machine) on localhost, with a
/// small darg.send() bridge put into each page, and raises <see cref="Reply"/> when a page sends.
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
    /// <summary>The Darg API's address, token included: POST to this plus /&lt;command&gt; runs one. Agents get it as DARG_API.</summary>
    public string ApiUrl => $"{Root}api/{_token}";
    public event Action<ArtifactReply>? Reply;
    /// <summary>Set by the app: the agents it runs, as JSON (GET /agents/&lt;token&gt;). Fettle reads it.</summary>
    public Func<Task<string>>? ListAgents { get; set; }
    /// <summary>Set by the app: close these agents' sessions and say what happened (POST /agents/&lt;token&gt;/close,
    /// the ids in the body). Fettle closes agents through it when Philip asks.</summary>
    public Func<IReadOnlyList<string>, Task<string>>? CloseAgents { get; set; }
    /// <summary>The Darg API the app registers its commands in (GET /api/&lt;token&gt; lists them,
    /// GET or POST /api/&lt;token&gt;/&lt;name&gt; runs one, its arguments as a JSON object in the body or the query string).</summary>
    public DargApi Api { get; } = new();

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
            else if (parts.Length is 2 or 3 && parts[0] == "api")
                await CallApi(ctx, parts[1], parts.Length == 3 ? parts[2] : null);
            else if (parts.Length is 2 or 3 && parts[0] == "agents")
                await Agents(ctx, parts[1], parts.Length == 3 ? parts[2] : null);
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
        var reply = $"/reply/{_token}/{Esc(project)}/{Esc(agent)}/{Esc(Path.GetRelativePath(dir, file).Replace('\\', '/'))}";
        if (ArtifactStore.IsReview(file))
            await Write(res, Encoding.UTF8.GetBytes(WithBridge(ReviewPage(await File.ReadAllTextAsync(file), ArtifactStore.Title(file)), reply)), "text/html; charset=utf-8");
        else if (Path.GetExtension(file).ToLowerInvariant() is ".html" or ".htm")
        {
            var html = WithBridge(await File.ReadAllTextAsync(file), reply);
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

    bool Allowed(string token) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_token));

    /// <summary>The agents: listed (GET), or the ids in the body closed (POST .../close). Needs the token.</summary>
    async Task Agents(HttpListenerContext ctx, string token, string? action)
    {
        var res = ctx.Response;
        if (!Allowed(token)) { res.StatusCode = 403; return; }
        var method = ctx.Request.HttpMethod;
        if (action is null && method == "GET")
        {
            if (ListAgents is null) { res.StatusCode = 503; return; }
            await Write(res, Encoding.UTF8.GetBytes(await ListAgents()), "application/json");
        }
        else if (action == "close" && method == "POST")
        {
            if (CloseAgents is null) { res.StatusCode = 503; return; }
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (body.Length > MaxReply) { res.StatusCode = 413; return; }
            await Write(res, Encoding.UTF8.GetBytes(await CloseAgents(Ids(body))), "text/plain; charset=utf-8");
        }
        else res.StatusCode = 404;
    }

    /// <summary>An API call: the command list (no name), or a command run with the body's JSON and the query string.</summary>
    async Task CallApi(HttpListenerContext ctx, string token, string? name)
    {
        var res = ctx.Response;
        if (!Allowed(token)) { res.StatusCode = 403; return; }
        if (name is null) { await Write(res, Encoding.UTF8.GetBytes(Api.Help()), "text/plain; charset=utf-8"); return; }
        var body = "";
        if (ctx.Request.HasEntityBody)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            body = await reader.ReadToEndAsync();
            if (body.Length > MaxReply) { res.StatusCode = 413; return; }
        }
        var q = ctx.Request.QueryString;
        var query = q.AllKeys.OfType<string>().Select(k => new KeyValuePair<string, string>(k, q[k] ?? ""));
        var (status, text) = await Api.Call(name, ApiArgs.Parse(body, query));
        res.StatusCode = status;
        var t = text.TrimStart();
        await Write(res, Encoding.UTF8.GetBytes(text), t.StartsWith('[') || t.StartsWith('{') ? "application/json" : "text/plain; charset=utf-8");
    }

    /// <summary>Agent ids out of a body however it's written: spaces, commas, lines or a JSON array.</summary>
    public static IReadOnlyList<string> Ids(string body) =>
        Regex.Matches(body, "[A-Za-z0-9_-]+").Select(m => m.Value).Distinct().ToList();

    async Task TakeReply(HttpListenerContext ctx, string token, string project, string agent, string file)
    {
        if (!Allowed(token)) { ctx.Response.StatusCode = 403; return; }
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
        var script = "<script>window.darg=window.workbench={send:function(d){return fetch(" + Js(replyPath) +
                     ",{method:'POST',headers:{'Content-Type':'text/plain'},body:typeof d==='string'?d:JSON.stringify(d,null,1)})" +
                     ".then(function(r){return r.ok},function(){return false})}};</script>";
        var head = Regex.Match(html, @"<head(\s[^>]*)?>", RegexOptions.IgnoreCase);
        return head.Success ? html.Insert(head.Index + head.Length, script) : script + html;
    }

    static readonly Lazy<string> ReviewTemplate = new(() =>
    {
        using var s = typeof(ArtifactServer).Assembly.GetManifestResourceStream("Darg.Core.Review.html")
                      ?? throw new InvalidOperationException("Review.html isn't embedded");
        return new StreamReader(s, Encoding.UTF8).ReadToEnd();
    });

    /// <summary>The picker page for an image review's JSON. JSON that doesn't read still gives a page saying why,
    /// so the person sees the fault rather than a blank.</summary>
    public static string ReviewPage(string json, string title)
    {
        string data = "null", error = "null";
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject o && (o["rows"] ?? o["jobs"]) is JsonArray) data = node.ToJsonString();
            else error = JsonSerializer.Serialize("This review has no rows: it needs an object with a rows list.");
        }
        catch (JsonException e) { error = JsonSerializer.Serialize("This review's JSON doesn't read: " + e.Message); }
        // the default encoder writes < > & as \u escapes, so nothing in the data can end the script element
        return ReviewTemplate.Value.Replace("__TITLE__", WebUtility.HtmlEncode(title)).Replace("__DATA__", data).Replace("__ERROR__", error);
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
