using Darg.Core;

namespace Darg.Core.Tests;

public class CommitsTests
{
    static ProjectConfig With(params (string Folder, string Rule)[] rules) =>
        new() { Commits = rules.ToDictionary(r => r.Folder, r => r.Rule) };

    [Fact]
    public void EachRepoHasItsOwnRule()
    {
        var p = With(("D:/Code/Bench", "push"), ("D:/Web/Site", "ask"), ("D:/Web/Workspace", "commit"));
        Assert.Equal(CommitRule.Push, Commits.For(p, "D:/Code/Bench"));
        Assert.Equal(CommitRule.Ask, Commits.For(p, @"D:\Web\Site\"));
        Assert.Equal(CommitRule.Commit, Commits.For(p, "d:/web/workspace"));
        Assert.Null(Commits.For(p, "D:/Web"));
    }

    [Fact]
    public void AFolderRuleCoversWhatIsInsideIt_DeepestWins()
    {
        var p = With(("D:/vault", "never"), ("D:/vault/Code/Bench", "push"));
        Assert.Equal(CommitRule.Never, Commits.For(p, "D:/vault/Tatty/tasks"));
        Assert.Equal(CommitRule.Push, Commits.For(p, "D:/vault/Code/Bench/docs"));
        // a sibling whose name starts the same is not inside
        Assert.Null(Commits.For(p, "D:/vaultish"));
    }

    [Fact]
    public void NoRulesOrABadRuleIsNoRule()
    {
        Assert.Null(Commits.For(new ProjectConfig(), "D:/x"));
        Assert.Null(Commits.For(With(("D:/x", "sometimes")), "D:/x"));
        Assert.Null(Commits.For(With(("D:/x", "1")), "D:/x"));
        Assert.Equal(CommitRule.Ask, Commits.For(With(("D:/x", " ASK ")), "D:/x"));
    }

    [Fact]
    public void SetupTextRoundTrips()
    {
        var text = "push D:/Code/Bench\nask   D:/Web/Site\n\nnever C:/Program Files/Thing";
        var map = Commits.ParseText(text)!;
        Assert.Equal("push", map["D:/Code/Bench"]);
        Assert.Equal("ask", map["D:/Web/Site"]);
        Assert.Equal("never", map["C:/Program Files/Thing"]);
        Assert.Equal(map, Commits.ParseText(Commits.Render(map)));
    }

    [Fact]
    public void ALineWithoutARuleIsReadAsAsk_ARuleWithoutAFolderIsDropped()
    {
        var map = Commits.ParseText("D:/Forgot/Rule\npush\npsuh D:/Typo")!;
        Assert.Equal("ask", map["D:/Forgot/Rule"]);
        Assert.Equal("ask", map["psuh D:/Typo"]);
        Assert.Equal(2, map.Count);
        Assert.Null(Commits.ParseText("  \n "));
    }

    [Fact]
    public void DescribeSaysWhatToDo()
    {
        Assert.Contains("push it without asking", Commits.Describe(CommitRule.Push, "D:/a"));
        Assert.Contains("never push", Commits.Describe(CommitRule.Commit, "D:/a"));
        Assert.Contains("AskUserQuestion", Commits.Describe(CommitRule.Ask, "D:/a"));
        Assert.Contains("never commit or push", Commits.Describe(CommitRule.Never, "D:/a"));
        var lines = Commits.Describe(With(("D:/vault/Code", "push"), ("D:/vault", "never"))).ToList();
        Assert.StartsWith("In D:/vault,", lines[0]); // the general rule first, the exception after
    }

    [Fact]
    public void InstanceJsonKeepsTheRules()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wb-commits-" + Guid.NewGuid().ToString("N"));
        try
        {
            new InstanceConfig { Projects = [new ProjectConfig { Id = "a", Commits = new() { ["D:/Code/Bench"] = "push" } }] }.Save(dir);
            Assert.Contains("\"commits\"", File.ReadAllText(InstanceConfig.FilePath(dir)));
            Assert.Equal(CommitRule.Push, Commits.For(InstanceConfig.Load(dir).Projects[0], "D:/Code/Bench"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
