using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Darg.Core;

/// <summary>Where phone notifications go (AND-0006): an ntfy server and a topic whose name is the secret. The phone
/// subscribes to <see cref="Topic"/> in the ntfy app; its option buttons post back to <see cref="ReplyTopic"/>, which
/// Darg listens to, so an answer reaches the PC from anywhere without the PC being reachable from the phone.</summary>
public sealed class PushConfig
{
    public const string DefaultServer = "https://ntfy.sh";
    public string Server { get; set; } = DefaultServer;
    public string Topic { get; set; } = "";
    /// <summary>An ntfy access token for a protected topic (sent as a Bearer header, and by the phone's buttons).</summary>
    public string? Token { get; set; }

    [JsonIgnore] public bool On => Topic.Trim().Length > 0;
    [JsonIgnore] public string Base => (Server.Trim().Length > 0 ? Server.Trim() : DefaultServer).TrimEnd('/');
    [JsonIgnore] public string ReplyTopic => Topic.Trim() + "-reply";

    /// <summary>A topic nobody will guess: on ntfy.sh anyone who knows a topic's name can read and post to it.</summary>
    public static string NewTopic() => "darg-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}

/// <summary>A decision sent to the phone and not yet answered: the one-time key its buttons carry, and what each
/// button means. Kept in push.json so a tap made while Darg was closed is still taken when it starts.</summary>
public sealed record PushPending(string Key, string Project, string Card, List<string> Labels, DateTime Sent);

/// <summary>A button tap that came back: the decision it answers and the option's label.</summary>
public sealed record PushAnswer(string Key, string Project, string Card, string Label);

/// <summary>Phone notifications through ntfy (AND-0006). What notifies is what makes a desktop toast
/// (<see cref="Decisions"/>); a card waiting on a decision also carries up to <see cref="MaxActions"/> of its options as
/// buttons. A button makes the phone post <c>darg &lt;key&gt; &lt;n&gt;</c> to the reply topic; Darg, subscribed to it,
/// answers the card as picking that option on its panel does (the decide command), once: the key is forgotten then.
/// Each card's notification has a sequence id, so it is replaced when the decision changes and deleted when answered.</summary>
public sealed class PushStore
{
    /// <summary>ntfy shows at most three buttons.</summary>
    public const int MaxActions = 3;
    /// <summary>ntfy refuses a title over 1 KB and moves a message over 4 KB to an attachment; ours stay well under.</summary>
    public const int MaxMessage = 1500, MaxTitle = 200;
    /// <summary>How long a decision's buttons keep working (ntfy itself keeps a message 12 hours).</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);
    public const string ReplyWord = "darg";

    readonly string _file;
    readonly object _lock = new();
    State _state;

