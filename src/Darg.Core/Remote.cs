using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Darg.Core;

/// <summary>Phone access to the Darg API (AND-0002): what a paired phone may call, and the pieces of pairing.
///
/// How it fits together: <see cref="RemoteServer"/> listens on every network the PC is on (port 47818), over TLS with a
/// self-signed certificate of the instance's own, so it works the same on the home Wi-Fi and over Tailscale (whose
/// 100.x address it offers too) with no relay in between. Pairing: Settings (or the phone-pair command) starts a
/// one-time code that lasts 10 minutes and dies after 5 wrong tries, shown as a QR of <see cref="PairUri"/>: the
/// addresses, the port, the code and the certificate's SHA-256 fingerprint, which the phone pins, so nobody between can
/// stand in for the PC. The phone POSTs the code to /pair and gets a token of its own; every later call carries it as
/// "Authorization: Bearer". Only the token's hash is kept (remote-devices.json in the instance folder), and revoking a
/// device (Settings, or phone-revoke) refuses its next call. A request without a good token gets a 401.</summary>
public static class RemoteAccess
{
    public const int PreferredPort = 47818;
    public static readonly TimeSpan PairingLasts = TimeSpan.FromMinutes(10);
    public const int WrongTries = 5;

    /// <summary>The Darg API commands a phone may call: reading projects, cards, agents, notes, docs and reports, and the things worth doing
    /// from the sofa (answer a decision, send a card back, message an agent, start work on a card, jot a card down).
    /// Left out: what drives the PC's own screen (show, where, theme), what runs open-ended work from free text
    /// (start-agent, run-command, flows' new-flow and run-flow), bulk starts, forgetting agents, and the phone-*
    /// commands, so a phone can never pair another.</summary>
    public static readonly IReadOnlySet<string> Commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "projects", "overview", "agents", "agent", "cards", "card", "flows",
        "notes", "docs", "doc", "reports", "report", // AND-0008's read-only views
        "boards", "move-card", // AND-0004's board
        "fettle-say", "fettle", "fettle-answer", "fettle-chats", // AND-0007's Fettle from the phone
        "say", "decide", "send-back", "new-card", "work-on", "close-agents",
    };

    /// <summary>A device token: 32 random bytes, URL-safe base64.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What's kept of a token: its SHA-256, hex.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O or 1/I: it may be typed in

    /// <summary>A pairing code: 8 characters (40 bits), shown as ABCD-EFGH.</summary>
    public static string NewCode() => new(Enumerable.Range(0, 8).Select(_ => CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)]).ToArray());

    /// <summary>A code as typed (any case, with or without the dash or spaces) in the form it's kept.</summary>
    public static string NormalCode(string? code) => new((code ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>What the QR holds: darg://pair?v=1&amp;name=&amp;hosts=a,b&amp;port=&amp;code=&amp;fp=.</summary>
    public static string PairUri(string name, IEnumerable<string> hosts, int port, string code, string fingerprint) =>
        $"darg://pair?v=1&name={Uri.EscapeDataString(name)}&hosts={Uri.EscapeDataString(string.Join(',', hosts))}&port={port}&code={code}&fp={fingerprint}";

    /// <summary>The PC's IPv4 addresses a phone could reach: the home network's first (an adapter with a gateway, which
    /// leaves out WSL's and Hyper-V's virtual ones), then Tailscale's (100.64.0.0/10, which has none).</summary>
    public static IReadOnlyList<string> Hosts()
    {
        var found = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = nic.GetIPProperties();
                var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var a in props.UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && (gateway || IsTailscale(a.Address)))
                        found.Add(a.Address);
            }
        }
        catch (NetworkInformationException) { }
        return found.Distinct().OrderBy(a => IsTailscale(a) ? 1 : 0).Select(a => a.ToString()).ToList();
    }

    public static bool IsTailscale(IPAddress a) { var b = a.GetAddressBytes(); return b.Length == 4 && b[0] == 100 && (b[1] & 0xC0) == 64; }
}

/// <summary>A paired phone: its name, the hash of its token, when it was paired and last seen.</summary>
public sealed class RemoteDevice
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("paired")] public DateTime Paired { get; set; }
    [JsonPropertyName("lastSeen")] public DateTime? LastSeen { get; set; }
}

