using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0245: a project's cap on agents at work at once, and how a waiting agent is shown.</summary>
public class AgentCapTests
{
    [Fact]
    public void A_new_agent_waits_once_the_cap_is_reached_or_others_already_wait()
    {
        Assert.False(AgentCap.MustWait(null, 10, 0)); // no cap
        Assert.False(AgentCap.MustWait(0, 10, 0)); // 0 is no cap too
        Assert.False(AgentCap.MustWait(2, 1, 0));
        Assert.True(AgentCap.MustWait(2, 2, 0));
        Assert.True(AgentCap.MustWait(2, 3, 0)); // over it (a message to one already started isn't held)
        Assert.True(AgentCap.MustWait(2, 0, 1)); // room, but one asked for earlier waits: it goes first
    }

    [Fact]
    public void Room_is_the_cap_less_what_is_at_work()
    {
        Assert.Equal(int.MaxValue, AgentCap.Free(null, 4));
        Assert.Equal(2, AgentCap.Free(3, 1));
        Assert.Equal(0, AgentCap.Free(3, 5));
    }

    [Fact]
    public void Setups_box_reads_a_number_over_nought_and_anything_else_as_no_limit()
    {
        Assert.Equal(3, AgentCap.Parse(" 3 "));
        Assert.Null(AgentCap.Parse(""));
        Assert.Null(AgentCap.Parse("0"));
        Assert.Null(AgentCap.Parse("-2"));
        Assert.Null(AgentCap.Parse("lots"));
        Assert.Equal("", AgentCap.Render(null));
        Assert.Equal("", AgentCap.Render(0));
        Assert.Equal("4", AgentCap.Render(4));
    }

    [Fact]
    public void A_waiting_agent_says_its_place_in_line_and_why()
    {
        Assert.Equal("waiting to start · 1st in line", AgentCap.State(1));
        Assert.Equal("waiting to start · 2nd in line", AgentCap.State(2));
        Assert.Equal("waiting to start · 3rd in line", AgentCap.State(3));
        Assert.Equal("waiting to start · 11th in line", AgentCap.State(11));
        Assert.Equal("waiting to start · 22nd in line", AgentCap.State(22));
        Assert.Equal("waiting to start", AgentCap.State(0));
        Assert.Contains("1 of this project's agents is at work", AgentCap.Why(1, 1));
        Assert.Contains("most at once is 1", AgentCap.Why(1, 1));
    }

    [Fact]
    public void A_card_whose_agent_waits_sorts_into_its_own_sub_column()
    {
        Assert.Equal(CardState.Waiting, CardStates.Of(false, false, false, qaRunning: true, false, blocked: true, agentWaiting: true));
        Assert.Equal(CardState.Ready, CardStates.Of(false, false, false, false, false, false, agentWaiting: false));
        Assert.Equal("Waiting to start", CardStates.Label(CardState.Waiting));
    }
}
