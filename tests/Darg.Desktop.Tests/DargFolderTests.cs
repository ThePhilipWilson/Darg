using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0144: a project whose repo is here keeps its reports, mistakes and artifacts in <c>.darg</c> in the repo,
/// which git ignores; what the old data folder held moves there the first time the app starts.</summary>
public class DargFolderTests
{
    [AvaloniaFact]
    public async Task A_done_report_is_written_under_the_repos_dot_darg_and_git_ignores_it()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "To Do");
        await app.Start();

        BoardStore.Move(app.BoardFolder, card.Id, app.ColumnId("Done"));
        var reports = Path.Combine(app.Repo, ".darg", "reports");
        await app.Until(() => Directory.Exists(reports) && Directory.GetFiles(reports, "*.md").Length > 0, "the done report under the repo's .darg");

        Assert.Equal(reports, app.Project.Reports!.Folder);
        Assert.Contains(".darg/", File.ReadAllLines(Path.Combine(app.Repo, ".git", "info", "exclude")));
        Assert.False(Directory.Exists(Path.Combine(app.Home, "projects", "proj", "reports")));
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task What_the_old_data_folder_held_moves_into_the_repo_at_start_and_the_status_line_says_so()
    {
        using var app = DargApp.Make();
        var old = Path.Combine(app.Home, "projects", "proj");
        Directory.CreateDirectory(Path.Combine(old, "reports"));
        File.WriteAllText(Path.Combine(old, "reports", "20261101-1000-T-0001.md"), "---\ncard: T-0001\ntitle: Old\nread: true\n---\n\n## Asked\n\nx\n");
        Directory.CreateDirectory(Path.Combine(old, "mistakes"));
        File.WriteAllText(Path.Combine(old, "mistakes", "mistake_log.md"), "# log\n");
        await app.Start();

        Assert.True(File.Exists(Path.Combine(app.Repo, ".darg", "reports", "20261101-1000-T-0001.md")));
        Assert.True(File.Exists(Path.Combine(app.Repo, ".darg", "mistakes", "mistake_log.md")));
        Assert.False(Directory.Exists(old));
        Assert.Contains("Proj", app.Main.FooterLine);
        Assert.Contains(".darg", app.Main.FooterLine);
        Assert.Contains(ReportStore.List(app.Project.Reports!.Folder), r => r.Title == "Old");
        Assert.Equal("", app.Crashes());
    }
}
