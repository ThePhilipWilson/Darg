using Darg.Core;

namespace Darg.Core.Tests;

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

public class ProjectFlowTests
{
    [Fact]
    public void A_project_lists_the_shared_flows_and_its_own_and_its_own_wins_a_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "darg-flows-" + Guid.NewGuid().ToString("N")[..8]);
        var shared = Path.Combine(root, "flows");
        var own = FlowStore.ProjectFolder("alpha", root);
        try
        {
            Directory.CreateDirectory(shared);
            File.WriteAllText(Path.Combine(shared, "review.md"), "# Review\n## Reviewer\nRead it");
            File.WriteAllText(Path.Combine(shared, "build.md"), "# Build\n## Builder\nBuild it");
            // no project folder yet: just the shared ones
            Assert.Equal(["Build", "Review"], FlowStore.ForProject("alpha", shared, own).Select(f => f.Name));

            Directory.CreateDirectory(own);
            File.WriteAllText(Path.Combine(own, "card-art.md"), "# Card art\n## Artist\nMake it");
            File.WriteAllText(Path.Combine(own, "review.md"), "# review\n## Art reviewer\nLook at it");
            var flows = FlowStore.ForProject("alpha", shared, own);
            Assert.Equal(["Build", "Card art", "review"], flows.Select(f => f.Name));
            Assert.True(flows[0].Shared);
            Assert.Equal("alpha", flows[1].Project);
            Assert.Equal("Art reviewer", flows[2].Stages[0].Role); // the project's own stands in for the shared one
            Assert.Equal("Card art · this project", flows[1].ToString());
            Assert.Equal(Path.Combine(root, "projects", "alpha", "flows"), own);
        }
        finally { Directory.Delete(root, true); }
    }
}

public class FlowGeneratorTests
{
    static readonly string[] Others = ["C:/i/flows", "C:/i/projects/alpha/flows"];

    [Fact]
    public void A_basic_project_flow_goes_in_the_projects_folder_with_the_format_and_no_art()
    {
        var p = FlowGenerator.Prompt("Alpha", FlowKind.Basic, "write card text, then check it", "C:/i/projects/alpha/flows", shared: false, Others);
        Assert.Contains("for the Alpha project only", p);
        Assert.Contains("> write card text, then check it", p);
        Assert.Contains("one markdown file in C:/i/projects/alpha/flows", p);
        Assert.Contains("Sends back to:", p);
        Assert.Contains("Max rounds: 2", p); // the starter, as an example
        Assert.Contains("AskUserQuestion", p);
        Assert.Contains("C:/i/flows", p);
        Assert.DoesNotContain("Forge", p);
        Assert.Contains("can name its files", p);
    }

    [Fact]
    public void An_art_flow_names_the_image_tools_the_projects_recipe_and_ends_with_a_review()
    {
        List<ToolConfig> tools = [new() { Name = "Forge", Kind = "forge", Url = "http://127.0.0.1:7860" }, new() { Name = "Ollama", Kind = "ollama", Url = "http://x" }];
        var p = FlowGenerator.Prompt("Alpha", FlowKind.Art, "card art", "C:/i/flows", shared: true, Others, tools);
        Assert.Contains("art flow", p);
        Assert.Contains("shared by every project", p);
        Assert.Contains("Forge (forge) at http://127.0.0.1:7860", p);
        Assert.DoesNotContain("Ollama", p);
        Assert.Contains("art skill", p);
        Assert.Contains(".review.json", p);
        Assert.Contains("name no repo", p);
    }

    [Fact]
    public void With_no_words_it_asks_what_the_flow_is_for()
    {
        var p = FlowGenerator.Prompt("Darg", FlowKind.Basic, "  ", "C:/i/flows", shared: true, Others);
        Assert.Contains("I haven't said what it's for", p);
        Assert.DoesNotContain("in my words", p);
    }
}
