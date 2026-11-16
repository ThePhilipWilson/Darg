using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Darg.Core;

/// <summary>A day's Claude time, as the header's timer shows it (WOR-0069). Active: the time any session was
/// mid-turn, sessions that overlap counted once, plus the gaps between turns up to the break threshold
/// (reading, thinking); a longer gap is a break. Busy: the mid-turn time alone.</summary>
public sealed record WorkDay(TimeSpan Active, TimeSpan Busy, int Turns, int Sessions, bool Working, TimeSpan BreakAfter);

/// <summary>Reads the timings claude-code-timer's hooks write (<c>~/.claude/stats/timings</c>, one jsonl per
/// session: session_start, prompt_submit, agent_stop with a UTC ts). The hooks run in every Claude Code
/// session, Darg's headless agents included, so this is all the Claude time on the machine. The same
/// break threshold and folder as the timer's own config (<c>~/.claude/timer-config.json</c>).</summary>
public sealed class WorkTimer
{
    /// <summary>A day runs from 04:00 to 04:00, so working past midnight stays one day.</summary>
    public static readonly TimeSpan DayStartsAt = TimeSpan.FromHours(4);
    /// <summary>A turn with no stop yet counts up to now, but no more than this past its start: a session killed
    /// mid-turn never writes its stop.</summary>
    public static readonly TimeSpan OpenTurnCap = TimeSpan.FromHours(2);
    public static readonly TimeSpan DefaultBreak = TimeSpan.FromMinutes(5);

    readonly string _folder;
    readonly TimeSpan _break;
    readonly Dictionary<string, (DateTime Written, List<Turn> Turns)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public WorkTimer(string folder, TimeSpan breakAfter) { _folder = folder; _break = breakAfter; }

    /// <summary>The timer as claude-code-timer is set up here; null when it isn't installed (no timings folder).</summary>
    public static WorkTimer? FromConfig(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.Combine(home, ".claude", "stats", "timings");
        var breakAfter = DefaultBreak;
        try
        {
            var config = Path.Combine(home, ".claude", "timer-config.json");
            if (File.Exists(config) && JsonNode.Parse(File.ReadAllText(config)) is JsonObject c)
            {
                if (c["timings_dir"] is JsonValue d && d.TryGetValue<string>(out var dir) && dir.Length > 0)
                    folder = dir.StartsWith('~') ? home + dir[1..] : dir;
                if (c["break_threshold_minutes"] is JsonValue b && b.TryGetValue<double>(out var mins) && mins > 0)
                    breakAfter = TimeSpan.FromMinutes(mins);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return Directory.Exists(folder) ? new WorkTimer(folder, breakAfter) : null;
    }

    /// <summary>When the day holding <paramref name="now"/> began (local 04:00).</summary>
    public static DateTimeOffset DayStart(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var start = local.Date + DayStartsAt;
        if (local.DateTime < start) start = start.AddDays(-1);
        return new DateTimeOffset(start, TimeZoneInfo.Local.GetUtcOffset(start));
    }

    /// <summary>Today's Claude time. Files are re-read only when they've been written since last time.</summary>
    public WorkDay Today(DateTimeOffset now)
    {
        var from = DayStart(now);
        var turns = new List<Turn>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(_folder, "*.jsonl").ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { files = []; }
        foreach (var f in files)
        {
            DateTime written;
            try { written = File.GetLastWriteTimeUtc(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (written < (from - OpenTurnCap).UtcDateTime) continue;
            seen.Add(f);
            if (!_cache.TryGetValue(f, out var c) || c.Written != written)
                _cache[f] = c = (written, TurnsIn(ReadLines(f)));
            turns.AddRange(c.Turns);
        }
        foreach (var gone in _cache.Keys.Where(k => !seen.Contains(k)).ToList()) _cache.Remove(gone);
        return Measure(turns, from, now, _break);
    }

    static IEnumerable<string> ReadLines(string file)
    {
        try { return File.ReadAllLines(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>A turn: from a prompt to its stop (or the next prompt, if one was cut off); End null while it runs.</summary>
    public sealed record Turn(string Session, DateTimeOffset Start, DateTimeOffset? End);

    /// <summary>The turns in one session's timing lines (lines that aren't events are skipped).</summary>
    public static List<Turn> TurnsIn(IEnumerable<string> lines)
    {
        var events = new List<(DateTimeOffset At, string Event, string Session)>();
        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0) continue;
            try
            {
                if (JsonNode.Parse(line) is not JsonObject o) continue;
                if (o["ts"] is not JsonValue t || !t.TryGetValue<string>(out var ts)
                    || !DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) continue;
                if (o["event"] is not JsonValue e || !e.TryGetValue<string>(out var ev)) continue;
                var session = o["session_id"] is JsonValue s && s.TryGetValue<string>(out var sid) ? sid : "";
                events.Add((at, ev, session));
            }
            catch (JsonException) { }
        }
        events.Sort((a, b) => a.At.CompareTo(b.At));

        var turns = new List<Turn>();
        (DateTimeOffset At, string Session)? open = null;
        foreach (var (at, ev, session) in events)
        {
            if (ev == "prompt_submit")
            {
                if (open is { } o) turns.Add(new Turn(o.Session, o.At, at));
                open = (at, session);
            }
            else if (ev == "agent_stop" && open is { } o)
            {
                turns.Add(new Turn(o.Session, o.At, at));
                open = null;
            }
        }
        if (open is { } last) turns.Add(new Turn(last.Session, last.At, null));
        return turns;
    }

    /// <summary>The day's figures from every session's turns: each clipped to [from, now], merged where they
    /// overlap, with gaps up to <paramref name="breakAfter"/> between them counted as active too.</summary>
    public static WorkDay Measure(IEnumerable<Turn> turns, DateTimeOffset from, DateTimeOffset now, TimeSpan breakAfter)
    {
        var spans = new List<(DateTimeOffset S, DateTimeOffset E)>();
        int count = 0;
        var sessions = new HashSet<string>();
        var working = false;
        foreach (var t in turns)
        {
            var end = t.End ?? Min(now, t.Start + OpenTurnCap);
            if (t.End is null && t.Start + OpenTurnCap > now) working = true;
            var s = Max(t.Start, from);
            var e = Min(end, now);
            if (e <= s) continue;
            spans.Add((s, e));
            count++;
            sessions.Add(t.Session);
        }
        spans.Sort((a, b) => a.S.CompareTo(b.S));

        TimeSpan busy = TimeSpan.Zero, active = TimeSpan.Zero;
        DateTimeOffset? curS = null, curE = null;
        foreach (var (s, e) in spans)
        {
            if (curE is { } ce && s <= ce) { if (e > ce) curE = e; continue; }
            if (curS is { } cs && curE is { } pe)
            {
                busy += pe - cs;
                active += pe - cs;
                if (s - pe <= breakAfter) active += s - pe;
            }
            curS = s; curE = e;
        }
        if (curS is { } ls && curE is { } le) { busy += le - ls; active += le - ls; }
        return new WorkDay(active, busy, count, sessions.Count, working, breakAfter);
    }

    static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
