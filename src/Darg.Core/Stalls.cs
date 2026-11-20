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

    /// <summary>Gets the UI thread's stack as text, from another thread (the Desktop's ClrMD snapshot), for
    /// <see cref="Watch"/> to call while the UI thread is held; null: no stacks taken.</summary>
    public Func<string?>? Sampler { get; set; }
    /// <summary>How long the UI thread must be held before its stack is taken, then how often again, and how many at most per stall.</summary>
    public TimeSpan SampleAfter { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan SampleEvery { get; set; } = TimeSpan.FromSeconds(2);
    public int MaxSamples { get; set; } = 5;

    readonly object _lock = new();
    readonly Dictionary<string, Timed> _done = [];
    // every top-level section and counted event since the last perf line (WOR-0161)
    readonly Dictionary<string, Timed> _tally = [];
    readonly List<(double At, string Stack)> _samples = [];
    long _sampledFor = -1;
    int _sampledCount;
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
        {
            _done[path] = _done.TryGetValue(path, out var t) ? t with { Ms = t.Ms + ms, Count = t.Count + 1 } : new Timed(path, ms, top, 1);
            if (top) _tally[path] = _tally.TryGetValue(path, out var a) ? a with { Ms = a.Ms + ms, Count = a.Count + 1 } : new Timed(path, ms, true, 1);
        }
    }

    /// <summary>Count an event that isn't timed (a file watcher firing, an API request), for the perf line.</summary>
    public void Count(string name)
    {
        lock (_lock) _tally[name] = _tally.TryGetValue(name, out var a) ? a with { Count = a.Count + 1 } : new Timed(name, 0, false, 1);
    }

    /// <summary>The top-level sections and counted events since the last call, busiest first, and starts again.</summary>
    public IReadOnlyList<Timed> TakeTally()
    {
        lock (_lock)
        {
            var all = _tally.Values.OrderByDescending(t => t.Ms).ThenByDescending(t => t.Count).ToList();
            _tally.Clear();
            return all;
        }
    }

    /// <summary>The watchdog's step, called from a thread of its own every few hundred ms: while the UI thread has
    /// gone <see cref="SampleAfter"/> without a beat, take its stack with <see cref="Sampler"/> (then every
    /// <see cref="SampleEvery"/>, up to <see cref="MaxSamples"/>), to go into the stall's log entry. The stack says
    /// what held it when no section does ("in no section": a wait, a lock, a file read nobody timed, layout).</summary>
    public void Watch()
    {
        if (Sampler is not { } sampler || !Volatile.Read(ref _beating)) return;
        var last = Interlocked.Read(ref _last);
        var held = Ms(Now() - last);
        lock (_lock)
        {
            if (_sampledFor != last) { _sampledFor = last; _sampledCount = 0; }
            if (_sampledCount >= MaxSamples || held < SampleAfter.TotalMilliseconds + _sampledCount * SampleEvery.TotalMilliseconds) return;
            _sampledCount++;
        }
        string? stack;
        try { stack = sampler(); }
        catch (Exception e) { stack = $"(couldn't take the stack: {e.GetType().Name}: {e.Message})"; }
        if (stack is null) return;
        lock (_lock)
            if (Interlocked.Read(ref _last) == last) _samples.Add((held, stack)); // no beat since: it's this stall's
    }

    /// <summary>A heartbeat on the UI thread. Returns the stall when the gap since the last beat was over the
    /// threshold (and logs it), else null. The sections that ended since the last beat go with it either way.</summary>
    public Stall? Beat()
    {
        var now = Now();
        List<Timed> sections;
        List<(double At, string Stack)> samples;
        var gcNow = GcPauses();
        var (last, gcLast, beating) = (_last, _gcAtLast, _beating);
        lock (_lock)
        {
            sections = [.. _done.Values]; _done.Clear();
            samples = [.. _samples]; _samples.Clear();
            Interlocked.Exchange(ref _last, now);
        }
        _gcAtLast = gcNow;
        Volatile.Write(ref _beating, true);
        if (!beating) return null;
        var gap = Ms(now - last);
        if (gap < Threshold.TotalMilliseconds) return null;
        var stall = new Stall(DateTime.Now, gap, sections.Where(s => s.Ms >= 1).OrderByDescending(s => s.Ms).ToList())
        {
            GcMs = (gcNow - gcLast).TotalMilliseconds,
            Stacks = samples.GroupBy(x => x.Stack).Select(g => new StackSample(g.Select(x => x.At).ToList(), g.Key)).ToList(),
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
            File.AppendAllText(LogFile, stall.Line() + Environment.NewLine + stall.Detail());
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

/// <summary>A stack of the UI thread taken during a stall, and how far into the stall (ms) each time it was seen.</summary>
public sealed record StackSample(IReadOnlyList<double> At, string Stack);

/// <summary>A gap in the UI thread's heartbeat, with the sections that ran in it, slowest first.</summary>
public sealed record Stall(DateTime At, double Ms, IReadOnlyList<Timed> Sections)
{
    /// <summary>How much of the gap the garbage collector had every thread paused (it may overlap sections).</summary>
    public double GcMs { get; init; }

    /// <summary>The UI thread's stacks taken while it was held (<see cref="StallWatch.Watch"/>), each one once with every time it was seen.</summary>
    public IReadOnlyList<StackSample> Stacks { get; init; } = [];

    /// <summary>The stacks, indented under the stall's line in stalls.log ("" when none were taken).</summary>
    public string Detail()
    {
        var sb = new StringBuilder();
        foreach (var s in Stacks)
        {
            sb.Append("    UI thread at ").Append(string.Join(", ", s.At.Select(a => $"{a / 1000:0.0} s"))).AppendLine(":");
            foreach (var frame in s.Stack.Split('\n', StringSplitOptions.RemoveEmptyEntries)) sb.Append("      ").AppendLine(frame.TrimEnd());
        }
        return sb.ToString();
    }

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
