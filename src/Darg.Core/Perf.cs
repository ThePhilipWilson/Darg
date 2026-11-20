using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>What the process did over the last minute (WOR-0161), as one line in perf.log beside stalls.log: CPU,
/// memory, garbage collection, the thread pool, how long the UI thread was held, and every top-level
/// <see cref="StallWatch.Section"/> and <see cref="StallWatch.Count"/> event with its count and time (board reloads,
/// agent events, file watchers firing, API requests). stalls.log says what held the window; this says what the
/// app was busy with while it didn't, and whether memory is climbing.</summary>
public sealed class PerfLog(StallWatch watch)
{
    public static PerfLog Shared { get; } = new(StallWatch.Shared);

    /// <summary>Where lines are appended (perf.log in the instance folder); null: only returned.</summary>
    public string? LogFile { get; set; }

    Reading? _last;

    /// <summary>The process's figures now (the test's own, or the runtime's).</summary>
    public Func<Reading> Read { get; init; } = Reading.Now;

    /// <summary>The figures since the last call, as the line it logs; the first call only learns the starting point (null).</summary>
    public string? Tick()
    {
        var now = Read();
        var tally = watch.TakeTally();
        var stalls = watch.Recent;
        // Recent keeps 50; a stall is new if it's after the last line's time
        var fresh = _last is { } l ? stalls.Where(s => s.At > l.At).ToList() : [];
        var last = _last;
        _last = now;
        if (last is null) return null;
        var line = Line(last, now, tally, fresh);
        if (LogFile is { } file)
            try
            {
                var f = new FileInfo(file);
                if (f.Exists && f.Length > 1_000_000) File.Move(file, Path.ChangeExtension(file, ".old.log"), overwrite: true);
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return line;
    }

    /// <summary>"2026-11-20 05:10:00 perf 60 s · CPU 0.42 cores · 412 MB working set, 180 MB GC heap, 3 gen2 · GC
    /// paused 120 ms · pool 24 threads, 0 queued · UI held 1.2 s in 4 stalls · Board.Reload 1830 ms (x41), Watch.Board (x120)".</summary>
    public static string Line(Reading a, Reading b, IReadOnlyList<Timed> tally, IReadOnlyList<Stall> stalls)
    {
        var secs = Math.Max(0.001, (b.At - a.At).TotalSeconds);
        var sb = new StringBuilder($"{b.At:yyyy-MM-dd HH:mm:ss} perf {secs:0} s");
        sb.Append($" · CPU {(b.Cpu - a.Cpu).TotalSeconds / secs:0.00} cores");
        sb.Append($" · {b.WorkingSet / 1048576} MB working set, {b.GcHeap / 1048576} MB GC heap, {b.Gen2 - a.Gen2} gen2");
        sb.Append($" · GC paused {(b.GcPaused - a.GcPaused).TotalMilliseconds:0} ms");
        sb.Append($" · pool {b.Threads} threads, {b.Queued} queued");
        sb.Append(stalls.Count == 0 ? " · UI never held" : $" · UI held {stalls.Sum(s => s.Ms) / 1000:0.0} s in {stalls.Count} stalls");
        var shown = tally.Take(15).ToList();
        if (shown.Count > 0)
            sb.Append(" · ").Append(string.Join(", ", shown.Select(t => t.Ms >= 1 ? $"{t.Name} {t.Ms:0} ms (x{t.Count})" : $"{t.Name} (x{t.Count})")));
        if (tally.Count > shown.Count) sb.Append($", +{tally.Count - shown.Count} more");
        return sb.ToString();
    }
}

/// <summary>The process's figures at one moment: CPU time used so far, memory, collections, GC pauses, thread pool.</summary>
public sealed record Reading(DateTime At, TimeSpan Cpu, long WorkingSet, long GcHeap, int Gen2, TimeSpan GcPaused, int Threads, long Queued)
{
    public static Reading Now()
    {
        using var p = Process.GetCurrentProcess();
        return new Reading(DateTime.Now, p.TotalProcessorTime, p.WorkingSet64, GC.GetGCMemoryInfo().HeapSizeBytes,
            GC.CollectionCount(2), GC.GetTotalPauseDuration(), ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount);
    }
}
