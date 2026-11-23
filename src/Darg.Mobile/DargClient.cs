using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Darg.Mobile;

/// <summary>A project as the desktop's <c>projects</c> command lists it: what the phone needs to show and open it.</summary>
public sealed record ProjectInfo(string Id, string Name, IReadOnlyList<string> Sections, string? Board, bool OnScreen)
{
    public string Summary => Board is { Length: > 0 } b ? $"{b} board" : "no board";
}

/// <summary>A call to the desktop that didn't work, with a message fit to show on the phone as it is.</summary>
public sealed class DargClientException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The desktop answered 401: this phone's token is no good (revoked); only pairing again helps.</summary>
    public bool Unpaired { get; init; }

    /// <summary>The desktop didn't answer at all (off, asleep, out of reach), as against answering with a refusal (AND-0009).</summary>
    public bool Unreachable { get; init; }
}

/// <summary>Talks to the desktop app's Darg API (<c>POST /api/&lt;token&gt;/&lt;command&gt;</c>): every read and action
/// the phone makes goes through it, the same code the desktop's buttons run (docs/ANDROID.md).</summary>
public sealed class DargClient(Connection connection, HttpMessageHandler? handler = null) : IDisposable
{
    // a paired connection talks only to the certificate it pinned (AND-0011), unless a test hands in a handler
    readonly HttpClient _http = Make(connection, handler);

    public Connection Connection { get; } = connection;

    /// <summary>Where the desktop's last answers to reads are kept, so they can stand in when it's off (AND-0009); the
    /// shell sets it on every client it makes. Null keeps nothing.</summary>
    public AnswerCache? Cache { get; set; }

    /// <summary>True from the first call the desktop didn't answer until one it does (AND-0009). Meanwhile a read with a
    /// kept answer gets that at once, and goes to the PC in the background at most every <see cref="ProbeEvery"/> to
    /// see if it's back; a read with nothing kept and anything that does something still go to the PC, and fail.</summary>
    public bool Offline { get; private set; }

    /// <summary>When the newest kept answer given out while offline was read (null when none was): what the banner says
    /// the screen's data is from.</summary>
    public DateTimeOffset? ShowingFrom { get; private set; }

    /// <summary>Raised when <see cref="Offline"/> or <see cref="ShowingFrom"/> changes, on whatever thread the call ended on.</summary>
    public event Action? OfflineChanged;

    /// <summary>How often a read made while offline also tries the PC.</summary>
    public static TimeSpan ProbeEvery { get; set; } = TimeSpan.FromSeconds(5);

    DateTimeOffset _lastTry;
    int _probing;

    /// <summary>A JSON body with its length known: the phone server (RemoteServer) wants a Content-Length and refuses a chunked body with a 411.</summary>
    internal static StringContent Body(object args) => new(System.Text.Json.JsonSerializer.Serialize(args), System.Text.Encoding.UTF8, "application/json");

    static HttpClient Make(Connection c, HttpMessageHandler? handler)
    {
        var http = new HttpClient(handler ?? (c.IsPinned ? PinnedTls.Handler(c.Fingerprint) : new HttpClientHandler())) { Timeout = TimeSpan.FromSeconds(10) };
        if (c.IsPinned) http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", c.Token);
        return http;
    }

    /// <summary>Runs a command with its arguments as a JSON object and gives back what it answered. With the PC off, a
    /// read answers what it answered last time, if it's kept (see <see cref="Offline"/>).</summary>
    public async Task<JsonNode?> Call(string command, object? args = null, CancellationToken ct = default)
    {
        var read = Cache is not null && AnswerCache.Reads.Contains(command);
        if (read && Offline && Cache!.Get(Connection.Address, command, args) is { } kept)
        {
            // the kept answer now, and the PC tried behind it now and then: one that answers ends offline
            if (DateTimeOffset.UtcNow - _lastTry >= ProbeEvery && Interlocked.Exchange(ref _probing, 1) == 0)
                _ = Task.Run(async () =>
                {
                    try { await Send(command, args, true, CancellationToken.None); }
                    catch (DargClientException) { }
                    finally { Volatile.Write(ref _probing, 0); }
                });
            Showing(kept.At);
            return kept.Answer;
        }
        try { return await Send(command, args, read, ct); }
        catch (DargClientException e) when (e.Unreachable && read && Cache!.Get(Connection.Address, command, args) is { } last)
        {
            Showing(last.At);
            return last.Answer;
        }
        catch (DargClientException e) when (e.Unreachable && !read && !AnswerCache.Reads.Contains(command))
        {
            throw new DargClientException($"Nothing was sent: {e.Message}", e.InnerException) { Unreachable = true };
        }
    }

