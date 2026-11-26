using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace Darg.Desktop.Tests;

/// <summary>HOM-0001: a project's plugin folder shows as a rail section, its page served with a bridge that runs the
/// plugin's own command; a folder that can't be read is named in the footer.</summary>
public class PluginSectionTests
{
    [AvaloniaFact]
    public async Task A_plugin_folder_is_a_rail_section_whose_page_runs_the_plugins_command()
    {
        using var app = DargApp.Make();
        var folder = Directory.CreateDirectory(Path.Combine(app.Folder, "plugins", "echo")).FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """{"name": "Echo", "icon": "✺", "tip": "says it back", "command": "cmd /c run.cmd"}""");
        File.WriteAllText(Path.Combine(folder, "index.html"), "<html><head><title>Echo</title></head><body>the plugin page body</body></html>");
        File.WriteAllText(Path.Combine(folder, "run.cmd"), "@echo off\r\necho %1 from the plugin\r\necho second line\r\n");
        var broken = Directory.CreateDirectory(Path.Combine(app.Folder, "plugins", "broken")).FullName;
        var file = Path.Combine(app.Home, "instance.json");
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        json["projects"]![0]!["plugins"] = new JsonArray(folder, broken);
        File.WriteAllText(file, json.ToJsonString());

        await app.Start();
        var section = Assert.Single(app.Project.Plugins);
        Assert.Contains("plugin not loaded", app.Project.StatusLine);
        Assert.Contains("broken", app.Project.StatusLine);
        await DargApp.Settle(100);
        var button = app.Window.GetVisualDescendants().OfType<ToggleButton>().Single(b => AutomationProperties.GetName(b) == "Echo");
        Assert.True(button.IsEffectivelyVisible);
        Assert.False(button.IsChecked);
        Assert.Null(section.Page); // nothing loads until it's opened

        button.Command!.Execute(button.CommandParameter);
        await DargApp.Settle(100);
        Assert.True(app.Project.OnPlugin);
        Assert.Same(section, app.Project.ShownPlugin);
        Assert.True(section.Showing);
        Assert.True(button.IsChecked);
        var page = section.Page ?? throw new InvalidOperationException("The plugin's page wasn't set when it was opened.");
        app.Shot("plugin-open");

        // the page is served with the bridge, which runs the plugin's command as the page would
        using var http = new HttpClient();
        var html = await http.GetStringAsync(page);
        Assert.Contains("the plugin page body", html);
        Assert.Contains("window.darg={theme:theme,run:run", html);
        var token = Regex.Match(html, "var T=\"([0-9a-f]+)\"").Groups[1].Value;
        var root = page.GetLeftPart(UriPartial.Authority);
        var started = await http.PostAsJsonAsync($"{root}/prun/{token}/proj/echo", new[] { "hello" });
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
        var job = (string)JsonNode.Parse(await started.Content.ReadAsStringAsync())!["job"]!;
        JsonNode state = new JsonObject();
        await app.Until(() =>
        {
            state = JsonNode.Parse(http.GetStringAsync($"{root}/pjob/{token}/{job}?from=0").Result)!;
            return (bool)state["done"]!;
        }, "the plugin's command to finish");
        Assert.Equal(["hello from the plugin", "second line"], state["lines"]!.AsArray().Select(l => (string)l!));
        Assert.Equal(0, (int)state["exit"]!);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await http.PostAsJsonAsync($"{root}/prun/not-the-token/proj/echo", new[] { "x" })).StatusCode);

        // another section hides it; opening it again keeps the page that was loaded
        app.Project.OpenCommand.Execute("Board");
        Assert.False(section.Showing);
        Assert.False(app.Project.OnPlugin);
        app.Project.OpenPluginCommand.Execute(section);
        Assert.True(section.Showing);
        Assert.Equal(page, section.Page);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_project_with_no_plugins_has_no_plugin_section()
    {
        using var app = DargApp.Make();
        await app.Start();
        Assert.Empty(app.Project.Plugins);
        Assert.False(app.Project.Has(Darg.Desktop.ViewModels.Section.Plugin));
        Assert.DoesNotContain("plugin not loaded", app.Project.StatusLine);
    }
}
