using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0159: what Optimise tokens lowers an agent to.</summary>
public class TokenSaverTests
{
    [Theory]
    [InlineData("fable", "opus")]
    [InlineData("opus", "sonnet")]
    [InlineData("claude-opus-5-5", "sonnet")]
    [InlineData("opus[1m]", "sonnet")]
    [InlineData("sonnet", "haiku")]
    [InlineData("haiku", "haiku")]
    [InlineData("some-other-model", "some-other-model")]
    public void A_model_goes_one_step_down(string given, string expected) => Assert.Equal(expected, TokenSaver.LowerModel(given));

    [Fact]
    public void The_default_model_goes_down_from_what_it_is()
    {
        Assert.Equal("sonnet", TokenSaver.LowerModel(null)); // unknown default: taken as Opus
        Assert.Equal("sonnet", TokenSaver.LowerModel(AgentChoices.Default, "claude-opus-5-5"));
        Assert.Equal("haiku", TokenSaver.LowerModel(null, "sonnet"));
        Assert.Null(TokenSaver.LowerModel(null, "haiku")); // already the cheapest: stays on the default
    }

    [Theory]
    [InlineData(null, "medium")]
    [InlineData("max", "medium")]
    [InlineData("xhigh", "medium")]
    [InlineData("high", "medium")]
    [InlineData("medium", "medium")]
    [InlineData("low", "low")]
    public void Effort_is_capped_at_medium(string? given, string expected) => Assert.Equal(expected, TokenSaver.LowerEffort(given));

    [Fact]
    public void Off_changes_nothing_and_a_pick_by_hand_is_left_alone()
    {
        Assert.Equal(("opus", "high"), TokenSaver.Running("opus", "high", on: false));
        Assert.Equal(("sonnet", "medium"), TokenSaver.Running("opus", "high", on: true));
        Assert.Equal(("opus", "medium"), TokenSaver.Running("opus", "high", modelByHand: true, on: true));
        Assert.Equal(("sonnet", "max"), TokenSaver.Running("opus", "max", effortByHand: true, on: true));
    }

    [Fact]
    public void It_is_kept_in_the_instance_settings()
    {
        var folder = Directory.CreateTempSubdirectory("darg-tokensaver-").FullName;
        try
        {
            Assert.Null(TokenSaver.Set(true, folder));
            Assert.True(AppSettings.Load(folder).OptimiseTokens);
            TokenSaver.Load(folder);
            Assert.True(TokenSaver.On);
            Assert.Null(TokenSaver.Set(false, folder));
            Assert.False(AppSettings.Load(folder).OptimiseTokens);
            Assert.False(TokenSaver.On);
        }
        finally { TokenSaver.Set(false, folder); Directory.Delete(folder, true); }
    }
}
