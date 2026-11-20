using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>AND-0005: the phone watches an agent and nudges it through the Darg API: the agents list, the transcript a
/// page at a time from where it last read, and a message sent mid-turn.</summary>
public class PhoneAgentsTests
{
    /// <summary>The app's API as the phone calls it, counting the transcript entries each answer carries.</summary>
    sealed class Phone
    {
        public readonly List<int> EntriesPerCall = [];

        public async Task<JsonNode?> Call(string command, object args)
        {
            var (status, body) = await ArtifactHost.Server!.Api.Call(command, ApiArgs.Parse(JsonSerializer.Serialize(args)));
            await DargApp.Settle();
            if (status != 200) throw new ApiError(body);
            JsonNode? n;
            try { n = JsonNode.Parse(body); } catch (JsonException) { n = JsonValue.Create(body); }
            if (command == "agent") EntriesPerCall.Add(n!["items"]!.AsArray().Count);
            return n;
        }
    }

    /// <summary>Wait until the agent is idle and its transcript has had nothing new for 1.5 s.</summary>
    internal static async Task Quiet(DargApp app, AgentViewModel agent)
    {
        var (seen, at) = (-1, DateTime.UtcNow);
        await app.Until(() =>
        {
            if (agent.Items.Count != seen) (seen, at) = (agent.Items.Count, DateTime.UtcNow);
            return agent.State == AgentState.Idle && DateTime.UtcNow - at > TimeSpan.FromSeconds(1.5);
        }, "the agent to finish and go quiet", 60);
    }

    [AvaloniaFact]
    public async Task An_agent_is_watched_from_where_it_was_read_and_nudged_mid_turn()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "4");
        try
        {
            using var app = DargApp.Make();
            await app.Start();
            var phone = new Phone();
            await app.Api("start-agent", new { project = "proj", prompt = "tidy the header", name = "header" });
            var agent = app.Agents.Agents.Single();
            await app.Until(() => agent.State == AgentState.Working && agent.Items.OfType<ToolItem>().Count() >= 2, "the agent to be at work");

            // the list shows it at work with what it's doing
            var listed = Assert.Single(RemoteAgent.List(await phone.Call("agents", new { project = "proj" })));
            Assert.Equal(agent.Id, listed.Id);
            Assert.True(listed.Working);
            Assert.False(string.IsNullOrEmpty(listed.Doing));

            var feed = new AgentFeed(phone.Call, agent.Id);
            await feed.Refresh();
            Assert.Equal("header", feed.Name);
            Assert.True(feed.Working);
            Assert.Equal("tidy the header", feed.Entries[0].Text);
            Assert.Contains(feed.Entries, e => e.Kind == "tool" && e.Tool == "Read");
            Assert.Equal(Enumerable.Range(0, feed.Entries.Count), feed.Entries.Select(e => e.Index));

            // plain say is refused mid-turn (Fettle waits); the phone's nudge goes in
            await Assert.ThrowsAsync<ApiError>(() => phone.Call("say", new { id = agent.Id, text = "too soon" }));
            await feed.Send("use the teal logo");
            await app.Until(() => app.Prompts().Contains("use the teal logo"), "the nudge to reach the agent");

            // the fake works on the nudge in a second turn overlapping the first: wait until it's idle and nothing more comes
            await Quiet(app, agent);
            var before = feed.Entries.Count;
            await feed.Refresh();
            Assert.Equal(agent.Items.Count, feed.Entries.Count);
            Assert.Equal(Enumerable.Range(0, feed.Entries.Count), feed.Entries.Select(e => e.Index));
            Assert.Contains(feed.Entries, e => e.Kind == "user" && e.Text == "use the teal logo");
            Assert.All(feed.Entries, e => Assert.True(e.Settled, $"entry {e.Index} ({e.Kind}) unsettled after the turn"));
            Assert.Equal("idle", feed.Status);
            // the second read carried only what changed since the first, not the whole transcript again
            Assert.True(phone.EntriesPerCall[^1] < feed.Entries.Count, $"the last read carried {phone.EntriesPerCall[^1]} of {feed.Entries.Count} entries");
            Assert.True(feed.Entries.Count > before);

            // nothing new: an empty read
            await feed.Refresh();
            Assert.Equal(0, phone.EntriesPerCall[^1]);
            app.Shot("after-nudge");
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task The_agent_command_pages_by_index_and_refuses_a_bad_since()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("start-agent", new { project = "proj", prompt = "count to three" });
        var agent = app.Agents.Agents.Single();
        await app.Until(() => agent.State == AgentState.Idle, "the turn to end");

        var total = agent.Items.Count;
        Assert.True(total >= 3, $"only {total} entries");
        var page = JsonNode.Parse(await app.Api("agent", new { id = agent.Id, since = 1 }))!;
        Assert.Equal(total, (int)page["total"]!);
        Assert.Equal(total, (int)page["next"]!);
        Assert.Equal(1, (int)page["items"]![0]!["i"]!);
        Assert.Equal("idle", (string)page["status"]!);
        var step = page["items"]!.AsArray().First(i => (string)i!["kind"]! == "tool")!;
        Assert.Equal("Read", (string)step["tool"]!);
        Assert.True((bool)step["done"]!);

        // past the end: nothing, still saying how many there are; last n still works as before
        var past = JsonNode.Parse(await app.Api("agent", new { id = agent.Id, since = total + 5 }))!;
        Assert.Empty(past["items"]!.AsArray());
        Assert.Equal(total, (int)past["next"]!);
        var last = JsonNode.Parse(await app.Api("agent", new { id = agent.Id, last = 2 }))!;
        Assert.Equal(total - 2, (int)last["items"]![0]!["i"]!);

        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("agent", new { id = agent.Id, since = "soon" }));
        Assert.Contains("since", bad.Message);
    }
}
