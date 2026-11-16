using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The usage bar and timer in the header (WOR-0069): how much of the 5-hour window is used and how
/// long until it resets, with the week beside it. Fed by <see cref="UsageMeter.Shared"/>, which every agent
/// turn updates; the countdown ticks on its own between turns.</summary>
public partial class UsageViewModel : ObservableObject
{
    /// <summary>The bar's full width in the header, in pixels.</summary>
    public const double BarWidth = 64;
    readonly UsageMeter _meter;
    readonly DispatcherTimer _tick;

    public UsageViewModel(UsageMeter? meter = null)
    {
        _meter = meter ?? UsageMeter.Shared;
        _meter.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _meter.Attach();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
        Refresh();
    }

    [ObservableProperty] public partial bool Visible { get; set; }
    [ObservableProperty] public partial double FillWidth { get; set; }
    /// <summary>"37%".</summary>
    [ObservableProperty] public partial string Percent { get; set; } = "";
    /// <summary>"2h 14m" until the 5-hour window resets; empty when unknown.</summary>
    [ObservableProperty] public partial string Timer { get; set; } = "";
    /// <summary>"wk 55%".</summary>
    [ObservableProperty] public partial string Week { get; set; } = "";
    /// <summary>80% or more of the 5-hour window, or the week, used, or out of usage until a reset: shown red.</summary>
    [ObservableProperty] public partial bool High { get; set; }
    /// <summary>The last reading is over an hour old, so it may be behind (only Darg's own agents report it).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Opacity))] public partial bool Stale { get; set; }
    /// <summary>A stale reading shows faded.</summary>
    public double Opacity => Stale ? 0.55 : 1;
    [ObservableProperty] public partial string Tip { get; set; } = "";

    void Refresh()
    {
        var now = DateTimeOffset.Now;
        var five = _meter.Window(UsageWindow.FiveHour);
        var week = _meter.Window(UsageWindow.Week);
        Visible = five is not null || week is not null;
        if (!Visible) return;

        var fiveUsed = five?.UsedAt(now) ?? 0;
        var weekUsed = week?.UsedAt(now) ?? 0;
        FillWidth = Math.Round(BarWidth * fiveUsed);
        Percent = $"{fiveUsed * 100:0}%";
        Timer = five?.ResetsAt is { } r && r > now ? UsageMeter.Countdown(r, now) : "";
        Week = week is null ? "" : $"wk {weekUsed * 100:0}%";
        var outOfUsage = (five is { Status: "rejected" } && fiveUsed > 0) || (week is { Status: "rejected" } && weekUsed > 0);
        High = outOfUsage || fiveUsed >= 0.8 || weekUsed >= 0.8;
        Stale = _meter.SeenAt is not { } seen || now - seen > TimeSpan.FromHours(1);

        var lines = new List<string>();
        if (five is not null) lines.Add(Line("5-hour window", five, fiveUsed, now));
        if (week is not null) lines.Add(Line("Week", week, weekUsed, now));
        if (_meter.SeenAt is { } at)
            lines.Add($"As of {UsageMeter.Ago(at, now)}: the last turn any of Darg's agents took. Use in a terminal shows here after Darg's next turn.");
        Tip = string.Join("\n", lines);
    }

    static string Line(string name, UsageWindow w, double used, DateTimeOffset now) =>
        w.ResetsAt is not { } r ? $"{name}: {used * 100:0}% used"
        : r <= now ? $"{name}: reset at {UsageMeter.When(r, now)}"
        : $"{name}: {used * 100:0}% used{(w.Status == "rejected" ? " (limit hit)" : "")}, resets {UsageMeter.When(r, now)} (in {UsageMeter.Countdown(r, now)})";
}
