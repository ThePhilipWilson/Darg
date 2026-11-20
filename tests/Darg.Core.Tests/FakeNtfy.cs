using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Darg.Core.Tests;

/// <summary>An ntfy server for tests (AND-0006), shaped as ntfy.sh answered on 2026-11-20: POST / takes a JSON publish,
/// DELETE /&lt;topic&gt;/&lt;sequence id&gt; deletes one, GET /&lt;topic&gt;/json streams a line per message (an "open"
/// line first). <see cref="Reply"/> plays the phone's button tap: a message on the reply topic. Shared by the unit and
/// integration tests.</summary>
public sealed class FakeNtfy : IDisposable
{
    readonly HttpListener _http = new();
    readonly CancellationTokenSource _stop = new();
    readonly Channel<string> _replies = Channel.CreateUnbounded<string>();
    int _ids;

    public string Url { get; }
    public List<JsonObject> Published { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<string> Streams { get; } = [];
    /// <summary>The status a publish gets (tests set 400 to see a refusal reported).</summary>
    public int PublishStatus { get; set; } = 200;

    public FakeNtfy()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        Url = $"http://127.0.0.1:{port}";
        _http.Prefixes.Add(Url + "/");
        _http.Start();
        _ = Task.Run(Loop);
    }

    /// <summary>A tap on the phone: a message on the reply topic, as its button posts it. Returns the message's id.</summary>
    public string Reply(string message)
    {
        var id = "m" + Interlocked.Increment(ref _ids);
        _replies.Writer.TryWrite(new JsonObject { ["id"] = id, ["event"] = "message", ["message"] = message }.ToJsonString());
        return id;
    }

    public JsonObject[] Snapshot() { lock (Published) return [.. Published]; }
    public string[] DeletedSnapshot() { lock (Deleted) return [.. Deleted]; }
    public int StreamCount { get { lock (Streams) return Streams.Count; } }

    async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync(); } catch (Exception) { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var path = req.Url!.AbsolutePath;
            if (req.HttpMethod == "POST" && path == "/")
            {
                var body = await new StreamReader(req.InputStream).ReadToEndAsync();
                lock (Published) Published.Add((JsonObject)JsonNode.Parse(body)!);
                ctx.Response.StatusCode = PublishStatus;
                await Write(ctx.Response, PublishStatus == 200 ? "{\"event\":\"message\"}" : "{\"error\":\"refused\"}");
                ctx.Response.Close();
            }
            else if (req.HttpMethod == "DELETE")
            {
                lock (Deleted) Deleted.Add(path.TrimStart('/'));
                await Write(ctx.Response, "{}");
                ctx.Response.Close();
            }
            else if (req.HttpMethod == "GET" && path.EndsWith("/json"))
            {
                lock (Streams) Streams.Add(req.Url.PathAndQuery);
                ctx.Response.SendChunked = true;
                await Write(ctx.Response, "{\"id\":\"open1\",\"event\":\"open\"}\n");
                while (await _replies.Reader.WaitToReadAsync(_stop.Token))
                    while (_replies.Reader.TryRead(out var line)) await Write(ctx.Response, line + "\n");
            }
            else { ctx.Response.StatusCode = 404; ctx.Response.Close(); }
        }
        catch (Exception) { /* a client went away, or the test ended */ }
    }

    static async Task Write(HttpListenerResponse res, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await res.OutputStream.WriteAsync(bytes);
        await res.OutputStream.FlushAsync();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _http.Stop(); _http.Close(); } catch (Exception) { }
    }
}
