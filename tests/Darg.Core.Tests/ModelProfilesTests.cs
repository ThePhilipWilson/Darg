using Darg.Core;

namespace Darg.Core.Tests;

public class ModelProfilesTests
{
    static ProfileSet Starter => ModelProfiles.Parse(ModelProfiles.Starter);

    [Fact]
    public void StarterParses()
    {
        var set = Starter;
        Assert.Equal(["Quick", "Standard", "Deep"], set.Profiles.Select(p => p.Name));
        Assert.Equal(("haiku", "low"), (set.Profiles[0].Model, set.Profiles[0].Effort));
        Assert.Equal(("opus", "high"), (set.Profiles[2].Model, set.Profiles[2].Effort));
        Assert.All(set.Profiles, p => Assert.NotEmpty(p.UseFor));
        Assert.True(set.AskHaiku);
        Assert.Null(set.Otherwise);
        Assert.Equal(3, set.Rules.Count); // the prose lines in Rules aren't taken for rules
    }

    [Fact] // WOR-0236: a profile can start its agents without the person's hooks; every other keeps them
    public void HooksOffIsReadPerProfile()
    {
        Assert.All(Starter.Profiles, p => Assert.True(p.Hooks)); // the starter's Hooks line is prose, above the profiles
        var set = ModelProfiles.Parse("""
            ## Fast
            Model: sonnet
            Hooks: off
            ## Same
            Model: sonnet
            Hooks: on
            ## Plain
            Model: opus
            ## No
            Hooks: No
            """);
        Assert.Equal([false, true, true, false], set.Profiles.Select(p => p.Hooks));
        Assert.Equal("sonnet, no hooks", set.Profiles[0].Summary);
        Assert.Equal("opus", set.Profiles[2].Summary);

        var args = ModelProfiles.NoHooksArgs();
        Assert.Equal("--settings", args[0]);
        Assert.Equal("""{"disableAllHooks": true}""", File.ReadAllText(args[1]));
    }

    [Fact]
    public void RulesMatchInOrder()
    {
        var set = ModelProfiles.Parse("""
            ## Quick
            Model: haiku
            ## Deep
            Model: opus
            Effort: max
            ## Rules
            - label: Deep → Deep
            - type: bug -> Quick
            * role: QA => Quick
            - kind: fettle -> Missing
            - project: work -> Deep
            """);
        Assert.Equal(4, set.Rules.Count); // the rule naming a missing profile is dropped
        Assert.Equal("Deep", ModelProfiles.ByRule(set, new AgentJob(JobKinds.Card, "x", "Bug", ["deep"]))?.Profile.Name);
        Assert.Equal("Quick", ModelProfiles.ByRule(set, new AgentJob(JobKinds.Card, "x", "BUG", []))?.Profile.Name);
        Assert.Equal("Quick", ModelProfiles.ByRule(set, new AgentJob(JobKinds.Flow, "x", Role: "qa"))?.Profile.Name);
        Assert.Equal("Deep", ModelProfiles.ByRule(set, new AgentJob(JobKinds.Chat, "x", Project: "Work"))?.Profile.Name);
        Assert.Null(ModelProfiles.ByRule(set, new AgentJob(JobKinds.Fettle, "x")));
        Assert.Equal("rule type: bug", ModelProfiles.ByRule(set, new AgentJob(JobKinds.Card, "x", "Bug"))?.Why);
    }

    [Fact]
    public void HeaderSettings()
    {
        var set = ModelProfiles.Parse("# P\nAsk Haiku: no\nOtherwise: Mid\n\n## Mid\nModel: sonnet\n");
        Assert.False(set.AskHaiku);
        Assert.Equal("Mid", set.Otherwise);
        Assert.Equal(AgentChoices.Default, set.Profiles[0].Effort);
        Assert.Equal("sonnet", set.Profiles[0].Summary);
    }

    [Theory]
    [InlineData("Deep | a design question across many files", "Deep", "Haiku: a design question across many files")]
    [InlineData("**Deep** - needs judgement", "Deep", "Haiku: needs judgement")]
    [InlineData("Profile: quick: a rename", "Quick", "Haiku: a rename")]
    [InlineData("\"Standard\"", "Standard", "picked by Haiku")]
    [InlineData("Standard (one bug fix).", "Standard", "Haiku: one bug fix")]
    [InlineData("I'd go with Deep here because it's vague.", "Deep", "picked by Haiku")]
    [InlineData("\n\nQuick|typo\nsecond line", "Quick", "Haiku: typo")]
    public void ReadsHaikusReply(string reply, string profile, string why)
    {
        var pick = ModelProfiles.ReadReply(Starter, reply);
        Assert.Equal(profile, pick?.Profile.Name);
        Assert.Equal(why, pick?.Why);
    }

