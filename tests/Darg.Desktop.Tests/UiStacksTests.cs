using System.Runtime.CompilerServices;
using Darg.Desktop;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0161: the watchdog's stack of a held thread, read from a snapshot of this very process with ClrMD.</summary>
public sealed class UiStacksTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void HeldInHere(ManualResetEventSlim entered, ManualResetEventSlim release)
    {
        entered.Set();
        release.Wait();
    }

    [Fact]
    public void The_stack_of_a_held_thread_names_what_holds_it()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var id = 0;
        var t = new Thread(() => { id = Environment.CurrentManagedThreadId; HeldInHere(entered, release); }) { IsBackground = true };
        t.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        string? stack;
        try { stack = UiStacks.Take(id); }
        finally { release.Set(); }
        t.Join();

        Assert.NotNull(stack);
        var frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(frames, f => f.Contains("UiStacksTests.HeldInHere"));
        Assert.Contains(frames, f => f.Contains("ManualResetEventSlim.Wait"));
        // innermost first: the wait comes before the method that waits
        Assert.True(Array.FindIndex(frames, f => f.Contains("ManualResetEventSlim.Wait")) < Array.FindIndex(frames, f => f.Contains("HeldInHere")), stack);
    }
}
