using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>HOM-0001: a project's plugins, read from their folders, and their commands run for their pages.</summary>
public class PluginsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "darg-plugins-" + Guid.NewGuid().ToString("N"));

    public PluginsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    string Plugin(string name, string? json, bool page = true)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        if (json is not null) File.WriteAllText(Path.Combine(folder, PluginStore.FileName), json);
        if (page) File.WriteAllText(Path.Combine(folder, "index.html"), "<html><head></head><body>hi</body></html>");
        return folder;
    }

    [Fact]
    public void A_plugin_reads_its_name_icon_page_and_command()
    {
        var folder = Plugin("Disk Space", """
            { // comments and a trailing comma are fine
              "name": "Disk", "icon": "◫", "tip": "what's using the drives",
              "command": "bin/scan.exe --quiet \"two words\"",
            }
            """);
        var p = PluginStore.Load(folder);
        Assert.Equal("Disk", p.Name);
        Assert.Equal("disk-space", p.Id);
        Assert.Equal("◫", p.Icon);
        Assert.Equal("index.html", p.Page);
        Assert.Equal(["bin/scan.exe", "--quiet", "two words"], p.Command!);
    }

    [Fact]
    public void Folders_that_cant_be_read_are_named_with_why_and_the_rest_still_load()
    {
        var good = Plugin("good", """{"name": "Good"}""");
        var noJson = Plugin("nojson", null);
        var noPage = Plugin("nopage", """{"name": "No page"}""", page: false);
        var bad = Plugin("bad", "{ not json");
        var (plugins, problems) = PluginStore.For(new ProjectConfig { Plugins = [good, noJson, noPage, bad, Path.Combine(_dir, "missing")] });
        Assert.Equal("Good", Assert.Single(plugins).Name);
        Assert.Null(plugins[0].Command);
        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("has no plugin.json"));
        Assert.Contains(problems, p => p.Contains("page index.html isn't in"));
        Assert.Contains(problems, p => p.Contains("doesn't read"));
    }

    [Fact]
    public async Task A_run_gives_its_output_lines_and_exit_code_and_can_be_stopped()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Plugin("echo", """{"name": "Echo", "command": "cmd /c run.cmd"}""");
        File.WriteAllText(Path.Combine(folder, "run.cmd"),
            "@echo off\r\nif \"%1\"==\"wait\" (echo waiting & waitfor /t 30 dargneverfires >nul 2>&1 & exit /b 0)\r\necho first %1\r\necho second %2\r\necho oops 1>&2\r\nexit /b 3\r\n");
        var runner = new PluginRunner();
        var plugin = PluginStore.Load(folder);

        var job = runner.Start(plugin, ["a", "b c"]);
        Assert.Same(job, runner.Find(job.Id));
        await Until(() => job.Done);
        var (lines, done, exit, errors) = job.Read(0);
        Assert.True(done);
        Assert.True(lines.Count == 2, $"exit {exit}, errors {errors}, lines {string.Join("|", lines)}");
        Assert.Equal(["first a", "second \"b c\""], lines);
        Assert.Equal(3, exit);
        Assert.Contains("oops", errors);
        Assert.Equal(["second \"b c\""], job.Read(1).Lines);

        var slow = runner.Start(plugin, ["wait"]);
        await Until(() => slow.Read(0).Lines.Count > 0);
        Assert.True(runner.Stop(slow.Id));
        await Until(() => slow.Done);
        Assert.False(runner.Stop(slow.Id));
    }

    [Fact]
    public void A_plugin_without_a_command_or_with_one_that_isnt_there_says_so()
    {
        var runner = new PluginRunner();
        var pageOnly = PluginStore.Load(Plugin("page", """{"name": "Page"}"""));
        Assert.Contains("has no command", Assert.Throws<InvalidOperationException>(() => runner.Start(pageOnly, [])).Message);
        var missing = PluginStore.Load(Plugin("gone", """{"name": "Gone", "command": "no-such-program-here.exe"}"""));
        Assert.Contains("couldn't start no-such-program-here.exe", Assert.Throws<InvalidOperationException>(() => runner.Start(missing, [])).Message);
    }

    [Fact]
    public void A_plugin_page_gets_the_bridge_with_run_and_the_theme()
    {
        using var server = ArtifactServer.Start(_dir, FreePort());
        server.Theme = () => new Dictionary<string, string> { ["Bg"] = "#123456" };
        var html = server.WithPluginBridge("<html><head><title>x</title></head><body></body></html>", "home", "disk");
        Assert.Contains("window.darg={theme:theme,run:run", html);
        Assert.Contains("#123456", html);
        Assert.Contains("'/prun/'+T+'/'+P+'/'+G", html);
        Assert.True(html.IndexOf("<script>", StringComparison.Ordinal) < html.IndexOf("<title>", StringComparison.Ordinal), "the bridge goes first in the head");
    }

    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException();
            await Task.Delay(50);
        }
    }
}
