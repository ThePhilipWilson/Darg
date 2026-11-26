using Darg.Core;

namespace Darg.Core.Tests;

public sealed class AgentStoreTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-agents-").FullName;

    [Fact]
    public void An_agents_purpose_is_saved_and_an_older_save_has_none()
    {
        AgentStore.Save(_dir, new AgentRecord { Id = "q", Purpose = AgentPurposes.Qa });
        AgentStore.Save(_dir, new AgentRecord { Id = "old" });
        var list = AgentStore.List(_dir);
        Assert.Equal(AgentPurposes.Qa, list.Single(r => r.Id == "q").Purpose);
        Assert.Null(list.Single(r => r.Id == "old").Purpose);
    }

    [Fact]
    public void A_pick_made_by_hand_is_saved_so_Optimise_tokens_leaves_it_alone_after_a_restart()
    {
        AgentStore.Save(_dir, new AgentRecord { Id = "h", Model = "opus", ModelByHand = true });
        AgentStore.Save(_dir, new AgentRecord { Id = "g", Model = "opus", Effort = "high", EffortByHand = true });
        var list = AgentStore.List(_dir);
        Assert.Equal((true, false), (list.Single(r => r.Id == "h").ModelByHand, list.Single(r => r.Id == "h").EffortByHand));
        Assert.Equal((false, true), (list.Single(r => r.Id == "g").ModelByHand, list.Single(r => r.Id == "g").EffortByHand));
    }

    [Fact]
    public void Saves_lists_newest_first_and_deletes()
    {
        AgentStore.Save(_dir, new AgentRecord { Id = "a", Name = "old", SessionId = "s1", Updated = new DateTime(2026, 11, 1), Items = [new() { Kind = "user", Text = "hi" }] });
        AgentStore.Save(_dir, new AgentRecord { Id = "b", Name = "new", Updated = new DateTime(2026, 11, 16), Args = ["--add-dir", "x"] });
        var list = AgentStore.List(_dir);
        Assert.Equal(["b", "a"], list.Select(r => r.Id));
        Assert.Equal(("s1", "hi"), (list[1].SessionId, list[1].Items[0].Text));
        Assert.Equal(["--add-dir", "x"], list[0].Args);
        AgentStore.Delete(_dir, "a");
        Assert.Single(AgentStore.List(_dir));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void A_broken_file_is_skipped()
    {
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ not json");
        AgentStore.Save(_dir, new AgentRecord { Id = "ok" });
        Assert.Equal("ok", Assert.Single(AgentStore.List(_dir)).Id);
    }

    [Fact]
    public void A_save_in_the_background_is_listed_newest_last_written()
    {
        // a long transcript, as a card agent's is by its last turn
        var items = Enumerable.Range(0, 300).Select(i => new ItemRecord { Kind = "tool", Name = "Read", Result = new string('x', 4000) }).ToList();
        AgentStore.SaveLater(_dir, new AgentRecord { Id = "a", Name = "first", Items = items });
        AgentStore.SaveLater(_dir, new AgentRecord { Id = "a", Name = "second", Items = items });
        AgentStore.SaveLater(_dir, new AgentRecord { Id = "b", Name = "other" });
        var list = AgentStore.List(_dir); // waits for what's waiting
        Assert.Equal("second", list.Single(r => r.Id == "a").Name);
        Assert.Contains(list, r => r.Id == "b");
    }

    [Fact]
    public void A_deleted_agent_isnt_written_back_by_a_save_still_waiting()
    {
        var items = Enumerable.Range(0, 300).Select(i => new ItemRecord { Kind = "tool", Result = new string('x', 4000) }).ToList();
        for (var i = 0; i < 5; i++) AgentStore.SaveLater(_dir, new AgentRecord { Id = $"busy{i}", Items = items });
        AgentStore.SaveLater(_dir, new AgentRecord { Id = "gone", Items = items });
        AgentStore.Delete(_dir, "gone");
        AgentStore.Flush();
        Assert.False(File.Exists(Path.Combine(_dir, "gone.json")));
    }

    [Fact]
    public void List_doesnt_wait_on_a_writer_kept_busy_and_has_the_newest_records()
    {
        // WOR-0232: List waited for the background writer, which never finishes while agents keep saving, so it
        // held the UI thread for its full 10 s timeout.
        var items = Enumerable.Range(0, 50).Select(i => new ItemRecord { Kind = "tool", Result = new string('x', 2000) }).ToList();
        // each write slow, as a long transcript's is: the writer never catches up with the saves
        AgentStore.BeforeWrite = path => { if (path.StartsWith(_dir)) Thread.Sleep(30); };
        var stop = false;
        var saved = -1;
        var saver = Task.Run(() =>
        {
            for (var i = 0; !Volatile.Read(ref stop); i++)
            {
                AgentStore.SaveLater(_dir, new AgentRecord { Id = "busy", Name = i.ToString(), Items = items });
                AgentStore.SaveLater(_dir, new AgentRecord { Id = $"other{i % 3}", Items = items });
                Volatile.Write(ref saved, i);
                Thread.Sleep(5);
            }
        });
        try
        {
            while (Volatile.Read(ref saved) < 20) Thread.Yield(); // the writer is at it
            AgentStore.List(_dir); // compiled, so the clock times the wait, not the JIT
            AgentStore.SaveLater(_dir, new AgentRecord { Id = "fresh", Name = "newest" });
            var before = Volatile.Read(ref saved);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var list = AgentStore.List(_dir);
            clock.Stop();
            Assert.True(clock.ElapsedMilliseconds < 100, $"List took {clock.ElapsedMilliseconds} ms");
            Assert.Equal("newest", list.Single(r => r.Id == "fresh").Name);
            Assert.True(int.Parse(list.Single(r => r.Id == "busy").Name) >= before, "busy's newest save is listed");
            // the folder written another way lists each agent once all the same
            Assert.Single(AgentStore.List(_dir + Path.DirectorySeparatorChar), r => r.Id == "busy");
        }
        finally
        {
            Volatile.Write(ref stop, true);
            saver.Wait();
            AgentStore.BeforeWrite = null;
            AgentStore.Flush();
        }
    }

    [Fact]
    public void Delete_doesnt_wait_for_the_save_being_written_and_that_save_cant_bring_it_back()
    {
        using var writing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var gone = Path.Combine(_dir, "gone.json");
        AgentStore.BeforeWrite = path => { if (path == gone) { writing.Set(); release.Wait(TimeSpan.FromSeconds(30)); } };
        try
        {
            AgentStore.Save(_dir, new AgentRecord { Id = "gone", Name = "on disk" });
            AgentStore.SaveLater(_dir, new AgentRecord { Id = "gone", Name = "being written" });
            Assert.True(writing.Wait(TimeSpan.FromSeconds(10)), "the writer took it");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            AgentStore.Delete(_dir, "gone");
            clock.Stop();
            Assert.True(clock.ElapsedMilliseconds < 100, $"Delete took {clock.ElapsedMilliseconds} ms");
            Assert.DoesNotContain(AgentStore.List(_dir), r => r.Id == "gone");
        }
        finally { release.Set(); }
        AgentStore.Flush();
        AgentStore.BeforeWrite = null;
        Assert.False(File.Exists(gone), "the save in hand wrote it back");
        Assert.DoesNotContain(AgentStore.List(_dir), r => r.Id == "gone");
    }

    [Fact]
    public void Save_now_doesnt_wait_for_the_save_being_written_and_wins_over_it()
    {
        using var writing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var path = Path.Combine(_dir, "a.json");
        AgentStore.BeforeWrite = p => { if (p == path) { writing.Set(); release.Wait(TimeSpan.FromSeconds(30)); } };
        try
        {
            AgentStore.SaveLater(_dir, new AgentRecord { Id = "a", Name = "older" });
            Assert.True(writing.Wait(TimeSpan.FromSeconds(10)), "the writer took it");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            AgentStore.Save(_dir, new AgentRecord { Id = "a", Name = "newer" });
            clock.Stop();
            Assert.True(clock.ElapsedMilliseconds < 100, $"Save took {clock.ElapsedMilliseconds} ms");
            Assert.Equal("newer", AgentStore.List(_dir).Single(r => r.Id == "a").Name);
        }
        finally { release.Set(); }
        AgentStore.Flush();
        AgentStore.BeforeWrite = null;
        Assert.Equal("newer", AgentStore.List(_dir).Single(r => r.Id == "a").Name);
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
