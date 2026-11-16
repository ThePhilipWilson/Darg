using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class UsageLimitTests
{
    // 16 Nov 2026, 17:19 in London (GMT, so UTC+0)
    static readonly DateTimeOffset Now = new(2026, 11, 16, 17, 19, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("You've hit your 5-hour limit · resets 6:20pm (Europe/London)")]
    [InlineData("You've hit your session limit · resets 5:30am (Europe/London)")]
    [InlineData("You've hit your weekly limit · resets Nov 20, 5pm (Europe/London)")]
    [InlineData("You've hit your limit · resets 3pm")]
    [InlineData("Claude AI usage limit reached|1757036400")]
    [InlineData("5-hour limit reached ∙ resets 3am")]
    [InlineData("Your org is out of usage · contact your admin")]
    public void Knows_a_usage_limit(string text) => Assert.True(UsageLimit.IsLimit(text));

    [Theory]
    [InlineData("API Error: 500 Internal server error")]
    [InlineData("Prompt is too long")]
    [InlineData("")]
    public void Other_errors_are_not_a_limit(string text) => Assert.False(UsageLimit.IsLimit(text));

    [Fact]
    public void A_time_later_today_is_today() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 18, 20, 0, TimeSpan.Zero),
            UsageLimit.ResetFrom("You've hit your 5-hour limit · resets 6:20pm (Europe/London)", Now));

    [Fact]
    public void A_time_already_gone_is_tomorrow() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 5, 30, 0, TimeSpan.Zero),
            UsageLimit.ResetFrom("You've hit your session limit · resets 5:30am (Europe/London)", Now));

    [Fact]
    public void The_named_zone_is_used() =>
        // 9am in New York is 2pm in London in November
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.FromHours(-5)),
            UsageLimit.ResetFrom("You've hit your limit · resets 9am (America/New_York)", Now));

    [Fact]
    public void A_date_is_read() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 17, 0, 0, TimeSpan.Zero),
            UsageLimit.ResetFrom("You've hit your weekly limit · resets Nov 20, 5pm (Europe/London)", Now));

    [Fact]
    public void A_date_early_next_year_is_next_year() =>
        Assert.Equal(new DateTimeOffset(2027, 1, 2, 17, 0, 0, TimeSpan.Zero),
            UsageLimit.ResetFrom("resets Jan 2 at 5pm (Europe/London)", new DateTimeOffset(2026, 12, 30, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void An_epoch_is_read() =>
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757036400), UsageLimit.ResetFrom("Claude AI usage limit reached|1757036400", Now));

    [Fact]
    public void No_time_said_gives_null() => Assert.Null(UsageLimit.ResetFrom("Your org is out of usage", Now));

    [Fact]
    public void Carries_on_after_the_reported_reset_first()
    {
        var reported = Now.AddHours(2);
        Assert.Equal(reported + UsageLimit.Margin, UsageLimit.ContinueAt(reported, "resets 9pm (Europe/London)", Now));
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 21, 0, 0, TimeSpan.Zero) + UsageLimit.Margin,
            UsageLimit.ContinueAt(null, "resets 9pm (Europe/London)", Now));
        Assert.Equal(Now + UsageLimit.Unknown, UsageLimit.ContinueAt(null, "out of usage", Now));
    }

    [Fact]
    public void A_rate_limit_event_is_parsed()
    {
        var e = Assert.IsType<UsageLimited>(Assert.Single(StreamParser.Parse(
            """{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1763317200,"rateLimitType":"five_hour","unifiedRateLimitFallbackAvailable":false},"uuid":"u","session_id":"s"}""")));
        Assert.True(e.Rejected);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763317200), e.ResetsAt);
        Assert.Equal("five_hour", e.Kind);
        Assert.False(Assert.IsType<UsageLimited>(Assert.Single(StreamParser.Parse(
            """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}"""))).Rejected);
    }
}
