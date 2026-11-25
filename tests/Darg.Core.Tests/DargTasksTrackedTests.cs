using System.Diagnostics;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>DAR-0003: Darg's own boards and cards (.darg/tasks) are committed, cards made later too. A one-off force-add
/// left every new card ignored by the .darg/ line Darg writes into .git/info/exclude: those cards never reached a clone,
/// and git overwrote or left behind the ignored files on checkout (a board.json replaced by another machine's, a
/// renamed card's old file left as a duplicate). The repo's .gitignore takes .darg/tasks back in.</summary>
public sealed class DargTasksTrackedTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-dargtasks-").FullName;

    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Darg.sln"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void New_cards_are_seen_by_git_and_the_rest_of_dot_darg_stays_ignored()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_dir, "repo")).FullName;
        var instance = Directory.CreateDirectory(Path.Combine(_dir, "inst")).FullName;
        Git(repo, "init", "-q", ".");
        File.Copy(Path.Combine(Root(), ".gitignore"), Path.Combine(repo, ".gitignore"));
        new InstanceConfig { Projects = [new() { Id = "p", Name = "P", Repos = [repo] }] }.Save(instance);

        // Darg makes .darg as it does on every machine, excluding it
        var reports = Directory.CreateDirectory(ReportStore.FolderFor("p", instance)).FullName;
        Assert.Contains(".darg/", File.ReadAllLines(Path.Combine(repo, ".git", "info", "exclude")));
        File.WriteAllText(Path.Combine(reports, "r.md"), "report");
        var items = Directory.CreateDirectory(Path.Combine(repo, ".darg", "tasks", "Main", "items")).FullName;
        File.WriteAllText(Path.Combine(repo, ".darg", "tasks", "Main", "board.json"), "{}");
        File.WriteAllText(Path.Combine(items, "WOR-0001 - a card made today.md"), "# WOR-0001: a card made today\n");

        var seen = Git(repo, "status", "--porcelain", "--untracked-files=all").Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("?? .darg/tasks/Main/board.json", seen);
        Assert.Contains("?? \".darg/tasks/Main/items/WOR-0001 - a card made today.md\"", seen);
        Assert.DoesNotContain(seen, l => l.Contains(".darg/reports"));
    }

    static string Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)}: {error}");
        return output;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }
}
