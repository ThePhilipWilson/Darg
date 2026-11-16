using Darg.Core;

namespace Darg.Core.Tests;

public sealed class ProjectDetectTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-detect-").FullName;
    string Dir(string rel) => Directory.CreateDirectory(Path.Combine(_dir, rel)).FullName;

    [Fact]
    public void A_repo_with_its_own_boards_and_docs()
    {
        var root = Dir("LoraDeck");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var board = Dir("LoraDeck/planning/Main");
        File.WriteAllText(Path.Combine(board, "board.json"), "{}");
        Dir("LoraDeck/docs");
        var (p, found) = ProjectDetect.FromFolder(root, [], [], Dir("data"));
        Assert.Equal(("LoraDeck", "loradeck"), (p.Name, p.Id));
        Assert.Equal([root.Replace('\\', '/')], p.Repos);
        Assert.Equal(Path.Combine(root, "planning").Replace('\\', '/'), p.Boards);
        Assert.Equal(Path.Combine(root, "docs").Replace('\\', '/'), p.Docs);
        Assert.Contains(p.Commands, c => c.Command == "git status");
        Assert.Contains(found, f => f.StartsWith("Boards found"));
    }

    [Fact]
    public void Docs_in_the_vault_beside_the_other_projects_and_a_new_board_in_dargs_data()
    {
        var vaultCode = Dir("vault/Code");
        Dir("vault/Code/Alpha");
        Dir("vault/Code/Night-Market");
        var root = Dir("studio/night-market");
        var data = Dir("data");
        var others = new List<ProjectConfig> { new() { Id = "alpha", Name = "Alpha", Docs = Path.Combine(vaultCode, "Alpha") } };
        var (p, found) = ProjectDetect.FromFolder(root, others, [], data);
        Assert.Equal("Night Market", p.Name);
        Assert.Equal(Path.Combine(vaultCode, "Night-Market").Replace('\\', '/'), p.Docs);
        Assert.True(File.Exists(Path.Combine(data, "projects", "night-market", "tasks", "Main", "board.json")));
        Assert.Empty(p.Repos);
        Assert.Contains(found, f => f.StartsWith("No git repo"));
        Assert.Contains(found, f => f.StartsWith("No boards in the folder"));
    }

    [Fact]
    public void A_studio_folder_holding_several_repos()
    {
        var root = Dir("Studio");
        foreach (var r in new[] { "a", "b" }) Directory.CreateDirectory(Path.Combine(Dir($"Studio/{r}"), ".git"));
        Dir("Studio/notes-not-a-repo");
        var (p, _) = ProjectDetect.FromFolder(root, [], [], Dir("data"));
        Assert.Equal(2, p.Repos.Count);
    }

    [Fact]
    public void Ids_dont_clash()
    {
        var (p, _) = ProjectDetect.FromFolder(Dir("Beta"), [new() { Id = "beta", Name = "Beta" }], [], Dir("data"));
        Assert.Equal("beta-2", p.Id);
    }

    [Theory]
    [InlineData("Project C", "PS")]
    [InlineData("Alpha", "VOT")]
    [InlineData("night market", "NIG")]
    public void Prefixes(string name, string prefix) => Assert.Equal(prefix, ProjectDetect.Prefix(name));

    public void Dispose() => Directory.Delete(_dir, true);
}
