using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0235: a spare Fettle session stands in for a new chat's only when the chat would start exactly as it did.</summary>
public class SpareSessionTests
{
    [Fact]
    public void The_key_changes_with_the_args_the_folder_the_API_and_what_the_named_files_hold()
    {
        var dir = Directory.CreateTempSubdirectory("spare-key").FullName;
        try
        {
            var mcp = Path.Combine(dir, "mcp.json");
            var brief = Path.Combine(dir, "brief.md");
            File.WriteAllText(mcp, "{ \"mcpServers\": { \"a\": {} } }");
            File.WriteAllText(brief, "You are Fettle.");
            List<string> args = ["--add-dir", dir, "--append-system-prompt-file", brief, "--strict-mcp-config", "--mcp-config", mcp];
            var key = SpareSession.KeyFor(dir, args, "http://localhost:1/api/t");

            Assert.Equal(key, SpareSession.KeyFor(dir + Path.DirectorySeparatorChar, [.. args], "http://localhost:1/api/t"));
            Assert.NotEqual(key, SpareSession.KeyFor(dir, [.. args, "--model", "opus"], "http://localhost:1/api/t"));
            Assert.NotEqual(key, SpareSession.KeyFor(dir, args, "http://localhost:2/api/t"));
            Assert.NotEqual(key, SpareSession.KeyFor(Path.GetTempPath(), args, "http://localhost:1/api/t"));

            // same paths, new contents: a server unticked, or a project added to the briefing
            File.WriteAllText(mcp, "{ \"mcpServers\": {} }");
            Assert.NotEqual(key, SpareSession.KeyFor(dir, args, "http://localhost:1/api/t"));
            File.WriteAllText(mcp, "{ \"mcpServers\": { \"a\": {} } }");
            Assert.Equal(key, SpareSession.KeyFor(dir, args, "http://localhost:1/api/t"));
            File.WriteAllText(brief, "You are Fettle. Projects: Other.");
            Assert.NotEqual(key, SpareSession.KeyFor(dir, args, "http://localhost:1/api/t"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_pick_without_text_is_known_by_rule_or_fallback_and_unknown_when_Haiku_would_read_the_words()
    {
        var starter = ModelProfiles.Parse(ModelProfiles.Starter);
        Assert.True(ModelProfiles.PickWithoutText(starter, new AgentJob(JobKinds.Fettle, ""), out var fettle));
        Assert.Equal("Standard", fettle!.Profile.Name); // the starter's kind: fettle rule
        Assert.False(ModelProfiles.PickWithoutText(starter, new AgentJob(JobKinds.Chat, ""), out _)); // Haiku would pick on the words

        var noHaiku = ModelProfiles.Parse("Ask Haiku: no\nOtherwise: B\n## A\nModel: haiku\n## B\nModel: opus\n");
        Assert.True(ModelProfiles.PickWithoutText(noHaiku, new AgentJob(JobKinds.Fettle, ""), out var fallback));
        Assert.Equal("B", fallback!.Profile.Name);

        Assert.True(ModelProfiles.PickWithoutText(ModelProfiles.Parse(""), new AgentJob(JobKinds.Fettle, ""), out var none));
        Assert.Null(none); // no profiles: the person's own settings
    }
}
