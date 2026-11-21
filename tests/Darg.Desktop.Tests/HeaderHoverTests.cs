using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0185: the four top bar buttons (Tools, Restart, Setup, Settings) look the same with the pointer over them:
/// Fluent's :pointerover style must not change their background or text colour.</summary>
public class HeaderHoverTests
{
    static string Paint(IBrush? b) => b is ISolidColorBrush s ? s.Color.ToString() : b?.GetType().Name ?? "none";

    [AvaloniaFact]
    public async Task Top_bar_buttons_look_the_same_when_hovered_in_every_theme()
    {
        using var app = DargApp.Make();
        await app.Start();

        foreach (var theme in Themes.All)
        {
            await app.Api("theme", new { theme = theme.Id });
            await DargApp.Settle();
            HeadlessWindowExtensions.MouseMove(app.Window, new Point(5, 400));
            await DargApp.Settle();

            foreach (var label in new[] { "Tools", "Settings", "Setup" }.Append("RESTART"))
            {
                var button = app.Window.GetVisualDescendants().OfType<Button>().First(b =>
                    label == "RESTART" ? b.Name == "RestartButton" : b.Content is string s && s.EndsWith(label));
                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                var before = (Paint(presenter.Background), Paint(presenter.Foreground), presenter.Bounds);

                var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), app.Window)!.Value;
                HeadlessWindowExtensions.MouseMove(app.Window, at);
                await DargApp.Settle();
                Assert.True(button.IsPointerOver, $"{theme.Id}/{label}: the pointer should be over the button");
                var after = (Paint(presenter.Background), Paint(presenter.Foreground), presenter.Bounds);
                HeadlessWindowExtensions.MouseMove(app.Window, new Point(5, 400));
                await DargApp.Settle();

                Assert.True(before == after, $"{theme.Id}/{label}: hovering changed it from {before} to {after}");
            }
        }
    }
}