/// <summary>The paired phones of an instance, in remote-devices.json beside instance.json. Read on every call, so
/// a revoke (or a hand edit) takes effect on the next request.</summary>
public sealed class RemoteDevices(string instanceFolder)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static readonly Lock Gate = new();

    public string FilePath { get; } = Path.Combine(instanceFolder, "remote-devices.json");

    public IReadOnlyList<RemoteDevice> All()
    {
        lock (Gate) return Read();
    }

    List<RemoteDevice> Read()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<RemoteDevice>>(File.ReadAllText(FilePath), Json) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    void Write(List<RemoteDevice> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Json));
    }

    /// <summary>Pair a device: its record, and the token to hand it (never kept).</summary>
    public (RemoteDevice Device, string Token) Add(string? name, DateTime? now = null)
    {
        var token = RemoteAccess.NewToken();
        var device = new RemoteDevice
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Name = string.IsNullOrWhiteSpace(name) ? "Phone" : name.Trim()[..Math.Min(name.Trim().Length, 60)],
            Hash = RemoteAccess.Hash(token), Paired = now ?? DateTime.Now,
        };
        lock (Gate) { var list = Read(); list.Add(device); Write(list); }
        return (device, token);
    }

    /// <summary>Unpair devices by id or name; the ones removed.</summary>
    public IReadOnlyList<RemoteDevice> Revoke(string idOrName)
    {
        lock (Gate)
        {
            var list = Read();
            var gone = list.Where(d => d.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || d.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (gone.Count > 0) { list.RemoveAll(gone.Contains); Write(list); }
            return gone;
        }
    }

    /// <summary>The device a token belongs to, or null; marks it seen (written at most once a minute).</summary>
    public RemoteDevice? Check(string? token, DateTime? now = null)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var hash = Encoding.ASCII.GetBytes(RemoteAccess.Hash(token));
        lock (Gate)
        {
            var list = Read();
            var device = list.FirstOrDefault(d => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(d.Hash), hash));
            if (device is null) return null;
            var t = now ?? DateTime.Now;
            if (device.LastSeen is not { } seen || t - seen > TimeSpan.FromMinutes(1))
            {
                device.LastSeen = t;
                try { Write(list); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return device;
        }
    }
}

/// <summary>The one pairing in progress, if any: a code that lasts <see cref="RemoteAccess.PairingLasts"/>, works
/// once, and is thrown away after <see cref="RemoteAccess.WrongTries"/> wrong tries.</summary>
public sealed class RemotePairing
{
    readonly Lock _gate = new();
    string? _code;
    DateTime _expires;
    int _wrong;

    public enum Result { Paired, Wrong, NoPairing }

    /// <summary>Raised when a pairing starts, ends (paired, ran out, too many wrong tries) or is cancelled.</summary>
    public event Action? Changed;

    public string? Code { get { lock (_gate) return Live(DateTime.Now) ? _code : null; } }
    public DateTime Expires { get { lock (_gate) return _expires; } }

    bool Live(DateTime now) => _code is not null && now < _expires;

    public string Begin(DateTime? now = null)
    {
        var code = RemoteAccess.NewCode();
        lock (_gate) { _code = code; _expires = (now ?? DateTime.Now) + RemoteAccess.PairingLasts; _wrong = 0; }
        Changed?.Invoke();
        return code;
    }

    public void Cancel()
    {
        lock (_gate) _code = null;
        Changed?.Invoke();
    }

    /// <summary>Try a code: Paired uses the pairing up; Wrong counts against it.</summary>
    public Result Redeem(string? code, DateTime? now = null)
    {
        Result r;
        bool ended;
        lock (_gate)
        {
            if (!Live(now ?? DateTime.Now)) { _code = null; return Result.NoPairing; }
            var given = Encoding.ASCII.GetBytes(RemoteAccess.NormalCode(code));
            if (CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(_code!))) { _code = null; r = Result.Paired; }
            else { if (++_wrong >= RemoteAccess.WrongTries) _code = null; r = Result.Wrong; }
            ended = _code is null;
        }
        if (ended) Changed?.Invoke();
        return r;
    }
}

/// <summary>The instance's TLS certificate for phone access: self-signed, made once and kept in remote-cert.pfx in
/// the instance folder, so a paired phone's pinned fingerprint stays good across restarts.</summary>
public static class RemoteCert
{
    public static X509Certificate2 LoadOrCreate(string instanceFolder)
    {
        var file = Path.Combine(instanceFolder, "remote-cert.pfx");
        if (File.Exists(file))
            try { return X509CertificateLoader.LoadPkcs12FromFile(file, null); }
            catch (CryptographicException) { }
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN=Darg on {Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)); // server auth
        using var made = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        var pfx = made.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(instanceFolder);
        File.WriteAllBytes(file, pfx);
        // loaded back from the PFX: Schannel won't serve with the key CreateSelfSigned leaves in memory
        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    /// <summary>The SHA-256 of the certificate, hex, lower case: what the phone pins.</summary>
    public static string Fingerprint(X509Certificate2 cert) => cert.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
}

