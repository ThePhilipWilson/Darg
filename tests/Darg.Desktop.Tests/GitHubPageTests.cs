using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0151: the GitHub page shows a repo's CI, pull requests, issues, commits, branches, releases and
/// notifications, each in full when picked, so the web page isn't needed. gh is tools/perf/fake_gh.py.</summary>
public class GitHubPageTests
{
    static GitHubViewModel Open(DargApp app) => app.Project.Hub ?? throw new InvalidOperationException("The project has no GitHub page.");

    static async Task<DargApp> Start(string test)
    {
        var app = DargApp.Make(test);
        Environment.SetEnvironmentVariable("FAKE_GH_LOG", Path.Combine(app.Folder, "fake-gh.jsonl"));
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = app.Repo, RedirectStandardError = true };
        foreach (var a in new[] { "remote", "add", "origin", "https://github.com/example-studio/proj.git" }) psi.ArgumentList.Add(a);
        using (var p = System.Diagnostics.Process.Start(psi)!) p.WaitForExit();
        await app.Start();
        await app.Api("show", new { project = "proj", section = "GitHub" });
        var hub = Open(app);
        await app.Until(() => hub.Repo is not null, "the repo read through the fake gh");
        return app;
    }

    [AvaloniaFact]
    public async Task A_failed_run_shows_its_jobs_and_the_log_lines_that_say_why_and_can_become_a_card()
    {
        using var app = await Start(nameof(A_failed_run_shows_its_jobs_and_the_log_lines_that_say_why_and_can_become_a_card));
        var hub = Open(app);
        Assert.Equal("", hub.Repo!.Error);
        Assert.Equal("Actions ✗", hub.Tabs[(int)HubTab.Actions]); // main's latest run failed
        Assert.Equal("Notifications 1", hub.Tabs[(int)HubTab.Notifications]);
        Assert.Equal("Pull requests 1", hub.Tabs[(int)HubTab.Pulls]);
        app.Shot("overview");

        hub.ShowTab(HubTab.Actions);
        await DargApp.Settle();
        hub.Selected = hub.Rows.First(r => r.Bad);
        await app.Until(() => hub.Detail is not null, "the failed run's detail");
        var parts = hub.Detail!.Parts;
        Assert.Contains(parts, p => p.IsLines && p.Heading.Contains("test") && p.Lines.Any(l => l.Text == "Run tests" && l.Bad));
        var log = parts.Single(p => p.IsLog);
        Assert.Contains("Failed Proj.Tests.Coins_add_up", log.LogText);
        Assert.Contains("Actual:   9", log.LogText);
        Assert.DoesNotContain("Proj.Tests.Case5", log.LogText); // a passing line well away from the failure is left out
        Assert.Contains("✗ Process completed with exit code 1.", log.LogText);
        log.ToggleLogCommand.Execute(null);
        Assert.Contains("Proj.Tests.Case5", log.LogText); // the whole log, a click away
        log.ToggleLogCommand.Execute(null);
        await DargApp.Settle();
        app.Shot("failed-run");

        Assert.True(hub.CanCard);
        hub.ToCardCommand.Execute(null);
        var card = BoardStore.Load(app.BoardFolder).Lists.Values.SelectMany(l => l).Single(c => c.Title.StartsWith("CI failed"));
        Assert.Contains("Failed Proj.Tests.Coins_add_up", card.Description);
        Assert.Contains("https://github.com/example-studio/proj/actions/runs/902", card.Description);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_pull_request_shows_its_checks_files_and_conversation_in_order()
    {
        using var app = await Start(nameof(A_pull_request_shows_its_checks_files_and_conversation_in_order));
        var hub = Open(app);
        hub.ShowTab(HubTab.Pulls);
        await DargApp.Settle();
        var row = hub.Rows.Single();
        Assert.Contains("coin-art → main", row.Meta);
        Assert.Contains("changes requested", row.Meta);
        hub.Selected = row;
        await app.Until(() => hub.Detail is not null, "the pull request's detail");
        var headings = hub.Detail!.Parts.Select(p => p.Heading).ToList();
        Assert.Contains(hub.Detail.Parts, p => p.Text.Contains("**painted**"));
        Assert.Contains(hub.Detail.Parts, p => p.Heading.StartsWith("Checks") && p.Lines.Single().Good);
        Assert.Contains(hub.Detail.Parts, p => p.Heading.StartsWith("Files changed") && p.Lines.Any(l => l.Text == "src/Coins.cs"));
        var comment = headings.FindIndex(h => h.StartsWith("pat commented"));
        var review = headings.FindIndex(h => h.StartsWith("kim changes requested"));
        Assert.True(comment >= 0 && review > comment, string.Join(" | ", headings));
        Assert.Contains("src/Coins.cs:12", hub.Detail.Parts[review].Text);
        await DargApp.Settle();
        app.Shot("pull-request");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_ci_notification_opens_its_run_and_can_be_marked_read()
    {
        using var app = await Start(nameof(A_ci_notification_opens_its_run_and_can_be_marked_read));
        var hub = Open(app);
        hub.ShowTab(HubTab.Notifications);
        await DargApp.Settle();
        Assert.True(hub.Rows[0].Unread); // unread first
        hub.Selected = hub.Rows[0];
        await app.Until(() => hub.Detail is not null, "the notification's run");
        Assert.Equal("Count the coins right", hub.Detail!.Title);
        Assert.True(hub.CanMarkRead);
        await hub.MarkReadCommand.ExecuteAsync(null);
        Assert.False(hub.Rows[0].Unread);
        Assert.Equal("Notifications", hub.Tabs[(int)HubTab.Notifications]);
        await DargApp.Settle();
        Assert.Equal((HubTab.Notifications, "Count the coins right"), (hub.Tab, hub.Detail?.Title)); // the relabelled tabs keep the tab and the pick
        Assert.Contains("notifications/threads/77", File.ReadAllText(Path.Combine(app.Folder, "fake-gh.jsonl")));

        // the rest of the tabs read too
        hub.ShowTab(HubTab.Commits);
        await DargApp.Settle();
        hub.Selected = hub.Rows.First();
        await app.Until(() => hub.Detail is not null, "the commit's diff");
        Assert.Contains(hub.Detail!.Parts, p => p.IsDiff && p.Diff.Any(d => d.Add && d.Text.Contains("long n")));
        app.Shot("commit");
        hub.ShowTab(HubTab.Releases);
        await DargApp.Settle();
        hub.Selected = hub.Rows.Single();
        await app.Until(() => hub.Detail is not null, "the release's notes");
        Assert.Contains(hub.Detail!.Parts, p => p.Lines.Any(l => l.Text == "proj-win.zip" && l.Tag == "3 MB"));
        hub.ShowTab(HubTab.Branches);
        await DargApp.Settle();
        Assert.Equal("main (default)", hub.Rows[0].Title);
        Assert.Equal("", app.Crashes());
    }
}
