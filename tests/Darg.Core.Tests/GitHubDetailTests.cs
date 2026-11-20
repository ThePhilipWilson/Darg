using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0151: the GitHub page's fuller reads: the repo in one GraphQL answer, a failed run's telling log lines, a pull request's conversation.</summary>
public sealed class GitHubDetailTests
{
    [Fact]
    public void A_repo_reads_from_the_graphql_answer()
    {
        var graph = JsonNode.Parse("""
            {"description":"d","url":"https://github.com/o/r","isPrivate":true,
             "defaultBranchRef":{"name":"main","target":{"history":{"nodes":[{"oid":"abcdef123","messageHeadline":"Fix","messageBody":"why","committedDate":"2026-11-19T09:00:00Z","author":{"name":"Pat","user":null},"statusCheckRollup":{"state":"FAILURE"}}]}}},
             "pullRequests":{"totalCount":31,"nodes":[{"number":7,"title":"Art","isDraft":true,"headRefName":"art","baseRefName":"main","reviewDecision":"APPROVED","author":{"login":"sam"},"comments":{"totalCount":2},"commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"PENDING"}}}]}}]},
             "issues":{"totalCount":1,"nodes":[{"number":3,"title":"Bug","author":{"login":"kim"},"labels":{"nodes":[{"name":"bug"}]},"assignees":{"nodes":[{"login":"pat"}]}}]},
             "releases":{"totalCount":1,"nodes":[{"tagName":"v1","name":"","publishedAt":null,"createdAt":"2026-11-01T00:00:00Z","releaseAssets":{"nodes":[{"name":"a.zip","size":10,"downloadCount":2,"downloadUrl":"u"}]}}]},
             "refs":{"totalCount":2,"nodes":[{"name":"art","target":{"oid":"1","committedDate":"2026-11-20T00:00:00Z"},"associatedPullRequests":{"nodes":[{"number":7}]}},{"name":"main","target":{"oid":"2","committedDate":"2026-11-01T00:00:00Z"},"associatedPullRequests":{"nodes":[]}}]}}
            """);
        var notices = (JsonArray)JsonNode.Parse("""[{"id":"1","unread":false,"reason":"mention","updated_at":"2026-11-19T00:00:00Z","subject":{"title":"old","type":"Issue","url":"https://api.github.com/repos/o/r/issues/3"}},{"id":"2","unread":true,"reason":"ci_activity","updated_at":"2026-11-18T00:00:00Z","subject":{"title":"CI workflow run failed for main branch","type":"CheckSuite","url":null}}]""")!;
        var runs = (JsonArray)JsonNode.Parse("""[{"id":9,"name":"CI","display_title":"Fix","status":"completed","conclusion":"failure","head_branch":"main","run_number":4,"run_started_at":"2026-11-19T09:00:00Z","updated_at":"2026-11-19T09:03:30Z"}]""")!;
        var r = GitHub.Parse("o", "r", graph, runs, notices, "");
        Assert.Equal(("d", true, "main", 31), (r.Description, r.Private, r.DefaultBranch, r.PullCount));
        var pr = r.Pulls.Single();
        Assert.Equal((true, "PENDING", "APPROVED", "art", 2), (pr.Draft, pr.Checks, pr.Review, pr.Head, pr.Comments));
        Assert.Equal("pat", r.Issues.Single().Assignees.Single());
        Assert.Equal(("abcdef1", "Fix", "Pat", "FAILURE"), (r.Commits[0].Short, r.Commits[0].Headline, r.Commits[0].Author, r.Commits[0].Checks));
        Assert.Equal(["main", "art"], r.Branches.Select(b => b.Name)); // the default branch first, then newest
        Assert.Equal(7, r.Branches[1].Pull);
        Assert.Equal((new DateTime(2026, 11, 1), "a.zip"), (r.Releases[0].Date, r.Releases[0].Assets[0].Name)); // unpublished: when made
        Assert.Equal(["2", "1"], r.Notices.Select(n => n.Id)); // unread first
        Assert.Equal(3, r.Notices[1].Number);
        Assert.Equal((9L, 4, TimeSpan.FromSeconds(210)), (r.Runs[0].Id, r.Runs[0].Number, r.Runs[0].Took));
    }

    [Fact]
    public void A_job_log_is_cleaned_and_cut_to_the_lines_that_say_why()
    {
        var lines = new List<string> { "2026-11-19T10:00:05.1234567Z ##[group]Run tests", "2026-11-19T10:00:05.1234567Z ##[endgroup]" };
        for (var i = 0; i < 60; i++) lines.Add($"2026-11-19T10:00:06.0000000Z Passed T{i}");
        lines.Add("2026-11-19T10:00:07.0000000Z Build: 0 errors, 0 warnings");
        for (var i = 60; i < 80; i++) lines.Add($"2026-11-19T10:00:06.0000000Z Passed T{i}");
        lines.Add("2026-11-19T10:00:08.0000000Z   Failed Proj.Tests.Adds_up [3 ms]");
        for (var i = 80; i < 120; i++) lines.Add($"2026-11-19T10:00:06.0000000Z Passed T{i}");
        lines.Add("2026-11-19T10:00:09.0000000Z ##[error]Process completed with exit code 1.");
        lines.Add("2026-11-19T10:00:10.0000000Z Post job cleanup.");
        var clean = GitHubDetail.CleanLog("﻿" + string.Join("\r\n", lines));
        Assert.StartsWith("▸ Run tests\nPassed T0", clean);
        Assert.DoesNotContain("##[", clean);
        Assert.DoesNotContain("2026-11-19T", clean);

        var cut = GitHubDetail.LogExcerpt(clean);
        Assert.Contains("Passed T79\n  Failed Proj.Tests.Adds_up [3 ms]\nPassed T80", cut); // the failure with the lines around it
        Assert.EndsWith("✗ Process completed with exit code 1.", cut); // the runner's tidying up after it is left out
        Assert.DoesNotContain("0 errors", cut); // a line that only looks like an error
        Assert.DoesNotContain("Passed T30\n", cut);
        Assert.Contains("…", cut);
    }

