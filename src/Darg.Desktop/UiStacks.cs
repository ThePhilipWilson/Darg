using System;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Runtime;

namespace Darg.Desktop;

/// <summary>Takes the UI thread's managed stack from another thread (WOR-0161), for <see cref="Darg.Core.StallWatch"/>
/// to log while the UI thread is held: ClrMD snapshots this process (PssCaptureSnapshot on Windows, a cheap
/// copy-on-write clone) and walks the UI thread in the snapshot, so the app itself is never suspended. It only runs
/// once the UI thread has been held a second, so its cost (tens of ms, on the watchdog thread) is never paid in
/// normal use. A watchdog thread calls <see cref="Darg.Core.StallWatch.Watch"/> every 250 ms.</summary>
public static class UiStacks
{
    static int _uiThread;

    /// <summary>Called on the UI thread: remembers which thread that is and starts the watchdog. DARG_STACKS=0 leaves it off.</summary>
    public static void Start()
    {
        if (Environment.GetEnvironmentVariable("DARG_STACKS") == "0") return;
        _uiThread = Environment.CurrentManagedThreadId;
        // DARG_STACK_MS: take the first stack sooner than a second, to catch the stalls of 300 to 900 ms that add up
        if (int.TryParse(Environment.GetEnvironmentVariable("DARG_STACK_MS"), out var after) && after >= 100)
            Darg.Core.StallWatch.Shared.SampleAfter = TimeSpan.FromMilliseconds(after);
        Darg.Core.StallWatch.Shared.Sampler = () => Take(_uiThread);
        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(250);
                Darg.Core.StallWatch.Shared.Watch();
            }
        }) { IsBackground = true, Name = "Darg stall watchdog", Priority = ThreadPriority.AboveNormal }.Start();
    }

    /// <summary>A thread's stack (the UI thread's, for the watchdog), innermost frame first, up to 40 frames; framework frames kept, since a stall
    /// in layout or in a wait is only told apart by them.</summary>
    public static string? Take(int managedThreadId)
    {
        using var target = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
        var runtime = target.ClrVersions.FirstOrDefault()?.CreateRuntime();
        if (runtime is null) return "(no .NET runtime found in the snapshot)";
        using (runtime)
        {
            var thread = runtime.Threads.FirstOrDefault(t => t.ManagedThreadId == managedThreadId);
            if (thread is null) return $"(thread {managedThreadId} wasn't in the snapshot)";
            var sb = new StringBuilder();
            var n = 0;
            foreach (var f in thread.EnumerateStackTrace())
            {
                var name = f.Method?.Signature ?? (f.FrameName is { Length: > 0 } fn ? $"[{fn}]" : null);
                if (name is null) continue;
                sb.Append(name).Append('\n');
                if (++n == 40) { sb.Append("…\n"); break; }
            }
            return n == 0 ? "(no managed frames: the UI thread was in native code with none above it)" : sb.ToString();
        }
    }
}
