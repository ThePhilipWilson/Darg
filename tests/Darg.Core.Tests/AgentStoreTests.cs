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

    public void Dispose() => Directory.Delete(_dir, true);
}