    [Fact]
    public void A_failed_run_lists_its_steps_annotations_and_log_and_its_card_excerpt_says_why()
    {
        var jobs = (JsonArray)JsonNode.Parse("""
            [{"id":5,"name":"test","status":"completed","conclusion":"failure","started_at":"2026-11-19T10:00:00Z","completed_at":"2026-11-19T10:02:05Z",
              "steps":[{"name":"Build","status":"completed","conclusion":"success"},{"name":"Run tests","status":"completed","conclusion":"failure"}]},
             {"id":6,"name":"lint","status":"completed","conclusion":"success","steps":[]}]
            """)!;
        var asked = new List<long>();
        var d = GitHubDetail.ParseRun(new HubRun("CI", "main", "completed", "failure", "https://x/runs/1", default) { Title = "Fix", Number = 4 }, jobs, id =>
        {
            asked.Add(id);
            return ((JsonArray)JsonNode.Parse("""[{"path":".github","annotation_level":"warning","title":"","message":"Node 20 is deprecated"},{"path":".github","annotation_level":"failure","title":"","message":"No space left on device"}]""")!,
                    "2026-11-19T10:00:00.0Z Failed Adds_up\n2026-11-19T10:00:01.0Z ##[error]Process completed with exit code 1.");
        });
        Assert.Equal([5L], asked); // only the failed job's log is read
        Assert.Equal("✗ test · failure · 2m 5s", d.Parts[0].Heading);
        Assert.Equal(["✓", "✗"], d.Parts[0].Lines!.Select(l => l.Tag));
        Assert.Contains(d.Parts, p => p.Lines?.Any(l => l.Text == "No space left on device") == true);
        Assert.Contains(d.Parts, p => p.Kind == HubPartKind.Log && p.Text.Contains("Failed Adds_up"));
        Assert.Contains("No space left on device", d.Excerpt);
        Assert.DoesNotContain("deprecated", d.Excerpt); // a warning isn't why it failed
        Assert.Equal("No space left on device", d.Parts.First(p => p.Heading == "What GitHub says").Lines![0].Text); // failures first
        Assert.Contains("Failed Adds_up", d.Excerpt);
        Assert.Equal("CI · run 4 · main · failure", d.Sub);
    }

    [Fact]
    public void A_pull_requests_conversation_reads_in_order_with_its_line_comments()
    {
        var pr = JsonNode.Parse("""
            {"number":7,"title":"Art","url":"u","body":"","author":{"login":"sam"},"createdAt":"2026-11-18T08:00:00Z","headRefName":"art","baseRefName":"main","additions":1,"deletions":2,"mergeable":"CONFLICTING",
             "comments":{"nodes":[{"author":{"login":"pat"},"body":"later","createdAt":"2026-11-18T12:00:00Z"}]},
             "reviews":{"nodes":[{"author":{"login":"kim"},"body":"","state":"COMMENTED","submittedAt":"2026-11-18T09:00:00Z","comments":{"nodes":[]}},
                                 {"author":{"login":"kim"},"body":"","state":"CHANGES_REQUESTED","submittedAt":"2026-11-18T10:00:00Z","comments":{"nodes":[{"path":"a.cs","line":4,"body":"off by one"}]}}]}}
            """)!;
        var d = GitHubDetail.ParsePull(pr);
        Assert.Equal(["sam · ", "kim changes requested · ", "pat commented · "], d.Parts.Select(p => p.Heading[..(p.Heading.IndexOf('·') + 2)]));
        Assert.Equal("*No description.*", d.Parts[0].Text);
        Assert.Contains("**a.cs:4**", d.Parts[1].Text);
        Assert.Contains("has conflicts", d.Sub);
    }

    [Fact]
    public void A_commit_shows_its_message_files_and_diff()
    {
        var c = JsonNode.Parse("""{"sha":"abcdef12345","html_url":"u","commit":{"message":"Fix it\n\nBecause.","author":{"name":"Pat","date":"2026-11-19T09:00:00Z"}},"stats":{"additions":1,"deletions":1},"files":[{"filename":"a.cs","status":"modified","additions":1,"deletions":1,"patch":"@@ -1 +1 @@\n-a\n+b"},{"filename":"b.png","status":"added","additions":0,"deletions":0}]}""")!;
        var d = GitHubDetail.ParseCommit(c, null);
        Assert.Equal("Fix it", d.Title);
        Assert.Equal("Because.", d.Parts[0].Text);
        Assert.Equal(["a.cs", "b.png"], d.Parts[1].Lines!.Select(l => l.Text));
        Assert.Contains("+b", d.Parts[2].Text);
        Assert.Contains("no text diff", d.Parts[2].Text);
    }
}
