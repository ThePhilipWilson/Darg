using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

public sealed class UpdaterTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-update-").FullName;

    [Fact]
    public void Knows_its_repo_only_from_a_source_file_naming_a_real_one()
    {
        var install = Directory.CreateDirectory(Path.Combine(_dir, "install")).FullName;
        var repo = Path.Combine(_dir, "repo");
        Assert.Null(Updater.SourceRepo(install)); // a debug build: no source.txt

        File.WriteAllText(Path.Combine(install, Updater.SourceFile), repo + "\r\n");
        Assert.Null(Updater.SourceRepo(install)); // names a folder that isn't Darg's repo

        Directory.CreateDirectory(Path.Combine(repo, "src", "Darg.Desktop"));
        File.WriteAllText(Path.Combine(repo, "src", "Darg.Desktop", "Darg.Desktop.csproj"), "<Project/>");
        Assert.Equal(repo, Updater.SourceRepo(install));
    }

    [Fact]
    public void A_failed_build_shows_its_errors_once_else_its_last_lines()
    {
        var output = new[] { "Restoring...", "x.cs(3,1): error CS1002: ; expected [a.csproj]", "x.cs(3,1): error CS1002: ; expected [a.csproj]", "done" };
        var shown = Updater.Failure(output, 1);
        Assert.Contains("Nothing was changed", shown);
        Assert.Single(shown.Split('\n'), l => l.Contains("CS1002"));
        Assert.DoesNotContain("Restoring", shown);
        Assert.Contains("done", Updater.Failure(["Restoring...", "done"], 1));
    }

    /// <summary>The helper for real: process gone, new build copied over the old (files the build doesn't
    /// have left alone), staging removed, and the app started again with its arguments, spaces and all.</summary>
    [Fact]
    public void The_helper_copies_the_new_build_in_and_starts_the_app_again()
    {
        if (!OperatingSystem.IsWindows()) return;
        var install = Directory.CreateDirectory(Path.Combine(_dir, "install")).FullName;
        File.WriteAllText(Path.Combine(install, "app.dll"), "old");
        File.WriteAllText(Path.Combine(install, Updater.SourceFile), "repo");
        var staging = Directory.CreateDirectory(Updater.StagingFolder(install)).FullName;
        File.WriteAllText(Path.Combine(staging, "app.dll"), "new");
        Directory.CreateDirectory(Path.Combine(staging, "runtimes"));
        File.WriteAllText(Path.Combine(staging, "runtimes", "x.dll"), "new");
        var marker = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "with space")).FullName, "started.txt");
        var failed = Path.Combine(_dir, "failed.txt");

        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        gone.WaitForExit();
        var pwsh = FindOnPath("pwsh.exe");
        var script = Updater.HelperScript(gone.Id, pwsh,
            ["-NoProfile", "-Command", $"Set-Content -LiteralPath '{marker}' -Value ok"], staging, install, failed);
        var file = Path.Combine(_dir, "restart.ps1");
        File.WriteAllText(file, script);
        using (var run = Process.Start(new ProcessStartInfo(pwsh, $"-NoProfile -NonInteractive -File \"{file}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            Assert.True(run.WaitForExit(30_000));

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "app.dll")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "runtimes", "x.dll")));
        Assert.Equal("repo", File.ReadAllText(Path.Combine(install, Updater.SourceFile)));
        Assert.False(Directory.Exists(staging));
        Assert.False(File.Exists(failed));
        for (var i = 0; i < 100 && !File.Exists(marker); i++) Thread.Sleep(100);
        Assert.True(File.Exists(marker), "the app wasn't started again");
    }

    static string FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists) ?? exe;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (Exception) { } }
}