    sealed class State
    {
        [JsonPropertyName("since")] public string? Since { get; set; }
        [JsonPropertyName("pending")] public List<PushPending> Pending { get; set; } = [];
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public PushStore(string? instanceFolder = null)
    {
        _file = Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "push.json");
        try { _state = File.Exists(_file) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_file), Json) ?? new() : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _state = new(); }
    }

    /// <summary>The id of the last reply-topic message read, so a restart picks up where it left off (and taps made
    /// while closed); null the first time, when only new ones count.</summary>
    public string? Since { get { lock (_lock) return _state.Since; } }

    public IReadOnlyList<PushPending> Pending { get { lock (_lock) return _state.Pending.ToList(); } }

    /// <summary>A new key for a card's decision; any older one for the same card stops working.</summary>
    public PushPending Add(string project, string card, IEnumerable<string> labels, DateTime now)
    {
        var p = new PushPending(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), project, card, labels.ToList(), now);
        lock (_lock)
        {
            _state.Pending.RemoveAll(x => Same(x, project, card) || now - x.Sent > Keep);
            _state.Pending.Add(p);
            Save();
        }
        return p;
    }

    /// <summary>The card's decision was answered (anywhere): its buttons stop working. Whether there was one.</summary>
    public bool Forget(string project, string card)
    {
        lock (_lock)
        {
            var gone = _state.Pending.RemoveAll(x => Same(x, project, card)) > 0;
            if (gone) Save();
            return gone;
        }
    }

    /// <summary>A reply-topic message read: notes its id, and if it is a tap on a live button, forgets the key and
    /// returns the answer. Anything else (a stale or made-up key, another message on the topic) is null.</summary>
    public PushAnswer? Take(string? id, string message)
    {
        lock (_lock)
        {
            if (id is { Length: > 0 }) _state.Since = id;
            PushAnswer? answer = null;
            if (ParseReply(message) is { } r && _state.Pending.FirstOrDefault(x => x.Key == r.Key) is { } p && r.Index >= 0 && r.Index < p.Labels.Count)
            {
                _state.Pending.Remove(p);
                answer = new PushAnswer(p.Key, p.Project, p.Card, p.Labels[r.Index]);
            }
            Save();
            return answer;
        }
    }

    static bool Same(PushPending x, string project, string card) =>
        x.Project.Equals(project, StringComparison.OrdinalIgnoreCase) && x.Card.Equals(card, StringComparison.OrdinalIgnoreCase);

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_state, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* kept in memory; a restart loses unanswered keys */ }
    }

    /// <summary>"darg &lt;key&gt; &lt;n&gt;" as a button posts it; null for anything else.</summary>
    public static (string Key, int Index)? ParseReply(string message)
    {
        var parts = message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && parts[0] == ReplyWord && parts[1].Length >= 16 && int.TryParse(parts[2], out var n) ? (parts[1], n) : null;
    }

    /// <summary>The options a card's notification shows as buttons: the recommended one first, then the rest in order,
    /// at most <see cref="MaxActions"/> (a fourth option is answered on the desktop, and the message says so).</summary>
    public static List<DecisionOption> Buttons(CardDecision d) =>
        d.Options.OrderByDescending(o => o.Recommended).Take(MaxActions).ToList();

    /// <summary>The sequence id of a card's notification: the same for every send about that card, so a new one
    /// replaces it and answering deletes it.</summary>
    public static string SequenceId(string project, string card) =>
        new string($"{project}-{card}".ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());

    static string Cut(string s, int max) => s.Length > max ? s[..(max - 1)] + "…" : s;

    /// <summary>The ntfy publish body for a card waiting on a decision: the question and context, and a button per
    /// option (see <see cref="Buttons"/>) that posts its key and the option's number to the reply topic.</summary>
    public static JsonObject DecisionMessage(PushConfig config, PushPending pending, string projectName, string title, CardDecision d)
    {
        var buttons = Buttons(d);
        var text = new StringBuilder(d.Question);
        if (d.Context.Trim().Length > 0) text.Append("\n\n").Append(d.Context.Trim());
        if (d.Options.Count > buttons.Count) text.Append("\n\n").Append($"Also: {string.Join(", ", d.Options.Except(buttons).Select(o => o.Label))} (answer on the desktop).");
        var actions = new JsonArray();
        foreach (var o in buttons)
        {
            var action = new JsonObject
            {
                ["action"] = "http", ["label"] = Cut(o.Label + (o.Recommended ? " ★" : ""), 40),
                ["url"] = $"{config.Base}/{config.ReplyTopic}", ["method"] = "POST",
                ["body"] = $"{ReplyWord} {pending.Key} {pending.Labels.IndexOf(o.Label)}", ["clear"] = true,
            };
            if (config.Token is { Length: > 0 } t) action["headers"] = new JsonObject { ["Authorization"] = "Bearer " + t };
            actions.Add(action);
        }
        return new JsonObject
        {
            ["topic"] = config.Topic.Trim(), ["sequence_id"] = SequenceId(pending.Project, pending.Card),
            ["title"] = Cut($"{projectName}: {pending.Card} {title}", MaxTitle), ["message"] = Cut(text.ToString(), MaxMessage),
            ["priority"] = 4, ["tags"] = new JsonArray("question"), ["actions"] = actions,
        };
    }

    /// <summary>The ntfy publish body for anything else that notifies: an agent's question, a long turn finished or failed.</summary>
    public static JsonObject PlainMessage(PushConfig config, string title, string detail, bool urgent, string tag) => new()
    {
        ["topic"] = config.Topic.Trim(), ["title"] = Cut(title, MaxTitle), ["message"] = Cut(detail.Trim().Length > 0 ? detail.Trim() : title, MaxMessage),
        ["priority"] = urgent ? 4 : 3, ["tags"] = new JsonArray(tag),
    };
}

