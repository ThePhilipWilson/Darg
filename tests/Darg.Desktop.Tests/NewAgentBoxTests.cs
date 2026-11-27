using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0238: the new-agent box has Start and Profiles only; the default-context button went when
/// the Context page took over its job.</summary>
public class NewAgentBoxTests
{
    [AvaloniaFact]
    public async Task The_new_agent_box_has_no_default_context_button()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Agents" });
        await DargApp.Settle();
        var open = app.Window.GetVisualDescendants().OfType<Button>().First(b =>
            b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "New agent");
        var flyout = (Flyout)open.Flyout!;
        flyout.ShowAt(open);
        await DargApp.Settle();
        var buttons = ((Control)flyout.Content!).GetVisualDescendants().OfType<Button>().ToList();
        Assert.Contains(buttons, b => b.Content as string == "Start");
        Assert.Contains(buttons, b => b.Content as string == "✎ Profiles");
        Assert.DoesNotContain(buttons, b => (b.Content as string ?? "").Contains("Context"));
        Assert.Equal("", app.Crashes());
    }
}
