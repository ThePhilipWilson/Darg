using Workbench.Core;

namespace Workbench.Core.Tests;

/// <summary>A project with more than one repo (a site and its workspace): which one an agent starts in, and
/// which it is given besides.</summary>
public sealed class ReposTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-repos-").FullName;
    readonly ProjectConfig _p;

    public ReposTests()
    {
        foreach (var r in new[] { "Site", "Workspace" }) Directory.CreateDirectory(Path.Combine(_dir, r));
        _p = new ProjectConfig { Repos = [Path.Combine(_dir, "Site"), Path.Combine(_dir, "Workspace"), Path.Combine(_dir, "Gone")] };
    }

    string Repo(string name) => Path.Combine(_dir, name);

    [Theory]
    [InlineData("repo:Workspace")]
    [InlineData("Repo: workspace")]
    [InlineData("repo/Workspace")]
    [InlineData("repo = Workspace")]
    public void A_label_names_the_repo(string label) =>
        Assert.Equal(Repo("Workspace"), _p.RepoFor([label], ""));

    [Theory]
    [InlineData("Repo: Workspace")]
    [InlineData("Fix the talk deck.\n\nrepo: Workspace\n")]
    [InlineData("- **Repo:** Workspace")]
    [InlineData("**Repo**: `Workspace`")]
    public void A_line_in_the_description_names_the_repo(string description) =>
        Assert.Equal(Repo("Workspace"), _p.RepoFor([], description));

    [Theory]
    [InlineData("frontend")]
    [InlineData("repo:Gone")] // set, but not on this machine
    [InlineData("repo:Nowhere")]
    public void A_card_naming_no_repo_here_gets_none(string label) =>
        Assert.Null(_p.RepoFor([label], "The repository is broken. Report: none"));

    [Fact]
    public void A_full_path_names_the_repo_too() =>
        Assert.Equal(Repo("Workspace"), _p.RepoNamed(Repo("Workspace") + "/"));

    [Fact]
    public void An_agent_is_given_the_repos_it_did_not_start_in()
    {
        Assert.Equal([Repo("Workspace")], _p.OtherRepos(Repo("Site")));
        Assert.Equal([Repo("Site")], _p.OtherRepos(Repo("Workspace") + Path.DirectorySeparatorChar));
        // a worktree inside a repo is that repo
        Assert.Equal([Repo("Workspace")], _p.OtherRepos(Path.Combine(Repo("Site"), ".claude", "worktrees", "x")));
        Assert.Equal([Repo("Site"), Repo("Workspace")], _p.OtherRepos(_dir));
    }

    [Fact]
    public void A_project_with_one_repo_adds_nothing()
    {
        var one = new ProjectConfig { Repos = [Repo("Site")] };
        Assert.Empty(one.OtherRepos(Repo("Site")));
        Assert.Contains("--add-dir", DefaultContext.Build(_p, Repo("Site"), userHome: _dir, instanceFolder: _dir));
        Assert.Contains("Workspace repo", DefaultContext.Build(_p, Repo("Site"), userHome: _dir, instanceFolder: _dir));
        Assert.DoesNotContain("repo, so it can work there", DefaultContext.Build(one, Repo("Site"), userHome: _dir, instanceFolder: _dir));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
}
