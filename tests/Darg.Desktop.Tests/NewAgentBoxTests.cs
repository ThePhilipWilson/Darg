using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0238: the new-agent box has + New agent and Profiles only; the default-context button went when
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
        var buttons = app.Window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Contains(buttons, b => b.Content as string == "+ New agent");
        Assert.DoesNotContain(buttons, b => (b.Content as string ?? "").Contains("Context"));
        Assert.Equal("", app.Crashes());
    }
}
