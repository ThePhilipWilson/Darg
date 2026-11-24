using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>DAR-0001: the API's terminal command reads what a console shows, so Fettle can see it.</summary>
public class TerminalReadTests
{
    [AvaloniaFact]
    public async Task Terminal_command_returns_the_last_lines_a_console_shows()
    {
        using var app = DargApp.Make();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Terminal" });
        var consoles = app.Project.Consoles ?? throw new InvalidOperationException("no consoles");
        consoles.Run(new SavedCommand { Name = "say", Command = "echo marker-alpha-7431; echo marker-omega-9152" });

        await app.Until(() => consoles.Read(null, 200).Contains("marker-omega-9152"), "the console's output");

        var text = await app.Api("terminal", new { project = "proj", lines = 200 });
        Assert.StartsWith("say", text);
        Assert.Contains("marker-alpha-7431", text);

        var last = await app.Api("terminal", new { project = "proj", console = "say", lines = 1 });
        Assert.Contains("marker-omega-9152", last);
        Assert.DoesNotContain("marker-alpha-7431", last);
    }
}
