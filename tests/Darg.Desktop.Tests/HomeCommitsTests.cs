using System.Diagnostics;
using Avalonia.Headless.XUnit;

namespace Darg.Desktop.Tests;

/// <summary>DAR-0002: Home's recent commits show at most a few per repo.</summary>
public class HomeCommitsTests
{
    [AvaloniaFact]
    public async Task Home_shows_only_the_newest_few_commits_of_a_repo()
    {
        using var app = DargApp.Make();
        for (var i = 1; i <= 6; i++)
        {
            File.WriteAllText(Path.Combine(app.Repo, $"f{i}.txt"), "x");
            Git(app.Repo, "add", ".");
            Git(app.Repo, "-c", "user.name=T", "-c", "user.email=t@example.com", "commit", "-q", "-m", $"Change {i}");
        }
        await app.Start();
        await app.Project.Home.Load();

        Assert.Equal(ViewModels.HomeViewModel.CommitsPerRepo, app.Project.Home.Commits.Count);
        Assert.Equal("Change 6", app.Project.Home.Commits[0].Subject);
        Assert.Equal("", app.Crashes());
    }

    static void Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException(p.StandardError.ReadToEnd());
    }
}
