using Darg.Core;

namespace Darg.Core.Tests;

public sealed class WorkTimerTests : IDisposable
{
    static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Break = TimeSpan.FromMinutes(5);
    readonly string _dir = Path.Combine(Path.GetTempPath(), "darg-timer-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static WorkTimer.Turn T(string s, double from, double? to) =>
        new(s, T0.AddMinutes(from), to is { } t ? T0.AddMinutes(t) : null);
    static WorkDay M(DateTimeOffset now, params WorkTimer.Turn[] turns) => WorkTimer.Measure(turns, T0.AddHours(-8), now, Break);

    static string Ev(string session, string ev, double minutes) =>
        $$"""{"session_id":"{{session}}","event":"{{ev}}","ts":"{{T0.AddMinutes(minutes).UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}"}""";

    [Fact]
    public void Turns_are_read_from_a_session_file()
    {
        var turns = WorkTimer.TurnsIn([
            "﻿" + """{"platform":"win32","project":"x","session_id":"a","event":"session_start","ts":"2026-11-16T11:59:00.000Z"}""",
            Ev("a", "prompt_submit", 0), Ev("a", "agent_stop", 10),
            "not json", """{"event":"prompt_submit"}""",
            Ev("a", "prompt_submit", 12)]);
        Assert.Equal(2, turns.Count);
        Assert.Equal(T0.AddMinutes(10), turns[0].End);
        Assert.Null(turns[1].End);
    }

    [Fact]
    public void A_prompt_cut_off_by_another_ends_there() =>
        Assert.Equal(T0.AddMinutes(3), WorkTimer.TurnsIn([Ev("a", "prompt_submit", 0), Ev("a", "prompt_submit", 3)])[0].End);

    [Fact]
    public void Short_gaps_count_and_breaks_do_not()
    {
        // 0-10 busy, 3 min reading, 13-20 busy, 40 min break, 60-70 busy
        var d = M(T0.AddHours(3), T("a", 0, 10), T("a", 13, 20), T("a", 60, 70));
        Assert.Equal(TimeSpan.FromMinutes(27), d.Busy);
        Assert.Equal(TimeSpan.FromMinutes(30), d.Active);
        Assert.Equal(3, d.Turns);
        Assert.False(d.Working);
    }

    [Fact]
    public void Sessions_running_at_once_count_once()
    {
        var d = M(T0.AddHours(3), T("a", 0, 30), T("b", 10, 20), T("c", 25, 40));
        Assert.Equal(TimeSpan.FromMinutes(40), d.Active);
        Assert.Equal(3, d.Sessions);
    }

    [Fact]
    public void A_running_turn_counts_up_to_now()
    {
        var d = M(T0.AddMinutes(25), T("a", 0, null));
        Assert.Equal(TimeSpan.FromMinutes(25), d.Active);
        Assert.True(d.Working);
    }

    [Fact]
    public void A_turn_that_never_stopped_is_capped()
    {
        var d = M(T0.AddHours(6), T("a", 0, null));
        Assert.Equal(WorkTimer.OpenTurnCap, d.Active);
        Assert.False(d.Working);
    }

    [Fact]
    public void Only_today_counts() =>
        Assert.Equal(TimeSpan.FromMinutes(30), WorkTimer.Measure([T("a", -60, 30)], T0, T0.AddHours(1), Break).Active);

    [Fact]
    public void The_day_starts_at_four_in_the_morning()
    {
        var late = new DateTimeOffset(2026, 11, 17, 1, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 17, 1, 30, 0)));
        Assert.Equal(new DateTime(2026, 11, 16, 4, 0, 0), WorkTimer.DayStart(late).ToLocalTime().DateTime);
        var morning = late.AddHours(3);
        Assert.Equal(new DateTime(2026, 11, 17, 4, 0, 0), WorkTimer.DayStart(morning).ToLocalTime().DateTime);
    }

    [Fact]
    public void Today_reads_the_folder_and_rereads_a_file_that_grew()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "2026-11-16_12-00-00_a.jsonl");
        File.WriteAllLines(file, [Ev("a", "prompt_submit", 0), Ev("a", "agent_stop", 10)]);
        File.WriteAllLines(Path.Combine(_dir, "2026-11-16_12-00-00_b.jsonl"), [Ev("b", "prompt_submit", 5), Ev("b", "agent_stop", 15)]);
        var timer = new WorkTimer(_dir, Break);
        var now = T0.AddHours(1);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Today(now).Active);

        File.AppendAllLines(file, [Ev("a", "prompt_submit", 18), Ev("a", "agent_stop", 20)]);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(TimeSpan.FromMinutes(20), timer.Today(now).Active);
    }

    [Fact]
    public void Config_sets_the_folder_and_break()
    {
        var home = Path.Combine(_dir, "home");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        Assert.Null(WorkTimer.FromConfig(home)); // not installed: no timings folder
        Directory.CreateDirectory(Path.Combine(home, "t"));
        File.WriteAllText(Path.Combine(home, ".claude", "timer-config.json"), """{"timings_dir":"~/t","break_threshold_minutes":10}""");
        Assert.NotNull(WorkTimer.FromConfig(home));
    }
}
