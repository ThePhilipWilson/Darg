using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Automation;

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
}
