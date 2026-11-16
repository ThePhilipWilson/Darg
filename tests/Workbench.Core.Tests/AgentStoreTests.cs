using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class AgentStoreTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-agents-").FullName;

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

    public void Dispose() => Directory.Delete(_dir, true);
}
