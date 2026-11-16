using System.Text.Json.Nodes;
using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class GitHubTests
{
    [Theory]
    [InlineData("git@github.com:example-studio/alpha.git", "example-studio", "alpha")]
    [InlineData("https://github.com/ThePhilipWilson/Site.git", "ThePhilipWilson", "Site")]
    [InlineData("https://github.com/TimLewisDev/Tims-AI-Companion", "TimLewisDev", "Tims-AI-Companion")]
    [InlineData("ssh://git@github.com/a/b.git\n", "a", "b")]
    public void Remotes_on_github(string url, string owner, string name) => Assert.Equal((owner, name), GitHub.ParseRemote(url));

    [Theory]
    [InlineData("git@gitlab.com:team/thing.git")]
    [InlineData("D:/repos/local")]
    [InlineData("")]
    public void Other_remotes_are_not_github(string url) => Assert.Null(GitHub.ParseRemote(url));

    [Fact]
    public void Items_read_from_the_api_shape()
    {
        var i = GitHub.Item(JsonNode.Parse("""{"number":41,"title":"Split art","user":{"login":"pw"},"html_url":"https://github.com/x/y/pull/41","updated_at":"2026-11-16T01:00:00Z","labels":[{"name":"tooling"}],"body":"why"}"""));
        Assert.Equal((41, "Split art", "pw", "tooling", "why"), (i.Number, i.Title, i.Author, i.Labels[0], i.Body));
    }

    /// <summary>Real call. WORKBENCH_LIVE_REPO=path and WORKBENCH_LIVE_GH=account to run it.</summary>
    [Fact]
    public void Loads_a_real_repo_under_its_own_account()
    {
        var repo = Environment.GetEnvironmentVariable("WORKBENCH_LIVE_REPO");
        if (string.IsNullOrEmpty(repo)) return;
        var hub = GitHub.Load(repo, Environment.GetEnvironmentVariable("WORKBENCH_LIVE_GH"));
        Assert.True(hub.Error.Length == 0, hub.Error);
        Console.WriteLine($"{hub.Full}: {hub.Pulls.Count} PRs, {hub.Issues.Count} issues, {hub.Runs.Count} runs; last run {hub.Runs.FirstOrDefault()?.State}");
    }

    [Fact]
    public void Instance_settings_read_whatever_the_key_case()
    {
        var dir = Directory.CreateTempSubdirectory("wb-inst-").FullName;
        File.WriteAllText(Path.Combine(dir, "instance.json"), """{"projects":[{"id":"v","name":"V","github":"example-studio","Repos":["x"]}]}""");
        var p = InstanceConfig.Load(dir).Projects.Single();
        Assert.Equal(("example-studio", "x"), (p.GitHub, p.Repos.Single()));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void A_run_says_its_conclusion_once_done() =>
        Assert.Equal(("failure", "in_progress"), (new HubRun("ci", "main", "completed", "failure", "", default).State,
                                                  new HubRun("ci", "main", "in_progress", "", "", default).State));
}
