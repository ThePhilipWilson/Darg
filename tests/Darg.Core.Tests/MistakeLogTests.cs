using Darg.Core;

namespace Darg.Core.Tests;

public class MistakeLogTests
{
    // The shape of the logs kept before Darg: a schema example in a code fence, then entries.
    const string Log = """
        # Example mistake log

        ## Schema

        ```
        ### YYYY-MM-DD: short descriptive title

        - **Caught by:** user | self | hook
        ```

        ## Entries

        ### 2026-05-17: Forgot the baseline

        - **Caught by:** user
        - **Category:** `workflow-skip` + `measurement-gap`
        - **What was wrong:** no baseline.

        ### 2026-05-18: Captured an idea too early

        - **Caught by:** self
        - **Category:** `scope-creep` (in the sense of capturing it early)

        ### 2026-07-18: composite-call rule violated

        - **Caught by:** user (in a playtest)
        - **Category:** plan-drift, Workflow-Skip

        ### A heading with no date

        - **Category**: verification-skipped, other (env-var leakage)

        ## Archive

        Not an entry.
        """;

    [Fact]
    public void EntriesAreLevelThreeHeadingsOutsideFences()
    {
        var entries = MistakeLog.Parse(Log);
        Assert.Equal(["Forgot the baseline", "Captured an idea too early", "composite-call rule violated", "A heading with no date"],
            entries.Select(e => e.Title));
        Assert.Equal(new DateOnly(2026, 5, 17), entries[0].Date);
        Assert.Null(entries[3].Date);
        Assert.Equal("user", entries[0].CaughtBy);
        Assert.Contains("no baseline", entries[0].Body);
        Assert.DoesNotContain("Not an entry", entries[3].Body);
    }

    [Fact]
    public void CategoriesComeFromTicksOrCommasWithoutTheirReasons()
    {
        var entries = MistakeLog.Parse(Log);
        Assert.Equal(["workflow-skip", "measurement-gap"], entries[0].Categories);
        Assert.Equal(["scope-creep"], entries[1].Categories);
        Assert.Equal(["plan-drift", "workflow-skip"], entries[2].Categories);
        Assert.Equal(["verification-skipped", "other"], entries[3].Categories); // colon outside the bold
    }

    [Fact]
    public void TallyCountsEachCategoryMostFirst()
    {
        var tally = MistakeLog.Tally(MistakeLog.Parse(Log));
        Assert.Equal(("workflow-skip", 2), tally[0]);
        Assert.Equal(6, tally.Count);
    }

    [Fact]
    public void ANewLogHasNoEntriesAndKeepsWhatIsAppended()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wb-mistakes-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var path = MistakeLog.PathFor("proj", dir);
            Assert.Equal(Path.Combine(dir, "projects", "proj", "mistakes", MistakeLog.FileName), path);
            MistakeLog.Ensure(path, "Proj");
            Assert.Empty(MistakeLog.Read(path)); // the schema's example heading is in a fence
            File.AppendAllText(path, "\n### 2026-11-16: Guessed a menu path\n\n- **Caught by:** user\n- **Category:** `default-trust`\n");
            MistakeLog.Ensure(path, "Proj"); // already there: left alone
            var e = Assert.Single(MistakeLog.Read(path));
            Assert.Equal("Guessed a menu path", e.Title);
            Assert.Equal(["default-trust"], e.Categories);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void BriefingIsSafeOnACommandLine()
    {
        var b = MistakeLog.Briefing(@"C:\x\projects\p\mistakes\mistake_log.md");
        Assert.DoesNotContain(b, c => c is '<' or '>' or '"' or '&' or '|');
    }

    [Fact]
    public void AskToLogCarriesWhatWasWrongOrAsksForIt()
    {
        Assert.Contains("guessed the flag", MistakeLog.AskToLog("log.md", "guessed the flag"));
        Assert.Contains("AskUserQuestion", MistakeLog.AskToLog("log.md", " "));
    }
}
