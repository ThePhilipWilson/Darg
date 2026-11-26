namespace Darg.Desktop.ViewModels;

/// <summary>A section's folder read off the UI thread and shown on it in one go (WOR-0231: Artifacts and Notes
/// read whole folders on the UI thread each time a file watcher fired, holding the window for seconds).
/// <see cref="Run"/> is called on the UI thread; the read runs on the thread pool, and its result is handed to
/// <c>apply</c> back on the UI thread. One read at a time: asked again while one runs, the newest ask waits for it
/// and reads once more after, and any asks between are folded into that one, so a burst of changes reads at most
/// twice. Every result is shown as it lands (it is newer than what shows), so a steady stream of changes never
/// starves the view. The task a caller gets ends once what was on disk when it asked is showing.</summary>
public sealed class BackgroundRead<T>(Func<T> read, Action<T> apply, Action<Exception> failed)
{
    readonly SemaphoreSlim _gate = new(1, 1);
    int _asked;
    Task _latest = Task.CompletedTask;

    public Task Run()
    {
        var t = RunOne(++_asked);
        _latest = t;
        return t;
    }

    async Task RunOne(int ask)
    {
        var folded = false;
        await _gate.WaitAsync();
        try
        {
            // a newer ask is queued behind this one and will read: nothing to do here but wait for it
            if (ask != _asked) folded = true;
            else
            {
                T result;
                try { result = await Task.Run(read); }
                catch (Exception e) { failed(e); return; }
                apply(result);
            }
        }
        finally { _gate.Release(); }
        if (folded) await _latest;
    }
}
