using Darg.Core;

namespace Darg.Core.Tests;

public class DecisionsTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]   // WOR-0081's old threshold: no longer enough
    [InlineData(14.9, false)]
    [InlineData(15, true)]
    [InlineData(60, true)]
    public void OnlyALongTurnsEndNotifies(double minutes, bool tells) =>
        Assert.Equal(tells, Decisions.TellTurnEnded(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void ATurnWithNoKnownStartDoesNotNotify() => Assert.False(Decisions.TellTurnEnded(null));

    [Fact]
    public void BriefingIsSafeOnACommandLineAndNamesTheTool()
    {
        Assert.DoesNotContain(Decisions.Briefing, c => c is '<' or '>' or '"' or '&' or '|');
        Assert.Contains("AskUserQuestion", Decisions.Briefing);
    }

    [Fact]
    public void DefaultContextShowsTheBriefing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "darg-decisions-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        try
        {
            var text = DefaultContext.Build(new ProjectConfig { Id = "p", Name = "P" }, folder, userHome: folder, instanceFolder: folder);
            Assert.Contains(Decisions.Briefing, text);
        }
        finally { Directory.Delete(folder, true); }
    }
}