    void Showing(DateTimeOffset at)
    {
        if (ShowingFrom is { } now && now >= at) return;
        ShowingFrom = at;
        OfflineChanged?.Invoke();
    }

    void SetOffline(bool offline)
    {
        if (Offline == offline) return;
        Offline = offline;
        if (!offline) ShowingFrom = null;
        OfflineChanged?.Invoke();
    }

    /// <summary>Tries the PC now with a cheap read, whatever <see cref="ProbeEvery"/> says: the banner's Try again.
    /// True when it answered.</summary>
    public async Task<bool> TryAgain(CancellationToken ct = default)
    {
        try { await Send("projects", null, Cache is not null, ct); return true; }
        catch (DargClientException e) { return !e.Unreachable; }
    }

    async Task<JsonNode?> Send(string command, object? args, bool keep, CancellationToken ct)
    {
        _lastTry = DateTimeOffset.UtcNow;
        try
        {
            var body = await Post(command, args, ct);
            SetOffline(false);
            if (keep) Cache?.Put(Connection.Address, command, args, body);
            return AnswerCache.Parse(body);
        }
        catch (DargClientException e) { SetOffline(e.Unreachable); throw; } // a refusal is an answer: the PC is there
    }

    /// <summary>One call to the desktop: its answer's text, or why not.</summary>
    async Task<string> Post(string command, object? args, CancellationToken ct)
    {
        HttpResponseMessage res;
        try { res = await _http.PostAsync($"{Connection.ApiBase}/{command}", Body(args ?? new { }), ct); }
        catch (HttpRequestException e) when (Connection.IsPinned && PinnedTls.IsCertificateFailure(e))
        {
            throw new DargClientException($"{Connection.Address} showed a different certificate from the one this phone paired with, so nothing was sent. If you changed PC or reinstalled Darg, pair again; otherwise someone on this network is pretending to be it.", e);
        }
        catch (HttpRequestException e) { throw new DargClientException(Unreachable(e), e) { Unreachable = true }; }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new DargClientException($"{Connection.Address} didn't answer in {_http.Timeout.TotalSeconds:0} seconds.", e) { Unreachable = true };
        }
        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode is HttpStatusCode.Unauthorized && Connection.IsPinned)
                throw new DargClientException("Darg no longer knows this phone: it was revoked in Settings, or the pairing was undone. Pair again from the Settings QR.") { Unpaired = true };
            if (res.StatusCode is HttpStatusCode.Forbidden && Connection.IsPinned)
                throw new DargClientException($"A phone isn't allowed to call {command}.");
            if (res.StatusCode is HttpStatusCode.Forbidden)
                throw new DargClientException("Darg refused the token. Copy it again from the desktop (over USB, tools/android.ps1 -NoBuild on the PC does it).");
            if (res.StatusCode is HttpStatusCode.NotFound)
                throw new DargClientException($"{Connection.Address} answered, but not as Darg does (no {command} command there). Is that the right address?");
            if (!res.IsSuccessStatusCode)
                throw new DargClientException(body.Length > 0 ? body : $"Darg answered {(int)res.StatusCode} {res.ReasonPhrase}.");
            // some commands answer in words, not JSON (say: "Sent to …", AND-0005): Parse gives that back as a string value
            return body;
        }
    }

    /// <summary>The desktop's projects, in its tab order.</summary>
    public async Task<IReadOnlyList<ProjectInfo>> Projects(CancellationToken ct = default)
    {
        var list = new List<ProjectInfo>();
        if (await Call("projects", null, ct) is not JsonArray arr) throw new DargClientException("Darg's project list didn't come back as a list.");
        foreach (var p in arr.OfType<JsonObject>())
        {
            var sections = p["sections"] is JsonArray s ? s.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList() : [];
            list.Add(new ProjectInfo(
                p["id"]?.GetValue<string>() ?? "", p["name"]?.GetValue<string>() ?? p["id"]?.GetValue<string>() ?? "?",
                sections, p["board"]?.GetValue<string>(), p["onScreen"]?.GetValue<bool>() ?? false));
        }
        return list;
    }

    string Unreachable(HttpRequestException e)
    {
        var refused = e.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused } || e.Message.Contains("refused", StringComparison.OrdinalIgnoreCase);
        var local = Uri.TryCreate(Connection.Address, UriKind.Absolute, out var u) && u.IsLoopback;
        return refused && local
            ? $"Nothing answered at {Connection.Address}. On USB, run tools/android.ps1 -NoBuild on the PC (it sets up adb reverse; a re-plugged cable loses it), and check Darg is running there."
            : $"Can't reach {Connection.Address}: {e.InnerException?.Message ?? e.Message}. Check Darg is running on the PC and the phone can see it.";
    }

    public void Dispose() => _http.Dispose();
}