/// <summary>Talks to the ntfy server (AND-0006): publishes, deletes a card's notification once answered, and listens to
/// the reply topic for button taps, reconnecting when the stream drops. Every call is off the UI thread; failures are
/// reported through <see cref="Problem"/> and never thrown, since a phone notification is never worth stopping for.</summary>
public sealed class PushClient : IDisposable
{
    readonly PushConfig _config;
    readonly PushStore _store;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly HttpClient _stream = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly CancellationTokenSource _stop = new();
    Task? _listening;

    /// <summary>A button tap on a live decision. Raised off the UI thread.</summary>
    public event Action<PushAnswer>? Answered;
    /// <summary>What went wrong, in a line (a publish refused, the server unreachable). Raised off the UI thread.</summary>
    public event Action<string>? Problem;

    /// <summary>How long to wait before reconnecting a dropped stream (the tests shorten it).</summary>
    public TimeSpan Retry { get; init; } = TimeSpan.FromSeconds(10);

    public PushClient(PushConfig config, PushStore store) { _config = config; _store = store; }

    public PushStore Store => _store;
    public PushConfig Config => _config;

    HttpRequestMessage Request(HttpMethod method, string url, HttpContent? body = null)
    {
        var r = new HttpRequestMessage(method, url) { Content = body };
        if (_config.Token is { Length: > 0 } t) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
        return r;
    }

    /// <summary>Publishes one message (a body from <see cref="PushStore"/>); null when ntfy took it, else why not.</summary>
    public async Task<string?> Send(JsonObject message)
    {
        try
        {
            using var r = Request(HttpMethod.Post, _config.Base + "/", new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"));
            using var res = await _http.SendAsync(r, _stop.Token);
            if (res.IsSuccessStatusCode) return null;
            var why = $"ntfy refused a notification: {(int)res.StatusCode} {Cut(await res.Content.ReadAsStringAsync())}";
            Problem?.Invoke(why);
            return why;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var why = $"Couldn't reach ntfy at {_config.Base}: {e.Message}";
            Problem?.Invoke(why);
            return why;
        }
    }

    static string Cut(string s) => s.Length > 200 ? s[..200] + "…" : s.Trim();

    /// <summary>Sends a card's decision with its buttons, under a new key.</summary>
    public Task<string?> SendDecision(string project, string projectName, string card, string title, CardDecision d)
    {
        var pending = _store.Add(project, card, d.Options.Select(o => o.Label), DateTime.Now);
        return Send(PushStore.DecisionMessage(_config, pending, projectName, title, d));
    }

    /// <summary>A card's decision was answered: its buttons stop working and its notification goes from the phone.</summary>
    public async Task Settled(string project, string card)
    {
        if (!_store.Forget(project, card)) return;
        try
        {
            using var r = Request(HttpMethod.Delete, $"{_config.Base}/{_config.Topic.Trim()}/{PushStore.SequenceId(project, card)}");
            using var _ = await _http.SendAsync(r, _stop.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException) { /* it clears itself once tapped or swiped */ }
    }

    /// <summary>Starts listening to the reply topic, from where it left off last time.</summary>
    public void Listen() => _listening ??= Task.Run(ListenLoop);

    async Task ListenLoop()
    {
        string? lastProblem = null;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                // since=<id> replays taps made while Darg was closed (ntfy keeps them 12 hours); the first time, only new ones
                var since = _store.Since is { } s ? "?since=" + Uri.EscapeDataString(s) : "";
                using var r = Request(HttpMethod.Get, $"{_config.Base}/{_config.ReplyTopic}/json{since}");
                using var res = await _stream.SendAsync(r, HttpCompletionOption.ResponseHeadersRead, _stop.Token);
                res.EnsureSuccessStatusCode();
                lastProblem = null;
                using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(_stop.Token));
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                    if (Read(line) is { } answer) Answered?.Invoke(answer);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
            {
                var why = $"Not hearing phone answers from {_config.Base}: {e.Message}";
                if (why != lastProblem) Problem?.Invoke(why); // once per kind of failure, not every retry
                lastProblem = why;
            }
            try { await Task.Delay(Retry, _stop.Token); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One line of ntfy's JSON stream: a tap on a live button, else null (keepalives, opens, other messages).</summary>
    PushAnswer? Read(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject o || (string?)o["event"] != "message") return null;
            return _store.Take((string?)o["id"], (string?)o["message"] ?? "");
        }
        catch (JsonException) { return null; }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
        _stream.Dispose();
    }
}
