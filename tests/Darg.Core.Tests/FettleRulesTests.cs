using Darg.Core;
using Xunit;

public class FettleRulesTests
{
    [Fact]
    public void ADescribedChangeIsATicketUnlessToldToDoItNow()
    {
        var r = FettleRules.ChangeRequests;
        Assert.Contains("make a card", r);
        Assert.Contains("do not edit code", r);
        Assert.Contains("just do it", r);
        Assert.Contains("AskUserQuestion", r);
        Assert.Contains("New ticket", r);
        Assert.Contains("Do it now", r);
    }

    [Fact]
    public void IsSafeOnACommandLine() =>
        Assert.DoesNotContain(FettleRules.ChangeRequests, c => c is '<' or '>' or '"' or '&' or '|');
}
