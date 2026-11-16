using System.Globalization;
using System.Text.RegularExpressions;

namespace Workbench.Core;

/// <summary>Running out of usage (the 5-hour, session or weekly limit on a subscription): telling a turn that
/// failed for that from any other error, and when the limit resets. Claude Code says so two ways: a
/// rate_limit_event with status "rejected" and resetsAt (Unix seconds), which is the one to trust, and the
/// turn's error text, which is all an older CLI gives. Seen in real transcripts:
/// "You've hit your 5-hour limit · resets 6:20pm (Europe/London)", "You've hit your session limit · resets 5:30am
/// (Europe/London)", and from older versions "Claude AI usage limit reached|1757036400".</summary>
public static partial class UsageLimit
{
    /// <summary>A margin after the reset before carrying on, so the first message isn't refused by a clock a little behind.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);
    /// <summary>When the text says the limit was hit but not when it resets: try again after this long.</summary>
    public static readonly TimeSpan Unknown = TimeSpan.FromMinutes(30);

    /// <summary>Whether this error text is a usage limit (not some other failure).</summary>
    public static bool IsLimit(string? text) => text is { Length: > 0 } && LimitText().IsMatch(text);

    /// <summary>When the limit in this text resets, if it says (null if not).</summary>
    public static DateTimeOffset? ResetFrom(string? text, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var epoch = EpochReset().Match(text);
        if (epoch.Success && long.TryParse(epoch.Groups[1].Value, out var secs)) return DateTimeOffset.FromUnixTimeSeconds(secs);

        var m = ClockReset().Match(text);
        if (!m.Success) return null;
        var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Groups["ap"].Success)
        {
            if (hour is < 1 or > 12) return null;
            hour = hour % 12 + (m.Groups["ap"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        }
        if (hour > 23 || minute > 59) return null;

        var zone = m.Groups["tz"].Success ? Zone(m.Groups["tz"].Value.Trim()) : TimeZoneInfo.Local;
        var there = TimeZoneInfo.ConvertTime(now, zone);
        DateTime at;
        if (m.Groups["mon"].Success)
        {
            if (!DateTime.TryParseExact(m.Groups["mon"].Value[..3], "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var mon)) return null;
            var day = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
            if (day > DateTime.DaysInMonth(there.Year, mon.Month)) return null;
            at = new DateTime(there.Year, mon.Month, day, hour, minute, 0);
            if (at < there.DateTime.AddDays(-2)) at = at.AddYears(1); // "Jan 2" said on Dec 30
        }
        else
        {
            at = there.Date.AddHours(hour).AddMinutes(minute);
            if (at <= there.DateTime) at = at.AddDays(1); // a time already gone today means tomorrow
        }
        return new DateTimeOffset(at, zone.GetUtcOffset(at));
    }

    /// <summary>When to carry on after a limit: its reset (from the event if there was one, else the text) plus
    /// a margin, or a while from now when neither says.</summary>
    public static DateTimeOffset ContinueAt(DateTimeOffset? reported, string? text, DateTimeOffset now) =>
        reported is { } r && r > now.AddDays(-1) ? r + Margin
        : ResetFrom(text, now) is { } t ? t + Margin
        : now + Unknown;

    static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Local; }
    }

    [GeneratedRegex(@"hit your [\w -]*limit|usage limit|limit reached|out of (extra )?usage", RegexOptions.IgnoreCase)]
    private static partial Regex LimitText();

    [GeneratedRegex(@"limit reached\|(\d{9,11})", RegexOptions.IgnoreCase)]
    private static partial Regex EpochReset();

    // "resets 6:20pm (Europe/London)", "resets 5am", "resets Nov 20, 5pm (Europe/London)", "resets Nov 20 at 17:00"
    [GeneratedRegex(@"resets\s+(?:at\s+)?(?:(?<mon>[A-Za-z]{3,9})\s+(?<day>\d{1,2})(?:st|nd|rd|th)?,?\s+(?:at\s+)?)?(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?(?:\s*\((?<tz>[^)]+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ClockReset();
}
