using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0076: what froze the window when several cards were started, and the stall watch that found it.</summary>
public sealed class StallTests
{
    /// <summary>A child that reads nothing for a few seconds, as claude reads nothing while it starts up.</summary>
    static ProcessStartInfo SlowReader()
    {
        var psi = OperatingSystem.IsWindows()
            // not ping: DevServerTests counts the ping processes started while it runs
            ? new ProcessStartInfo("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep 3; $null = [Console]::In.ReadToEnd()"])
            : new ProcessStartInfo("/bin/sh", ["-c", "sleep 3; cat >/dev/null"]);
        psi.RedirectStandardInput = psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return psi;
    }

    [Fact]
    public async Task Send_returns_before_the_child_reads()
    {
        await using var s = AgentSession.Run(SlowReader());
        var clock = Stopwatch.StartNew();
        s.Send(new string('x', 256 * 1024)); // far more than a pipe holds: written straight, this waited for the child
        s.Send("and a second message after it");
        Assert.True(clock.ElapsedMilliseconds < 1000, $"Send held the caller for {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_gap_over_the_threshold_is_a_stall_with_what_ran_in_it()
    {
        long now = 0;
        var tick = Stopwatch.Frequency / 1000; // a millisecond
        var watch = new StallWatch { Now = () => now };
        watch.Beat();
        now += 50 * tick;
        Assert.Null(watch.Beat()); // a normal beat

        using (watch.Section("Work on"))
        {
            now += 100 * tick;
            using (watch.Section("Agents.Start")) now += 200 * tick;
            using (watch.Section("Agents.Start")) now += 100 * tick;
        }
        using (watch.Section("Board.Reload")) now += 50 * tick;
        now += 150 * tick; // layout: in no section
        var stall = watch.Beat();

        Assert.NotNull(stall);
        Assert.Equal(600, stall.Ms, 1);
        Assert.Equal(new[] { "Work on", "Work on > Agents.Start", "Board.Reload" }, stall.Sections.Select(s => s.Name));
        Assert.Equal(400, stall.Sections[0].Ms, 1);
        Assert.Equal(2, stall.Sections[1].Count);
        Assert.Equal(150, stall.Unaccounted, 1);
        Assert.Contains("Work on > Agents.Start 300 (x2)", stall.Line());
        Assert.Same(stall, watch.Recent.Single());

        now += 50 * tick;
        Assert.Null(watch.Beat()); // sections from before don't carry over
        using (watch.Section("Agents.Start")) now += 300 * tick;
        Assert.Equal("Agents.Start", Assert.Single(watch.Beat()!.Sections).Name);
    }

    sealed class Item(string name) { public override string ToString() => name; }

    [Fact]
    public void A_list_brought_up_to_date_keeps_the_items_that_stay()
    {
        Item a = new("a"), b = new("b"), c = new("c"), d = new("d"), e = new("e");
        var shown = new System.Collections.ObjectModel.ObservableCollection<Item>([a, b, c, d]);
        var changes = 0;
        shown.CollectionChanged += (_, _) => changes++;
        ListSync.Sync(shown, [b, a, e, d]); // c gone, a and b swapped, e new before d

        Assert.Equal(new[] { b, a, e, d }, shown);
        Assert.True(changes <= 3, $"{changes} changes"); // a remove, a move, an insert: not a rebuild of all four
        ListSync.Sync(shown, [b, a, e, d]);
        Assert.Equal(new[] { b, a, e, d }, shown);
        ListSync.Sync(shown, []);
        Assert.Empty(shown);
    }

    [Fact]
    public void A_card_read_again_unchanged_is_the_same_card()
    {
        var card = new Card("WOR-1", "Title", "Backlog", "", "Bug", ["ui", "perf"], "WOR-1 - Title.md", "words");
        var again = card with { Labels = ["ui", "perf"] }; // a fresh list, as each read of the file makes
        Assert.NotEqual(card, again); // what a record's == says: the label lists are different objects
        Assert.True(card.SameAs(again));
        Assert.False(card.SameAs(again with { Labels = ["ui"] }));
        Assert.False(card.SameAs(again with { Description = "other words" }));
        Assert.False(card.SameAs(again with { Status = "In Progress" }));
    }

    [Fact]
    public void A_stall_is_appended_to_the_log()
    {
        var dir = Directory.CreateTempSubdirectory("darg-stalls").FullName;
        try
        {
            long now = 0;
            var watch = new StallWatch { Now = () => now, LogFile = Path.Combine(dir, "stalls.log") };
            watch.Beat();
            now += Stopwatch.Frequency; // a second
            watch.Beat();
            Assert.Contains("stall 1000 ms", File.ReadAllText(Path.Combine(dir, "stalls.log")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
