using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Darg.Core;

/// <summary>Carries a session's events from its reading thread to the UI thread, in the order they were read (WOR-0234).
/// Every event but streamed text is posted at once, as before; streamed text (<see cref="AssistantDelta"/>, a few
/// characters a time, dozens a second) is posted at most once per <see cref="Every"/>, the pieces of one content block
/// that came meanwhile merged into one, so the UI thread isn't posted to per token. One queue keeps the order: a whole
/// message is never handled before the pieces of it that came first, nor after pieces of the next.</summary>
public sealed class StreamInbox
{
    /// <summary>How often streamed text reaches the screen: about two frames, quick enough to read as typing.</summary>
    public static readonly TimeSpan DefaultEvery = TimeSpan.FromMilliseconds(40);

    readonly ConcurrentQueue<(AgentEvent Event, long Read)> _queue = new();
    readonly Action<Action> _post;
    readonly Action<IReadOnlyList<(AgentEvent Event, long Read)>> _deliver;
    int _later; // 1 while a delayed delivery for streamed text is on its way

    public TimeSpan Every { get; }

    /// <summary>Set by the host: how long the next batch of streamed text waits, when that depends on what's shown (a
    /// long reply redrawn as a whole each time waits longer, <see cref="PaceFor"/>); unset, <see cref="Every"/>.
    /// Asked on the reading thread.</summary>
    public Func<TimeSpan>? Pace { get; set; }

    /// <summary>The wait for a reply of this many characters: <see cref="DefaultEvery"/>, plus a millisecond for each 100
    /// characters, at most 250 ms, since the transcript draws its markdown again whole on each batch.</summary>
    public static TimeSpan PaceFor(int chars) =>
        TimeSpan.FromMilliseconds(Math.Min(250, DefaultEvery.TotalMilliseconds + chars / 100.0));

    /// <param name="post">Runs an action on the UI thread, later (Dispatcher.UIThread.Post).</param>
    /// <param name="deliver">Handles what was queued, on the UI thread: each event with the timestamp it was read at
    /// (<see cref="Stopwatch.GetTimestamp"/>), streamed pieces already merged.</param>
    public StreamInbox(Action<Action> post, Action<IReadOnlyList<(AgentEvent Event, long Read)>> deliver, TimeSpan? every = null)
    {
        _post = post;
        _deliver = deliver;
        Every = every ?? DefaultEvery;
    }

    /// <summary>An event from the reading thread.</summary>
    public void Add(AgentEvent e)
    {
        _queue.Enqueue((e, Stopwatch.GetTimestamp()));
        if (e is not AssistantDelta) { _post(Deliver); return; }
        if (Interlocked.Exchange(ref _later, 1) == 1) return; // one is on its way and takes this too
        _ = Task.Delay(Pace?.Invoke() ?? Every).ContinueWith(_ => _post(() => { Volatile.Write(ref _later, 0); Deliver(); }), TaskScheduler.Default);
    }

    void Deliver()
    {
        var taken = Take();
        if (taken.Count > 0) _deliver(taken);
    }

    /// <summary>Everything queued so far, in order, a run of pieces of the same content block merged into one.</summary>
    public IReadOnlyList<(AgentEvent Event, long Read)> Take()
    {
        var list = new List<(AgentEvent Event, long Read)>();
        StringBuilder? run = null;
        AssistantDelta? first = null;
        long firstRead = 0;
        void Flush()
        {
            if (first is null) return;
            list.Add((first with { Text = run!.ToString() }, firstRead));
            first = null;
            run = null;
        }
        while (_queue.TryDequeue(out var item))
        {
            if (item.Event is AssistantDelta d)
            {
                if (first is not null && (first.MessageId != d.MessageId || first.Index != d.Index)) Flush();
                if (first is null) { first = d; firstRead = item.Read; run = new StringBuilder(); }
                run!.Append(d.Text);
                continue;
            }
            Flush();
            list.Add(item);
        }
        Flush();
        return list;
    }
}