/// <summary>Phone access to the Darg API (AND-0002; see <see cref="RemoteAccess"/>): a small HTTPS server on every
/// network the PC is on. GET /hello says it's Darg (no token needed); POST /pair {code, name} trades a pairing code for
/// a device token; GET /api lists the commands a phone may call and GET or POST /api/&lt;name&gt; runs one, both with
/// "Authorization: Bearer &lt;token&gt;". Anything else without a good token is a 401. One request per connection.</summary>
public sealed class RemoteServer : IDisposable
{
    const int MaxHead = 16 * 1024;
    const int MaxBody = 1 << 20;
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    readonly TcpListener _listener;
    readonly X509Certificate2 _cert;
    readonly DargApi _api;
    readonly Func<string> _instance;
    readonly CancellationTokenSource _stop = new();

    public int Port { get; }
    public string Fingerprint { get; }
    public RemotePairing Pairing { get; } = new();
    /// <summary>The instance's paired devices (read from the instance folder each time).</summary>
    public RemoteDevices Devices => new(_instance());
    /// <summary>Raised (off the UI thread) when a phone pairs.</summary>
    public event Action<RemoteDevice>? Paired;

    RemoteServer(DargApi api, X509Certificate2 cert, Func<string> instance, int port)
    {
        _api = api; _cert = cert; _instance = instance;
        Fingerprint = RemoteCert.Fingerprint(cert);
        _listener = new TcpListener(IPAddress.IPv6Any, port);
        _listener.Server.DualMode = true;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(Loop);
    }

    /// <summary>Start listening: on <paramref name="port"/> if it's free (so paired phones find it), else any free one.
    /// <paramref name="instance"/> gives the folder the devices are kept in; the certificate is kept in its folder now.</summary>
    public static RemoteServer Start(DargApi api, Func<string>? instance = null, int port = RemoteAccess.PreferredPort)
    {
        instance ??= () => InstanceConfig.Folder();
        var cert = RemoteCert.LoadOrCreate(instance());
        try { return new RemoteServer(api, cert, instance, port); }
        catch (SocketException) when (port != 0) { return new RemoteServer(api, cert, instance, 0); }
    }

    /// <summary>The QR's text for the pairing in progress (null when none).</summary>
    public string? PairUri(string? name = null) =>
        Pairing.Code is { } code ? RemoteAccess.PairUri(name ?? Environment.MachineName, RemoteAccess.Hosts(), Port, code, Fingerprint) : null;

