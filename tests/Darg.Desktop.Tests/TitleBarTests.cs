using Avalonia;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace Darg.Desktop.Tests;

public class TitleBarTests
{
    [AvaloniaFact] // WOR-0128: the bar's empty stretch is what a drag moves the window by
    public async Task The_empty_part_of_the_top_bar_is_hit_and_belongs_to_the_title_bar()
    {
        using var app = DargApp.Make();
        await app.Start();
        var w = app.Window;
        // between the wordmark and the right-hand buttons, well inside the 40 px bar
        var hit = w.GetVisualAt(new Point(w.Bounds.Width / 2, 20), x => x.IsVisible && x is not Avalonia.Controls.Primitives.Popup);
        Assert.NotNull(hit);
        var role = (hit as Avalonia.Visual)!.GetSelfAndVisualAncestors().OfType<AvaloniaObject>()
            .Select(o => o.GetValue(WindowDecorationProperties.ElementRoleProperty)).First(r => r != WindowDecorationsElementRole.None);
        Assert.Equal(WindowDecorationsElementRole.TitleBar, role);
    }
}
