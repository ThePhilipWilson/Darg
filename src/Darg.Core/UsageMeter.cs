using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darg.Core;

/// <summary>One usage window on a Claude subscription, as a rate_limit_event's unifiedWindows gives it:
/// "five_hour" or "seven_day", how much of it is used (0 to 1), when it resets, and "allowed" or "rejected".</summary>
public sealed record UsageWindow(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("used")] double Used,
    [property: JsonPropertyName("resetsAt")] DateTimeOffset? ResetsAt,
    [property: JsonPropertyName("status")] string Status)
{
    public const string FiveHour = "five_hour", Week = "seven_day";

    /// <summary>The share used as it stands at <paramref name="now"/>: none once the window has reset, since
    /// what was said before then no longer holds.</summary>
    public double UsedAt(DateTimeOffset now) => ResetsAt is { } r && r <= now ? 0 : Math.Clamp(Used, 0, 1);
}

/// <summary>The latest word on usage (WOR-0069), for the bar and timer in the header. Every agent turn
/// brings a rate_limit_event carrying both windows, so the meter is as fresh as the last turn any of
/// Darg's agents took; it is kept in <c>usage.json</c> beside instance.json so the header has something
/// to show when the app opens, before any agent has spoken.</summary>
public sealed class UsageMeter
{
    /// <summary>The one the app shows. AgentSession reports to it; until <see cref="Attach"/> it keeps what it
    /// hears in memory only, so tests that run sessions don't write into a real instance.</summary>
    public static UsageMeter Shared { get; } = new();

    readonly object _lock = new();
    string? _file;
    Snapshot _now = new([], null);

    /// <summary>Raised (on whatever thread the agent's output is read on) when the windows change.</summary>
    public event Action? Changed;

    public IReadOnlyList<UsageWindow> Windows { get { lock (_lock) return _now.Windows; } }
    /// <summary>When the windows were last heard of; null when never.</summary>
    public DateTimeOffset? SeenAt { get { lock (_lock) return _now.SeenAt; } }
    public UsageWindow? Window(string kind) => Windows.FirstOrDefault(w => w.Kind == kind);

    public static string FilePath(string? instanceFolder = null) => Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "usage.json");

    /// <summary>Reads what was saved last and saves from here on.</summary>
    public void Attach(string? instanceFolder = null)
    {
        var path = FilePath(instanceFolder);
        Snapshot? saved = null;
        try { if (File.Exists(path)) saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        lock (_lock)
        {
            _file = path;
            if (saved is { Windows: not null } s && (_now.SeenAt is null || s.SeenAt > _now.SeenAt)) _now = s;
        }
        Changed?.Invoke();
    }

    /// <summary>Takes in a rate_limit_event. One with windows replaces them all; an older CLI's, with only a
    /// status, still marks its window full when it says the limit was hit.</summary>
    public void Report(UsageLimited e, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        Snapshot next;
        lock (_lock)
        {
            if (e.Windows.Count > 0) next = new(e.Windows.ToList(), at);
            else if (e.Rejected && e.Kind is { Length: > 0 } kind)
                next = new(_now.Windows.Where(w => w.Kind != kind).Append(new UsageWindow(kind, 1, e.ResetsAt, e.Status)).ToList(), at);
            else return;
            _now = next;
        }
        Save(next);
        Changed?.Invoke();
    }

    void Save(Snapshot s)
    {
        string? path; lock (_lock) path = _file;
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(s));
        }
        catch (IOException) { } // a missed save only means an older reading on the next start
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Time left until a reset, short enough for the header: "2h 14m", "45m", "3d 4h", "<1m".</summary>
    public static string Countdown(DateTimeOffset resetsAt, DateTimeOffset now) =>
        resetsAt - now is var left && left <= TimeSpan.Zero ? "now" : Span(left);

    /// <summary>A length of time as the header shows it: "3d 4h", "2h 14m", "45m", "<1m".</summary>
    public static string Span(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m" : "<1m";
    }

    /// <summary>How long ago, for "as of": "just now", "12 min ago", "3 h ago", "2 days ago".</summary>
    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var d = now - then;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        return (int)d.TotalDays == 1 ? "a day ago" : $"{(int)d.TotalDays} days ago";
    }

    /// <summary>A reset time as a person reads it: "18:30" today, "Sat 00:00" within the week, else "20 Nov 17:00".</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;
        return local.Date == today ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.Date < today.AddDays(7) ? local.ToString("ddd HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
    }

    sealed record Snapshot(
        [property: JsonPropertyName("windows")] List<UsageWindow> Windows,
        [property: JsonPropertyName("seenAt")] DateTimeOffset? SeenAt);
}