    async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { if (_stop.IsCancellationRequested) return; continue; }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using var _ = client;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        cts.CancelAfter(RequestTimeout);
        try
        {
            await using var ssl = new SslStream(client.GetStream(), false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _cert, ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, cts.Token);
            var (status, type, body) = await ReadAndHandle(ssl, cts.Token);
            await Respond(ssl, status, type, body, cts.Token);
        }
        catch (Exception) { } // a bad handshake, a timeout or a dropped connection: nothing to answer
    }

    async Task<(int, string, string)> ReadAndHandle(Stream s, CancellationToken ct)
    {
        var req = await HttpRequest.Read(s, MaxHead, MaxBody, ct);
        if (req is null) return (400, Text, "Bad request.");
        if (req.Status is { } refused) return (refused, Text, "Request refused.");
        return await Handle(req);
    }

    const string Text = "text/plain; charset=utf-8";
    const string JsonType = "application/json";

    /// <summary>A request, already read: (status, content type, body).</summary>
    internal async Task<(int Status, string Type, string Body)> Handle(HttpRequest req)
    {
        var parts = req.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts is ["hello"] && req.Method == "GET")
            return (200, JsonType, new JsonObject { ["app"] = "darg", ["pairing"] = Pairing.Code is not null }.ToJsonString());
        if (parts is ["pair"] && req.Method == "POST") return Pair(req);

        var device = req.Headers.TryGetValue("authorization", out var auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? Devices.Check(auth["Bearer ".Length..].Trim()) : null;
        if (device is null) return (401, Text, "Not paired: pair this device from Darg's Settings first.");

        if (parts is ["api"] && req.Method == "GET")
        {
            var help = string.Join('\n', _api.Help().Split('\n').Where(l => l.IndexOf(':') is > 0 and var i && RemoteAccess.Commands.Contains(l[..i])));
            return (200, Text, help + "\n");
        }
        if (parts is ["api", var name] && req.Method is "GET" or "POST")
        {
            if (!RemoteAccess.Commands.Contains(name)) return (403, Text, $"\"{name}\" can't be called from a phone. GET /api lists what can.");
            var (status, text) = await _api.Call(name, ApiArgs.Parse(req.Body, req.Query));
            var t = text.TrimStart();
            return (status, t.StartsWith('[') || t.StartsWith('{') ? JsonType : Text, text);
        }
        return (404, Text, "Nothing here.");
    }

    (int, string, string) Pair(HttpRequest req)
    {
        var args = ApiArgs.Parse(req.Body, req.Query);
        switch (Pairing.Redeem(args.Get("code")))
        {
            case RemotePairing.Result.NoPairing: return (403, Text, "No pairing is open: start one in Darg's Settings.");
            case RemotePairing.Result.Wrong: return (403, Text, "Wrong code.");
        }
        var (device, token) = Devices.Add(args.Get("name"));
        Paired?.Invoke(device);
        var o = new JsonObject
        {
            ["token"] = token, ["device"] = device.Id, ["pc"] = Environment.MachineName,
            ["commands"] = new JsonArray(RemoteAccess.Commands.Order(StringComparer.Ordinal).Select(c => (JsonNode?)c).ToArray()),
        };
        return (200, JsonType, o.ToJsonString());
    }

    static async Task Respond(Stream s, int status, string type, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 411 => "Length Required", 413 => "Payload Too Large", 431 => "Request Header Fields Too Large", _ => "Error" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n" +
                   (status == 401 ? "WWW-Authenticate: Bearer\r\n" : "") + "\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await s.WriteAsync(bytes, ct);
        await s.FlushAsync(ct);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); } catch (Exception) { }
        _cert.Dispose();
    }
}

/// <summary>An HTTP/1.1 request as <see cref="RemoteServer"/> takes it: the request line, headers (names lower case)
/// and a Content-Length body. Status is set (and the rest empty) when it's refused: too big, or chunked.</summary>
internal sealed class HttpRequest
{
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public List<KeyValuePair<string, string>> Query { get; init; } = [];
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; init; } = "";
    public int? Status { get; init; }

    /// <summary>Read one request; null when the stream ends first or the head doesn't parse.</summary>
    public static async Task<HttpRequest?> Read(Stream s, int maxHead, int maxBody, CancellationToken ct)
    {
        var buf = new byte[maxHead];
        int n = 0, end = -1;
        while (end < 0)
        {
            if (n == buf.Length) return new HttpRequest { Status = 431 };
            var r = await s.ReadAsync(buf.AsMemory(n), ct);
            if (r == 0) return null;
            n += r;
            end = IndexOf(buf, n, "\r\n\r\n"u8);
        }
        var lines = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || !first[2].StartsWith("HTTP/1.")) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
            if (line.IndexOf(':') is > 0 and var i) headers[line[..i].Trim()] = line[(i + 1)..].Trim();
        if (headers.ContainsKey("transfer-encoding")) return new HttpRequest { Status = 411 };
        var length = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var len) ? len : 0;
        if (length < 0 || length > maxBody) return new HttpRequest { Status = 413 };
        var body = new byte[length];
        var have = Math.Min(n - (end + 4), length);
        Array.Copy(buf, end + 4, body, 0, have);
        while (have < length)
        {
            var r = await s.ReadAsync(body.AsMemory(have), ct);
            if (r == 0) return null;
            have += r;
        }
        var target = first[1];
        var q = target.IndexOf('?');
        var query = new List<KeyValuePair<string, string>>();
        if (q >= 0)
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                string Dec(string x) => Uri.UnescapeDataString(x.Replace('+', ' '));
                query.Add(eq < 0 ? new(Dec(pair), "") : new(Dec(pair[..eq]), Dec(pair[(eq + 1)..])));
            }
        return new HttpRequest
        {
            Method = first[0].ToUpperInvariant(), Path = q >= 0 ? target[..q] : target, Query = query, Headers = headers,
            Body = Encoding.UTF8.GetString(body),
        };
    }

    static int IndexOf(byte[] buf, int n, ReadOnlySpan<byte> what) => buf.AsSpan(0, n).IndexOf(what);
}
