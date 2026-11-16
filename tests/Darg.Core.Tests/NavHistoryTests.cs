using Darg.Core;

namespace Darg.Core.Tests;

public class NavHistoryTests
{
    static readonly NavPlace Board = new("wb", "Board"), Agents = new("wb", "Agents", "a1"), Site = new("site", "Preview");

    [Fact]
    public void BackAndForwardWalkTheStepsLikeABrowser()
    {
        var h = new NavHistory();
        Assert.False(h.CanGoBack);
        h.Visit(Board); h.Visit(Agents); h.Visit(Site);
        Assert.Equal(Agents, h.Back());
        Assert.Equal(Board, h.Back());
        Assert.Null(h.Back()); // nothing before the first
        Assert.Equal(Agents, h.Forward());
        h.Visit(new NavPlace("wb", "Docs", "Plan.md")); // somewhere new drops what was ahead
        Assert.False(h.CanGoForward);
        Assert.Equal(Agents, h.Back());
    }

    [Fact]
    public void BeingWhereYouAreAddsNoStep()
    {
        var h = new NavHistory();
        h.Visit(Board); h.Visit(Agents); h.Visit(Agents with { });
        Assert.Equal(Board, h.Back());
        Assert.False(h.CanGoBack);
    }

    [Fact]
    public void OpeningSomethingInAnEmptySectionTakesItsPlace()
    {
        var h = new NavHistory();
        h.Visit(Board);
        h.Visit(new NavPlace("wb", "Docs"));
        h.Visit(new NavPlace("wb", "Docs", "Plan.md")); // replaces the empty Docs step
        h.Visit(new NavPlace("wb", "Docs", "Notes.md")); // a second doc is a step of its own
        Assert.Equal(new NavPlace("wb", "Docs", "Plan.md"), h.Back());
        Assert.Equal(Board, h.Back());
    }

    [Fact]
    public void KeepsTheLastStepsOnly()
    {
        var h = new NavHistory(limit: 3);
        for (var i = 0; i < 5; i++) h.Visit(new NavPlace("wb", "Agents", $"a{i}"));
        Assert.Equal("a3", h.Back()?.Item);
        Assert.Equal("a2", h.Back()?.Item);
        Assert.Null(h.Back());
    }

    [Fact]
    public void LandingElsewhereRewritesTheStepOnly()
    {
        var h = new NavHistory();
        h.Visit(Board); h.Visit(new NavPlace("wb", "Docs", "Gone.md")); h.Visit(Site);
        h.Back();
        h.Landed(new NavPlace("wb", "Docs"));
        Assert.Equal(new NavPlace("wb", "Docs"), h.Current);
        Assert.Equal(Site, h.Forward()); // the step ahead is still there
    }
}