    [Theory]
    [InlineData("Quickly done, I think Standard or Deep")] // two names: no pick
    [InlineData("I can't tell")]
    [InlineData("")]
    public void RefusesAnUnclearReply(string reply) => Assert.Null(ModelProfiles.ReadReply(Starter, reply));

    [Fact]
    public async Task PickFallsBackWithoutHaiku()
    {
        var set = ModelProfiles.Parse("Ask Haiku: no\nOtherwise: B\n## A\nModel: haiku\n## B\nModel: opus\n## Rules\n- kind: fettle -> A\n");
        Assert.Equal("A", (await ModelProfiles.Pick(set, new AgentJob(JobKinds.Fettle, "hi")))?.Profile.Name);
        Assert.Equal("B", (await ModelProfiles.Pick(set, new AgentJob(JobKinds.Chat, "hi")))?.Profile.Name);
        Assert.Null(await ModelProfiles.Pick(ProfileSet.None, new AgentJob(JobKinds.Chat, "hi")));
        Assert.Null(await ModelProfiles.Pick(set with { Otherwise = null }, new AgentJob(JobKinds.Chat, "hi")));
    }

    [Fact] // WOR-0233: whether Haiku is asked is known before its pick, and the hooks an agent starts with meanwhile
    public async Task StartPickSaysFirstWhetherHaikuIsAsked()
    {
        var set = ModelProfiles.Parse("Otherwise: B\n## A\nModel: haiku\nHooks: off\n## B\nModel: opus\n## Rules\n- kind: fettle -> A\n");
        var byRule = ModelProfiles.StartPick(new AgentJob(JobKinds.Fettle, "hi"), () => set);
        Assert.Null(await byRule.Waiting);
        Assert.Equal("A", (await byRule.Result)?.Profile.Name);

        Assert.True(ModelProfiles.AsksHaiku(set, new AgentJob(JobKinds.Chat, "hi")));
        Assert.False(ModelProfiles.AsksHaiku(set, new AgentJob(JobKinds.Chat, " ")));
        Assert.False(ModelProfiles.AsksHaiku(set with { AskHaiku = false }, new AgentJob(JobKinds.Chat, "hi")));
        var allOff = ModelProfiles.Parse("## A\nModel: haiku\nHooks: off\n## B\nModel: opus\nHooks: off\n");
        Assert.True(ModelProfiles.AsksHaiku(allOff, new AgentJob(JobKinds.Chat, "hi")));
        // a queue's profile (WOR-0246) is the pick at once; one that's gone leaves Haiku to pick
        Assert.False(ModelProfiles.AsksHaiku(set, new AgentJob(JobKinds.Chat, "hi", Profile: "B")));
        Assert.True(ModelProfiles.AsksHaiku(set, new AgentJob(JobKinds.Chat, "hi", Profile: "Gone")));
        var named = ModelProfiles.StartPick(new AgentJob(JobKinds.Chat, "hi", Profile: "B"), () => set);
        Assert.Null(await named.Waiting);
        Assert.Equal("B", (await named.Result)?.Profile.Name);

        var failing = ModelProfiles.StartPick(new AgentJob(JobKinds.Chat, "hi"), () => throw new IOException("unreadable"));
        Assert.Null(await failing.Waiting); // settled even when the read fails, so nothing waits on it for ever
        await Assert.ThrowsAsync<IOException>(() => failing.Result);
    }

    [Fact]
    public void LoadWritesTheStarterOnce()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wb-profiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(3, ModelProfiles.Load(dir).Profiles.Count);
            File.WriteAllText(ModelProfiles.FilePath(dir), "## Only\nModel: sonnet\n");
            Assert.Equal("Only", Assert.Single(ModelProfiles.Load(dir).Profiles).Name);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AskPromptCarriesProfilesAndTask()
    {
        var p = ModelProfiles.AskPrompt(Starter, new AgentJob(JobKinds.Card, "Fix the typo in the header", "Bug", ["ui"]));
        Assert.Contains("- Deep: design and planning", p);
        Assert.Contains("Card type: Bug", p);
        Assert.Contains("Labels: ui", p);
        Assert.EndsWith("Fix the typo in the header", p);
    }
}
