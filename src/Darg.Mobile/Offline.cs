using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Mobile;

/// <summary>What the phone keeps between runs beside the connection (AND-0009): the theme picked, and the desktop's last
/// answer to every read, so the app shows something, marked as old, when the PC is off. One folder, the one the
/// connection file is in; every read tolerates a missing or broken file.</summary>
public sealed class PhoneData(string folder)
{
    public string Folder { get; } = folder;

    string SettingsFile => Path.Combine(Folder, "phone-settings.json");

    /// <summary>The desktop's last answers, for <see cref="DargClient.Cache"/>.</summary>
    public AnswerCache Answers { get; } = new(Path.Combine(folder, "answers"));

    public static PhoneData Beside(ConnectionStore store) => new(Path.GetDirectoryName(Path.GetFullPath(store.File))!);

    /// <summary>The picked theme, Dark when none or unknown (a Core theme's id).</summary>
    public Theme LoadTheme()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return Themes.Dark;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            return Themes.Get(doc.RootElement.TryGetProperty("theme", out var t) ? t.GetString() : null);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return Themes.Dark; }
    }

    public void SaveTheme(Theme theme)
    {
        try { Directory.CreateDirectory(Folder); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { theme = theme.Id })); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* not kept: Dark next run */ }
    }

    /// <summary>"12:03", or "Thu 12:03" / "3 Nov 12:03" when older: when the data the offline banner shows is from.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var l = at.ToLocalTime(); var n = now.ToLocalTime();
        return l.Date == n.Date ? l.ToString("HH:mm") : (n - l).TotalDays < 6 ? l.ToString("ddd HH:mm") : l.ToString("d MMM HH:mm");
    }
}

/// <summary>The desktop's last answer to each read command (with its arguments), kept on the phone so a screen still
/// has something to show with the PC off (AND-0009). A file per answer, named by a hash of address, command and
/// arguments, holding when it was read and what came back; the oldest go past <see cref="MaxFiles"/>.</summary>
public sealed class AnswerCache(string folder)
{
    public string Folder { get; } = folder;

    /// <summary>How many answers are kept: a transcript's pages, every doc and card opened, and so on.</summary>
    public int MaxFiles { get; init; } = 400;

    /// <summary>An unchanged answer rewrites its file (to move its time on) at most this often: polls answer every 2 s.</summary>
    static readonly TimeSpan Restamp = TimeSpan.FromMinutes(1);

    readonly Lock _lock = new();
    readonly Dictionary<string, (string Body, DateTimeOffset At)> _written = [];
    int _writes;

    /// <summary>The commands that only read, so their answers can stand in when the PC is off. Everything else (say,
    /// decide, move-card…) does something, and always goes to the PC.</summary>
    public static readonly IReadOnlySet<string> Reads = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "projects", "overview", "agents", "agent", "cards", "card", "flows", "notes", "docs", "doc", "reports", "report",
        "boards", "fettle", "fettle-chats", "needs-you",
    };

    static string Key(string address, string command, object? args) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{address.TrimEnd('/')}\n{command.ToLowerInvariant()}\n{JsonSerializer.Serialize(args ?? new { })}")))[..32].ToLowerInvariant();

    string FileFor(string key) => Path.Combine(Folder, key + ".json");

    /// <summary>The kept answer and when it was read, or null.</summary>
    public (JsonNode? Answer, DateTimeOffset At)? Get(string address, string command, object? args)
    {
        var key = Key(address, command, args);
        try
        {
            lock (_lock)
            {
                var file = FileFor(key);
                if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject o) return null;
                var at = DateTimeOffset.Parse(o["at"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                var body = o["body"]?.GetValue<string>() ?? "";
                return (Parse(body), at);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException or NullReferenceException) { return null; }
    }

    /// <summary>Keeps an answer as read now. Never throws: a cache that can't be written is just not there next time.</summary>
    public void Put(string address, string command, object? args, string body)
    {
        var key = Key(address, command, args);
        var now = DateTimeOffset.UtcNow;
        try
        {
            lock (_lock)
            {
                if (_written.TryGetValue(key, out var last) && last.Body == body && now - last.At < Restamp) return;
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FileFor(key), new JsonObject { ["at"] = now.ToString("o"), ["command"] = command, ["body"] = body }.ToJsonString());
                _written[key] = (body, now);
                if (++_writes % 50 == 1) Prune();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    void Prune()
    {
        var files = new DirectoryInfo(Folder).GetFiles("*.json");
        if (files.Length <= MaxFiles) return;
        foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxFiles))
            try { f.Delete(); } catch (IOException) { }
    }

    /// <summary>Forgets everything (the phone forgot its desktop).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _written.Clear();
            try { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>An answer's text as the client gives it back: JSON, or a plain string for a command that answers in words.</summary>
    internal static JsonNode? Parse(string body)
    {
        try { return body.Length == 0 ? null : JsonNode.Parse(body); }
        catch (JsonException) { return JsonValue.Create(body); }
    }
}
