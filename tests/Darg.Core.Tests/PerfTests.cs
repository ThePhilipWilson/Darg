using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0161: the UI thread's stack taken while it's held, and the once-a-minute perf line.</summary>
public sealed class PerfTests
{
    static readonly long Tick = Stopwatch.Frequency / 1000; // a millisecond

    [Fact]
    public void A_long_stall_carries_the_stacks_taken_while_it_held()
    {
        long now = 0;
        var stacks = new Queue<string>(["Wait\nBoard.Reload\n", "Wait\nBoard.Reload\n", "Layout\n"]);
        var watch = new StallWatch { Now = () => Interlocked.Read(ref now), Sampler = () => stacks.Dequeue() };
        watch.Beat();
        now += 500 * Tick;
        watch.Watch(); // half a second: not yet
        Assert.Equal(3, stacks.Count);
        now += 600 * Tick;
        watch.Watch(); // 1.1 s: the first
        watch.Watch(); // again at once: not until SampleEvery has passed
        Assert.Equal(2, stacks.Count);
        now += 2000 * Tick;
        watch.Watch(); // 3.1 s: the second, the same stack
        now += 2000 * Tick;
        watch.Watch(); // 5.1 s: the third
        var stall = watch.Beat()!;

        Assert.Equal(2, stall.Stacks.Count);
        Assert.Equal([1100, 3100], stall.Stacks[0].At.Select(a => Math.Round(a)));
        Assert.Contains("    UI thread at 1.1 s, 3.1 s:", stall.Detail());
        Assert.Contains("      Board.Reload", stall.Detail());
        Assert.Contains("    UI thread at 5.1 s:", stall.Detail());

        // the next stall starts with none, and a short one takes none
        now += 300 * Tick;
        Assert.Empty(watch.Beat()!.Stacks);
    }

    [Fact]
    public void Without_a_sampler_or_a_first_beat_nothing_is_taken()
    {
        long now = 0;
        var calls = 0;
        var watch = new StallWatch { Now = () => now, Sampler = () => { calls++; return "x"; } };
        now += 5000 * Tick;
        watch.Watch(); // no beat yet: the app is still starting
        Assert.Equal(0, calls);
        var none = new StallWatch { Now = () => now };
        none.Beat();
        now += 5000 * Tick;
        none.Watch();
        Assert.Empty(none.Beat()!.Stacks);
    }

    [Fact]
    public void A_stack_is_logged_under_its_stall()
    {
        var dir = Directory.CreateTempSubdirectory("darg-perf").FullName;
        try
        {
            long now = 0;
            var log = Path.Combine(dir, "stalls.log");
            var watch = new StallWatch { Now = () => now, LogFile = log, Sampler = () => "System.Threading.Monitor.Wait\nDarg.Core.Thing.Slow\n" };
            watch.Beat();
            now += 1500 * Tick;
            watch.Watch();
            watch.Beat();
            var lines = File.ReadAllLines(log);
            Assert.Contains("stall 1500 ms", lines[0]);
            Assert.Equal("    UI thread at 1.5 s:", lines[1]);
            Assert.Equal("      System.Threading.Monitor.Wait", lines[2]);
            Assert.Equal("      Darg.Core.Thing.Slow", lines[3]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_perf_line_says_what_the_minute_was_spent_on()
    {
        var dir = Directory.CreateTempSubdirectory("darg-perf").FullName;
        try
        {
            long now = 0;
            var watch = new StallWatch { Now = () => now };
            var t0 = new DateTime(2026, 11, 20, 5, 0, 0);
            var readings = new Queue<Reading>([
                new Reading(t0, TimeSpan.FromSeconds(10), 400L << 20, 100L << 20, 2, TimeSpan.FromMilliseconds(50), 20, 0),
                new Reading(t0.AddSeconds(60), TimeSpan.FromSeconds(40), 500L << 20, 180L << 20, 5, TimeSpan.FromMilliseconds(170), 24, 3),
            ]);
            var perf = new PerfLog(watch) { Read = () => readings.Dequeue(), LogFile = Path.Combine(dir, "perf.log") };
            Assert.Null(perf.Tick()); // the first only learns where it starts

            using (watch.Section("Board.Reload")) { now += 40 * Tick; using (watch.Section("Agents.RefreshCards")) now += 30 * Tick; }
            using (watch.Section("Board.Reload")) now += 20 * Tick;
            watch.Count("Watch.Board"); watch.Count("Watch.Board"); watch.Count("Watch.Board");
            var line = perf.Tick()!;

            Assert.StartsWith("2026-11-20 05:01:00 perf 60 s", line);
            Assert.Contains("CPU 0.50 cores", line);
            Assert.Contains("500 MB working set, 180 MB GC heap, 3 gen2", line);
            Assert.Contains("GC paused 120 ms", line);
            Assert.Contains("pool 24 threads, 3 queued", line);
            Assert.Contains("Board.Reload 90 ms (x2)", line); // top-level only: the nested section is inside it
            Assert.DoesNotContain("RefreshCards", line);
            Assert.Contains("Watch.Board (x3)", line);
            Assert.Equal(line, File.ReadAllText(Path.Combine(dir, "perf.log")).Trim());
            Assert.Empty(watch.TakeTally()); // taken: the next minute starts from nothing
        }
        finally { Directory.Delete(dir, true); }
    }
}
