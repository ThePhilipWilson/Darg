using Workbench.Core;

namespace Workbench.Core.Tests;

public class FlowTests
{
    [Fact]
    public void Starter_flow_parses_into_designer_engineer_qa()
    {
        var f = FlowStore.Parse(FlowStore.Starter)!;
        Assert.Equal("Designer, engineer, QA", f.Name);
        Assert.Equal(["Designer", "Engineer", "QA"], f.Stages.Select(s => s.Role));
        Assert.Equal(2, f.MaxRounds);
        Assert.Equal("Engineer", f.Stages[2].SendsBackTo);
        Assert.False(f.Stages[1].Checks);
        Assert.DoesNotContain("Sends back to", f.Stages[2].Brief);
        Assert.StartsWith("Check the engineer's work", f.Stages[2].Brief);
        Assert.Contains("A designer specifies", f.Description);
    }

    [Fact]
    public void A_send_back_to_a_later_or_unknown_stage_is_dropped()
    {
        var f = FlowStore.Parse("# X\n## A\nSends back to: B\nbrief\n## B\nSends back to: Nobody\nbrief")!;
        Assert.All(f.Stages, s => Assert.False(s.Checks));
    }

    [Fact]
    public void No_stages_is_no_flow() => Assert.Null(FlowStore.Parse("# Just a title\nsome words"));

    [Fact]
    public void Name_falls_back_to_the_file_name() =>
        Assert.Equal("my-flow", FlowStore.Parse("## Only\nDo it", Path.Combine("x", "my-flow.md"))!.Name);

    [Theory]
    [InlineData("All good.\nVERDICT: PASS", Verdict.Pass)]
    [InlineData("Broken.\n**VERDICT: FAIL**", Verdict.Fail)]
    [InlineData("The brief says end with VERDICT: PASS or VERDICT: FAIL.\n\nVERDICT: fail", Verdict.Fail)]
    [InlineData("I think it's fine", Verdict.None)]
    public void Verdict_is_the_last_one_given(string report, Verdict expected) => Assert.Equal(expected, FlowStore.VerdictOf(report));

    [Theory]
    [InlineData("The briefs conflict.\nFLOW: STOP", true)]
    [InlineData("**FLOW: STOP**", true)]
    [InlineData("I could say FLOW: STOP but won't", false)]
    [InlineData("VERDICT: FAIL", false)]
    public void Stop_line_is_a_line_of_its_own(string report, bool stop) => Assert.Equal(stop, FlowStore.AsksToStop(report));

    [Fact]
    public void Stage_prompt_carries_task_brief_and_handoffs()
    {
        var f = FlowStore.Parse(FlowStore.Starter)!;
        var p = FlowStore.StagePrompt(f, 2, "Make the button blue", [("Designer", "Spec: blue"), ("Engineer", "Did it")]);
        Assert.Contains("You are the QA", p);
        Assert.Contains("stage 3 of 3", p);
        Assert.Contains("Make the button blue", p);
        Assert.Contains("## Handed on by the Designer\nSpec: blue", p.Replace("\r\n", "\n"));
        Assert.Contains("VERDICT: PASS", p);
        Assert.DoesNotContain("VERDICT", FlowStore.StagePrompt(f, 0, "t", []));
    }

    [Fact]
    public void Starter_is_written_once_and_listed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wb-flows-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Assert.Single(FlowStore.List(dir));
            File.WriteAllText(Path.Combine(dir, "a.md"), "# Another\n## Solo\nDo it all");
            File.Delete(Path.Combine(dir, "designer-engineer-qa.md"));
            Assert.Equal(["Another"], FlowStore.List(dir).Select(f => f.Name)); // a deleted starter stays deleted
        }
        finally { Directory.Delete(dir, true); }
    }
}
