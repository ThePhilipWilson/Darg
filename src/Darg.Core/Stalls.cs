using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>Watches the UI thread for stalls (WOR-0076). A heartbeat on the UI thread calls <see cref="Beat"/>
/// every <see cref="Interval"/>; a gap over <see cref="Threshold"/> means the thread was held that long (no input,
/// no repaint), and it is logged with the named sections (<see cref="Section"/>) that ran during it, slowest
/// first, so the log says what held it. Time in no section is layout, rendering and bindings.</summary>
public sealed class StallWatch
{
    public static StallWatch Shared { get; } = new();

    public TimeSpan Threshold { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(50);
    /// <summary>Where stalls are appended (stalls.log in the instance folder); null: kept only in <see cref="Recent"/>.</summary>
    public string? LogFile { get; set; }
    /// <summary>The test's clock; the real one otherwise.</summary>
    public Func<long> Now { get; init; } = Stopwatch.GetTimestamp;
    /// <summary>Time the process has spent paused for garbage collection so far (the test's, or the runtime's).</summary>
    public Func<TimeSpan> GcPauses { get; init; } = GC.GetTotalPauseDuration;

    readonly object _lock = new();
    readonly Dictionary<string, Timed> _done = [];
    readonly List<Stall> _recent = [];
    long _last;
    bool _beating;
    TimeSpan _gcAtLast;
    [ThreadStatic] static List<string>? _open;

    /// <summary>The last stalls seen (up to 50), newest last.</summary>
    public IReadOnlyList<Stall> Recent { get { lock (_lock) return _recent.ToList(); } }

    /// <summary>Time a piece of work: <c>using var _ = StallWatch.Shared.Section("Board.Reload");</c>. Sections
    /// inside sections are named by their path ("Work on > Agents.Start"). Meant for UI-thread work: a section on another thread doesn't hold the UI.</summary>
    public Scope Section(string name)
    {
        var open = _open ??= [];
        open.Add(name);
        return new Scope(this, string.Join(" > ", open), open.Count == 1, Now());
    }

    void Ended(string path, bool top, long started)
    {
        var open = _open;
        if (open is { Count: > 0 }) open.RemoveAt(open.Count - 1);
        var ms = Ms(Now() - started);
        // added up by name, so a flood of small pieces of work shows as well as one slow one
        lock (_lock)
            _done[path] = _done.TryGetValue(path, out var t) ? t with { Ms = t.Ms + ms, Count = t.Count + 1 } : new Timed(path, ms, top, 1);
    }

    /// <summary>A heartbeat on the UI thread. Returns the stall when the gap since the last beat was over the
    /// threshold (and logs it), else null. The sections that ended since the last beat go with it either way.</summary>
    public Stall? Beat()
    {
        var now = Now();
        List<Timed> sections;
        lock (_lock) { sections = [.. _done.Values]; _done.Clear(); }
        var gcNow = GcPauses();
        var (last, gcLast, beating) = (_last, _gcAtLast, _beating);
        (_last, _gcAtLast, _beating) = (now, gcNow, true);
        if (!beating) return null;
        var gap = Ms(now - last);
        if (gap < Threshold.TotalMilliseconds) return null;
        var stall = new Stall(DateTime.Now, gap, sections.Where(s => s.Ms >= 1).OrderByDescending(s => s.Ms).ToList())
        {
            GcMs = (gcNow - gcLast).TotalMilliseconds,
        };
        lock (_lock)
        {
            _recent.Add(stall);
            if (_recent.Count > 50) _recent.RemoveAt(0);
        }
        Log(stall);
        return stall;
    }

    void Log(Stall stall)
    {
        if (LogFile is null) return;
        try
        {
            var f = new FileInfo(LogFile);
            if (f.Exists && f.Length > 1_000_000) File.Move(LogFile, Path.ChangeExtension(LogFile, ".old.log"), overwrite: true);
            File.AppendAllText(LogFile, stall.Line() + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public readonly struct Scope(StallWatch watch, string path, bool top, long started) : IDisposable
    {
        public void Dispose() => watch.Ended(path, top, started);
    }
}

/// <summary>A section of work that ended during a stall: its path ("Work on > Agents.Start"), how long it took in
/// all, whether it's a top-level one (top-level ones don't overlap, so they add up), and how many times it ran.</summary>
public sealed record Timed(string Name, double Ms, bool Top, int Count = 1);

/// <summary>A gap in the UI thread's heartbeat, with the sections that ran in it, slowest first.</summary>
public sealed record Stall(DateTime At, double Ms, IReadOnlyList<Timed> Sections)
{
    /// <summary>How much of the gap the garbage collector had every thread paused (it may overlap sections).</summary>
    public double GcMs { get; init; }

    /// <summary>The part of the gap no section accounts for: layout, rendering, bindings, or unmeasured work.</summary>
    public double Unaccounted => Math.Max(0, Ms - Sections.Where(s => s.Top).Sum(s => s.Ms));

    /// <summary>One line for stalls.log: "00:31:02.114 stall 1834 ms: Work on 1650, Work on > Agents.Start 300 · 184 ms in none".</summary>
    public string Line()
    {
        var sb = new StringBuilder($"{At:yyyy-MM-dd HH:mm:ss.fff} stall {Ms:0} ms");
        var shown = Sections.Take(12).ToList();
        if (shown.Count > 0) sb.Append(": ").Append(string.Join(", ", shown.Select(s => s.Count > 1 ? $"{s.Name} {s.Ms:0} (x{s.Count})" : $"{s.Name} {s.Ms:0}")));
        if (Sections.Count > shown.Count) sb.Append($", +{Sections.Count - shown.Count} more");
        sb.Append($" · {Unaccounted:0} ms in no section");
        if (GcMs >= 1) sb.Append($" · GC paused {GcMs:0} ms");
        return sb.ToString();
    }
}
