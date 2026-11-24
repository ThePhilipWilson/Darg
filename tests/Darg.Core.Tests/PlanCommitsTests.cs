using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0199: the Design page commits a plan file alone, pushes under the push rule, and keeps its own state (the
/// unticked rows, its planner chat) in the plan's properties.</summary>
public sealed class PlanCommitsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("darg-plan-commits-").FullName;

    const string Plan = "---\nkind: feature\nstatus: draft\n---\n# Phone\n\n## Goals\n- Answer from the sofa.\n\n## Cards\n\n### Pairing, with \"quotes\"\nWords.\n";

    (string Repo, string File) MakeRepo()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_dir, "repo")).FullName;
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "config", "user.name", "Darg tests");
        Git(repo, "config", "user.email", "tests@example.com");
        var design = Directory.CreateDirectory(Path.Combine(repo, "docs", "design")).FullName;
        var file = Path.Combine(design, "phone.md");
        File.WriteAllText(file, Plan);
        File.WriteAllText(Path.Combine(repo, "other.txt"), "one\n");
        Git(repo, "add", ".");
        Git(repo, "commit", "-q", "-m", "First");
        return (repo, file);
    }

    [Fact]
    public void Commits_only_the_plan_file_leaving_other_changes_and_staged_files_as_they_were()
    {
        var (repo, file) = MakeRepo();
        File.WriteAllText(Path.Combine(repo, "other.txt"), "changed\n");
        File.WriteAllText(Path.Combine(repo, "staged.txt"), "staged\n");
        Git(repo, "add", "staged.txt");
        PlanStore.SetSection(file, "Goals", "- Never miss a decision.");

        var r = PlanCommits.Commit(file, "edited Goals", null);

        Assert.Equal(PlanCommits.Outcome.Committed, r.Outcome);
        Assert.Equal("docs/design/phone.md", Git(repo, "show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("Design: phone.md: edited Goals", Git(repo, "log", "-1", "--format=%s").Trim());
        var status = Git(repo, "status", "--porcelain");
        Assert.Contains(" M other.txt", status);
        Assert.Contains("A  staged.txt", status);
        Assert.DoesNotContain("phone.md", status);
        // nothing changed since: nothing to commit, no empty commit
        Assert.Equal(PlanCommits.Outcome.Nothing, PlanCommits.Commit(file, "again", null).Outcome);
        Assert.Equal("2", Git(repo, "rev-list", "--count", "HEAD").Trim());
    }

    [Fact]
    public void A_new_plan_file_is_added_and_committed()
    {
        var (repo, _) = MakeRepo();
        var file = Path.Combine(repo, "docs", "design", "new.md");
        File.WriteAllText(file, Plan);
        Assert.Equal(PlanCommits.Outcome.Committed, PlanCommits.Commit(file, "its planner chat", CommitRule.Commit).Outcome);
        Assert.Equal("docs/design/new.md", Git(repo, "show", "--name-only", "--format=", "HEAD").Trim());
    }

    [Fact]
    public void Pushes_under_the_push_rule_only()
    {
        var (repo, file) = MakeRepo();
        var remote = Path.Combine(_dir, "remote.git");
        Git(_dir, "init", "-q", "--bare", "-b", "main", remote);
        Git(repo, "remote", "add", "origin", remote);
        Git(repo, "push", "-q", "-u", "origin", "main");

        PlanStore.SetProperty(file, "status", "agreed");
        Assert.Equal(PlanCommits.Outcome.Committed, PlanCommits.Commit(file, "agreed", CommitRule.Commit).Outcome);
        Assert.NotEqual(Git(repo, "rev-parse", "HEAD"), Git(remote, "rev-parse", "main"));

        PlanStore.SetProperty(file, "status", "dropped");
        var r = PlanCommits.Commit(file, "dropped", CommitRule.Push);
        Assert.Equal(PlanCommits.Outcome.Pushed, r.Outcome);
        Assert.Equal(Git(repo, "rev-parse", "HEAD"), Git(remote, "rev-parse", "main"));
        Assert.Equal("Committed and pushed.", r.Text);
    }

    [Fact]
    public void Never_and_no_repo_commit_nothing()
    {
        var (repo, file) = MakeRepo();
        PlanStore.SetProperty(file, "status", "agreed");
        Assert.Equal(PlanCommits.Outcome.Never, PlanCommits.Commit(file, "x", CommitRule.Never).Outcome);
        Assert.Equal("1", Git(repo, "rev-list", "--count", "HEAD").Trim());
        var loose = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "loose")).FullName, "plan.md");
        File.WriteAllText(loose, Plan);
        Assert.Equal(PlanCommits.Outcome.NotInGit, PlanCommits.Commit(loose, "x", null).Outcome);
    }

    [Fact]
    public void The_unticked_rows_and_the_planner_chat_are_kept_in_the_plans_properties()
    {
        var file = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "design")).FullName, "phone.md");
        File.WriteAllText(file, Plan);
        Assert.True(PlanStore.SetList(file, PlanStore.UntickedKey, ["Pairing, with \"quotes\"", "Other"]));
        Assert.Contains("unticked: [\"Pairing, with \\\"quotes\\\"\", \"Other\"]", File.ReadAllText(file));
        Assert.Equal(["Pairing, with \"quotes\"", "Other"], PlanDoc.Load(file).Unticked);
        // the same list again writes nothing
        Assert.False(PlanStore.SetList(file, PlanStore.UntickedKey, ["Pairing, with \"quotes\"", "Other"]));
        Assert.True(PlanStore.SetList(file, PlanStore.UntickedFeaturesKey, ["Themes"]));
        Assert.Equal(["Themes"], PlanDoc.Load(file).UntickedFeatures);
        Assert.True(PlanStore.SetProperty(file, PlanStore.PlannerKey, "abc-123"));
        Assert.Equal("abc-123", PlanDoc.Load(file).PlannerSession);
        Assert.False(PlanStore.SetProperty(file, PlanStore.PlannerKey, "abc-123"));
        // an empty list takes the property out, and the rest of the file is as it was
        Assert.True(PlanStore.SetList(file, PlanStore.UntickedKey, []));
        Assert.True(PlanStore.SetList(file, PlanStore.UntickedFeaturesKey, []));
        Assert.True(PlanStore.SetProperty(file, PlanStore.PlannerKey, null));
        Assert.Equal(Plan, File.ReadAllText(file));
        Assert.Empty(PlanDoc.Load(file).Unticked);
        // written by hand as a plain line, separated by commas
        Assert.Equal(["One", "Two"], PlanDoc.PropList("One, \"Two\""));
        Assert.Equal(["One", "Two"], PlanDoc.PropList("[One, Two]"));
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
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
