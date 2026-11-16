using Darg.Core;

namespace Darg.Core.Tests;

public sealed class PreviewTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-preview-").FullName;

    [Theory]
    [InlineData("  ┃ Local    http://localhost:4321/", "http://localhost:4321/")]
    [InlineData("\u001b[32m  ➜  Local:\u001b[39m   \u001b[36mhttp://localhost:\u001b[1m5173\u001b[22m/\u001b[39m", "http://localhost:5173/")]
    [InlineData("Now listening on: http://127.0.0.1:5000.", "http://127.0.0.1:5000")]
    [InlineData("Server running at http://0.0.0.0:8080", "http://localhost:8080")]
    [InlineData("  - Local:        http://[::1]:3000", "http://[::1]:3000")]
    [InlineData("  ┃ Network  use --host to expose", null)]
    [InlineData("see https://docs.astro.build for help", null)]
    public void Reads_the_local_address_a_server_prints(string line, string? url) =>
        Assert.Equal(url, Preview.FirstLocalUrl(line));

    [Fact]
    public void Finds_a_dev_script_and_its_port()
    {
        File.WriteAllText(Path.Combine(_dir, "package.json"), """{ "scripts": { "dev": "astro dev" }, "dependencies": { "astro": "^5.0.0" } }""");
        var p = Preview.Detect(_dir)!;
        Assert.Equal(("npm run dev", "http://localhost:4321/", _dir), (p.Command, p.Url, p.Cwd));
    }

    [Fact]
    public void A_project_without_a_dev_script_has_no_preview()
    {
        File.WriteAllText(Path.Combine(_dir, "package.json"), """{ "scripts": { "build": "tsc" } }""");
        Assert.Null(Preview.For(new ProjectConfig { Repos = [_dir] }));
    }

    [Fact]
    public void A_set_preview_wins_and_its_folder_is_under_the_repo()
    {
        File.WriteAllText(Path.Combine(_dir, "package.json"), """{ "scripts": { "dev": "vite" } }""");
        var p = Preview.For(new ProjectConfig { Repos = [_dir], Preview = new PreviewConfig { Command = "dotnet watch", Cwd = "web", Url = "http://localhost:5000" } })!;
        Assert.Equal(("dotnet watch", Path.Combine(_dir, "web")), (p.Command, p.Cwd));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }
}

public sealed class DevServerTests
{
    [Fact]
    public async Task Reads_the_address_and_stopping_ends_everything_it_started()
    {
        if (!OperatingSystem.IsWindows()) return;
        var t0 = DateTime.Now.AddSeconds(-1);
        // like npm: a shell that starts another shell that starts the long-running thing
        using var s = new DevServer("echo   Local  http://localhost:65000/ && cmd /c ping -n 60 127.0.0.1 >nul", Path.GetTempPath());
        var found = new TaskCompletionSource<string>();
        s.UrlFound += u => found.TrySetResult(u);
        Assert.Same(found.Task, await Task.WhenAny(found.Task, Task.Delay(10000)));
        Assert.Equal("http://localhost:65000/", await found.Task);
        List<int> Pings() => System.Diagnostics.Process.GetProcessesByName("PING")
            .Where(p => { try { return p.StartTime > t0; } catch (Exception) { return false; } }).Select(p => p.Id).ToList();
        for (var i = 0; i < 20 && Pings().Count == 0; i++) await Task.Delay(100);
        var started = Pings();
        Assert.NotEmpty(started);
        s.Stop();
        await Task.Delay(500);
        Assert.Empty(Pings().Intersect(started));
    }
}
