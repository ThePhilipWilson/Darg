using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0144: a project's working files go in .darg in its first repo, ignored by git, and the old data folder is moved in.</summary>
public sealed class ProjectDataTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-pdata-").FullName;
    string Dir(string rel) => Directory.CreateDirectory(Path.Combine(_dir, rel)).FullName;

    (string Instance, string Repo) Setup(bool withRepo = true)
    {
        var instance = Dir("inst");
        var repo = Dir("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        new InstanceConfig { Projects = [new() { Id = "p", Name = "P", Repos = withRepo ? [repo] : [Path.Combine(_dir, "nowhere")] }] }.Save(instance);
        return (instance, repo);
    }

    [Fact]
    public void A_project_with_a_repo_keeps_its_folders_in_dot_darg_and_git_ignores_it()
    {
        var (instance, repo) = Setup();
        Assert.Equal(Path.Combine(repo, ".darg", "reports"), ReportStore.FolderFor("p", instance));
        Assert.Equal(Path.Combine(repo, ".darg", "mistakes", "mistake_log.md"), MistakeLog.PathFor("p", instance));
        Assert.Equal(Path.Combine(repo, ".darg", "artifacts"), ArtifactStore.Folder("p", instance));
        Assert.Equal(Path.Combine(repo, ".darg", "flows"), FlowStore.ProjectFolder("p", instance));
        Assert.Contains(".darg/", File.ReadAllLines(Path.Combine(repo, ".git", "info", "exclude")));
        var file = Path.Combine(ArtifactStore.AgentFolder("p", "a1", instance), "x.html");
        File.WriteAllText(file, "<title>x</title>");
        Assert.Equal(("p", "a1"), ArtifactStore.Owner(file, instance));
    }

    [Fact]
    public void A_project_with_no_repo_here_keeps_the_old_folder()
    {
        var (instance, _) = Setup(withRepo: false);
        Assert.Equal(Path.Combine(instance, "projects", "p", "reports"), ReportStore.FolderFor("p", instance));
        Assert.Equal(Path.Combine(instance, "projects", "nope", "reports"), ReportStore.FolderFor("nope", instance));
    }

    [Fact]
    public void A_project_that_opted_in_by_deleting_the_exclude_line_is_not_ignored_again()
    {
        var (instance, repo) = Setup();
        _ = ReportStore.FolderFor("p", instance);
        var exclude = Path.Combine(repo, ".git", "info", "exclude");
        File.WriteAllText(exclude, "");
        Assert.Equal("", ProjectData.Migrate(instance));
        _ = ReportStore.FolderFor("p", instance);
        Assert.Equal("", File.ReadAllText(exclude));
    }

    [Fact]
    public void Migrate_moves_the_old_folder_in_repoints_the_boards_it_made_and_leaves_ones_elsewhere()
    {
        var (instance, repo) = Setup();
        var old = Path.Combine(instance, "projects", "p");
        Directory.CreateDirectory(Path.Combine(old, "reports"));
        File.WriteAllText(Path.Combine(old, "reports", "r.md"), "r");
        Directory.CreateDirectory(Path.Combine(old, "tasks", "Main"));
        File.WriteAllText(Path.Combine(old, "tasks", "Main", "board.json"), "{}");
        Directory.CreateDirectory(Path.Combine(old, "notes"));
        File.WriteAllText(Path.Combine(old, "shown-boards"), "Main");
        var config = InstanceConfig.Load(instance);
        config.Projects[0].Boards = Path.Combine(old, "tasks").Replace('\\', '/');
        config.Projects.Add(new() { Id = "q", Name = "Q", Repos = [repo], Boards = "C:/vault/Tatty" });
        config.Save(instance);

        var said = ProjectData.Migrate(instance);

        Assert.Contains("P (", said);
        Assert.True(File.Exists(Path.Combine(repo, ".darg", "reports", "r.md")));
        Assert.True(File.Exists(Path.Combine(repo, ".darg", "tasks", "Main", "board.json")));
        Assert.True(File.Exists(Path.Combine(repo, ".darg", "shown-boards")));
        Assert.False(Directory.Exists(old));
        var after = InstanceConfig.Load(instance);
        Assert.Equal(Path.Combine(repo, ".darg", "tasks").Replace('\\', '/'), after.Projects[0].Boards);
        Assert.Equal("C:/vault/Tatty", after.Projects[1].Boards);
        Assert.Equal("", ProjectData.Migrate(instance)); // nothing left to move
    }

    /// <summary>The old folder with an artifact, a report, a note and a board in it, the config's board pointing there.</summary>
    static string Legacy(string instance)
    {
        var old = Path.Combine(instance, "projects", "p");
        foreach (var (rel, text) in new[] { ("artifacts/a1/page.html", "<title>page</title>"), ("reports/r.md", "r"), ("notes/n.md", "n"), ("tasks/Main/board.json", "{}") })
        {
            var file = Path.Combine(old, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        var config = InstanceConfig.Load(instance);
        config.Projects[0].Boards = Path.Combine(old, "tasks").Replace('\\', '/');
        config.Save(instance);
        return old;
    }

    [Fact]
    public void Migrate_moves_the_old_folder_onto_another_drive()
    {
        // WOR-0152: the instance is on C: and the repo on D:, where Directory.Move throws; nothing moved, so the
        // project's artifacts, reports and notes vanished from Darg. The test's bin folder is on the repo's drive.
        var instance = Dir("inst");
        var repo = Path.Combine(AppContext.BaseDirectory, "wb-pdata-xvol-" + Guid.NewGuid().ToString("N")[..8]);
        if (string.Equals(Path.GetPathRoot(instance), Path.GetPathRoot(repo), StringComparison.OrdinalIgnoreCase)) return; // one drive here: nothing to show
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            new InstanceConfig { Projects = [new() { Id = "p", Name = "P", Repos = [repo] }] }.Save(instance);
            var old = Legacy(instance);

            var said = ProjectData.Migrate(instance);

            Assert.Contains("P (4)", said);
            var page = Path.Combine(repo, ".darg", "artifacts", "a1", "page.html");
            Assert.True(File.Exists(page));
            Assert.Equal("page", Assert.Single(ArtifactStore.List("p", instance)).Title);
            Assert.True(File.Exists(Path.Combine(repo, ".darg", "reports", "r.md")));
            Assert.True(File.Exists(Path.Combine(repo, ".darg", "notes", "n.md")));
            Assert.Equal(Path.Combine(repo, ".darg", "tasks").Replace('\\', '/'), InstanceConfig.Load(instance).Projects[0].Boards);
            Assert.False(Directory.Exists(old));
        }
        finally { try { Directory.Delete(repo, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_file_held_open_stays_behind_and_the_rest_still_move()
    {
        var (instance, repo) = Setup();
        var old = Legacy(instance);
        var held = Path.Combine(old, "artifacts", "a1", "page.html");

        string said;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            said = ProjectData.Migrate(instance);

        Assert.Contains("P (3)", said);
        Assert.True(File.Exists(held));
        Assert.True(File.Exists(Path.Combine(repo, ".darg", "reports", "r.md")));
        Assert.Equal(Path.Combine(repo, ".darg", "tasks").Replace('\\', '/'), InstanceConfig.Load(instance).Projects[0].Boards);

        // the next start takes what was left
        Assert.Contains("P (1)", ProjectData.Migrate(instance));
        Assert.True(File.Exists(Path.Combine(repo, ".darg", "artifacts", "a1", "page.html")));
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void A_board_that_could_not_move_is_still_pointed_at_where_it_is()
    {
        var (instance, repo) = Setup();
        var old = Legacy(instance);
        var board = Path.Combine(old, "tasks", "Main", "board.json");

        using (new FileStream(board, FileMode.Open, FileAccess.Read, FileShare.None))
            ProjectData.Migrate(instance);

        Assert.Equal(Path.Combine(old, "tasks").Replace('\\', '/'), InstanceConfig.Load(instance).Projects[0].Boards);
        Assert.True(File.Exists(board));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
}
