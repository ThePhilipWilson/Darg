using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

// WOR-0210: agents' dotnet builds must not leave MSBuild worker nodes running.
public sealed class BuildNodeReuseTests
{
    [Fact]
    public void An_agents_environment_turns_off_msbuild_node_reuse()
    {
        var psi = new ProcessStartInfo("claude");
        psi.Environment.Remove("MSBUILDDISABLENODEREUSE");
        AgentSession.NoBuildNodeReuse(psi);
        Assert.Equal("1", psi.Environment["MSBUILDDISABLENODEREUSE"]);
    }

    [Fact]
    public void A_value_the_person_already_set_stays()
    {
        var psi = new ProcessStartInfo("claude");
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "0";
        AgentSession.NoBuildNodeReuse(psi);
        Assert.Equal("0", psi.Environment["MSBUILDDISABLENODEREUSE"]);
    }
}
