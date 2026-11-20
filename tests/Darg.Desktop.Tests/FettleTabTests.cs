using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Automation;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

public class FettleTabTests
{
    [AvaloniaFact]
    public async Task Clicking_the_open_chat_tab_keeps_it_highlighted()
    {
        using var app = DargApp.Make();
        await app.Start();
        app.Main.Mascot.Open = true;
        await DargApp.Settle();
        var tab = app.Window.GetVisualDescendants().OfType<ToggleButton>()
            .Single(b => AutomationProperties.GetName(b) == "Fettle chat tab");
        Assert.True(tab.IsChecked);
        var at = tab.TranslatePoint(new Avalonia.Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), app.Window)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(app.Window, at, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(app.Window, at, Avalonia.Input.MouseButton.Left);
        await DargApp.Settle();
        Assert.True(tab.IsChecked, "the open Chat tab lost its highlight when clicked");
        Assert.False(app.Main.ShowNotifications);
    }

    /// <summary>WOR-0163: an entry waiting on my answer carries a "needs you" mark in the Notifications list; one that doesn't, doesn't.</summary>
    [AvaloniaFact]
    public async Task Notifications_waiting_on_a_decision_are_marked()
    {
        using var app = DargApp.Make();
        await app.Start();
        var now = DateTime.Now;
        app.Main.Notifications.Insert(0, new NotificationEntry("proj", "a1", "Proj", "Finished agent", "finished after 20 min", "all done", now));
        app.Main.Notifications.Insert(0, new NotificationEntry("proj", "a2", "Proj", "Asking agent", "has a question", "Which one?", now));
        app.Main.Notifications.Insert(0, new NotificationEntry("proj", "a3", "Proj", "Tool agent", "wants to use Bash", "rm -rf x", now));
        app.Main.Notifications.Insert(0, new NotificationEntry("proj", MainViewModel.CardPrefix + "WOR-1", "Proj", "WOR-1 voice", NotificationEntry.DecisionWaits, "Works?", now));
        app.Main.Mascot.Open = true;
        app.Main.ShowNotifications = true;
        await DargApp.Settle();
        app.Shot("notifications-marked");

        string? Marked(NotificationEntry n) => app.Window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext == n && b.CommandParameter == n).SelectMany(b => b.GetVisualDescendants().OfType<Border>())
            .Where(x => AutomationProperties.GetName(x) == "Needs your decision" && x.IsEffectivelyVisible)
            .Select(x => (x.Child as TextBlock)?.Text).FirstOrDefault();
        foreach (var n in app.Main.Notifications)
            if (n.What.StartsWith("finished")) Assert.Null(Marked(n));
            else Assert.Equal("needs you", Marked(n));
    }
}
