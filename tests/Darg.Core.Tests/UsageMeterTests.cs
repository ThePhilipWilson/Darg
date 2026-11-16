using Darg.Core;

namespace Darg.Core.Tests;

public sealed class UsageMeterTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 16, 17, 19, 0, TimeSpan.Zero);
    readonly string _dir = Path.Combine(Path.GetTempPath(), "darg-usage-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // as Claude Code 2.1.326 sends it, 2026-11-16
    const string Real = """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1794889800,"rateLimitType":"five_hour","overageStatus":"rejected","overageDisabledReason":"org_level_disabled","isUsingOverage":false,"unifiedWindows":{"five_hour":{"utilization":0.05,"resetsAt":1794889800,"status":"allowed"},"seven_day":{"utilization":0.55,"resetsAt":1794913200,"status":"allowed"}}},"uuid":"u","session_id":"s"}""";

    static UsageLimited Parse(string line) => Assert.IsType<UsageLimited>(Assert.Single(StreamParser.Parse(line)));

    [Fact]
    public void Both_windows_are_read_from_the_event()
    {
        var e = Parse(Real);
        Assert.Equal(2, e.Windows.Count);
        var five = e.Windows.Single(w => w.Kind == UsageWindow.FiveHour);
        Assert.Equal(0.05, five.Used, 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794889800), five.ResetsAt);
        Assert.Equal("allowed", five.Status);
        Assert.Equal(0.55, e.Windows.Single(w => w.Kind == UsageWindow.Week).Used, 3);
    }

    [Theory] // shapes the CLI might send instead: none of them may throw or invent a window
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":[]}}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":{"utilization":"lots"}}}}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":null}}}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":5,"resetsAt":"soon","unifiedWindows":{"five_hour":{}}}}""")]
    public void Odd_shapes_give_no_windows(string line) => Assert.Empty(Parse(line).Windows);

    [Fact]
    public void A_window_without_a_reset_still_counts() =>
        Assert.Null(Assert.Single(Parse("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":{"utilization":0.4}}}}""").Windows).ResetsAt);

    [Fact]
    public void A_report_is_kept_and_read_back_on_the_next_start()
    {
        var meter = new UsageMeter();
        meter.Attach(_dir);
        var changed = 0;
        meter.Changed += () => changed++;
        meter.Report(Parse(Real), Now);
        Assert.Equal(1, changed);
        Assert.Equal(Now, meter.SeenAt);

        var next = new UsageMeter();
        next.Attach(_dir);
        Assert.Equal(0.55, next.Window(UsageWindow.Week)!.Used, 3);
        Assert.Equal(Now, next.SeenAt);
    }

    [Fact]
    public void Nothing_is_written_until_attached()
    {
        new UsageMeter().Report(Parse(Real), Now);
        Assert.False(File.Exists(UsageMeter.FilePath(_dir)));
    }

    [Fact]
    public void A_bad_file_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(UsageMeter.FilePath(_dir), "{not json");
        var meter = new UsageMeter();
        meter.Attach(_dir);
        Assert.Empty(meter.Windows);
    }

    [Fact]
    public void An_older_cli_saying_rejected_marks_that_window_full()
    {
        var meter = new UsageMeter();
        meter.Report(Parse(Real), Now);
        meter.Report(Parse("""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1794889800,"rateLimitType":"five_hour"}}"""), Now);
        var five = meter.Window(UsageWindow.FiveHour)!;
        Assert.Equal(1, five.Used);
        Assert.Equal("rejected", five.Status);
        Assert.Equal(0.55, meter.Window(UsageWindow.Week)!.Used, 3); // the other window is kept
    }

    [Fact]
    public void An_allowed_event_with_no_windows_changes_nothing()
    {
        var meter = new UsageMeter();
        meter.Report(Parse(Real), Now);
        meter.Report(Parse("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}"""), Now.AddHours(1));
        Assert.Equal(Now, meter.SeenAt);
    }

    [Fact]
    public void A_window_past_its_reset_counts_as_unused()
    {
        var w = new UsageWindow(UsageWindow.FiveHour, 0.9, Now.AddMinutes(-1), "allowed");
        Assert.Equal(0, w.UsedAt(Now));
        Assert.Equal(0.9, w.UsedAt(Now.AddMinutes(-2)), 3);
    }

    [Theory]
    [InlineData(134, "2h 14m")]
    [InlineData(60, "1h 00m")]
    [InlineData(45, "45m")]
    [InlineData(0.5, "<1m")]
    [InlineData(0, "now")]
    [InlineData(-5, "now")]
    [InlineData(60 * 24 * 3 + 240, "3d 4h")]
    public void Countdown_reads_short(double minutes, string expected) =>
        Assert.Equal(expected, UsageMeter.Countdown(Now.AddMinutes(minutes), Now));

    [Theory]
    [InlineData(0.2, "just now")]
    [InlineData(12, "12 min ago")]
    [InlineData(180, "3 h ago")]
    [InlineData(60 * 30, "a day ago")]
    [InlineData(60 * 50, "2 days ago")]
    public void Ago_reads_short(double minutes, string expected) =>
        Assert.Equal(expected, UsageMeter.Ago(Now.AddMinutes(-minutes), Now));
}
