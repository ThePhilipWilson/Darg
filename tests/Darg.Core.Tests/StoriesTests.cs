using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>Wrapping a done column up into stories (WOR-0074): which cards go, and what the agent is told.</summary>
public sealed class StoriesTests
{
    static Card C(int n, string title, string type = "Task") =>
        new($"WOR-{n:0000}", title, "Done", "", type, [], $"WOR-{n:0000} - {title}.md", "");

    static readonly Column Done = new("c3", "Done");

    [Fact]
    public void Story_is_a_card_type_and_a_card_knows_it_is_one()
    {
        Assert.Contains(CardTypes.Story, CardTypes.All);
        Assert.True(C(1, "Themes", "story").IsStory);
        Assert.False(C(2, "Fix", "Bug").IsStory);
        Assert.False(C(3, "Plain", "").IsStory);
    }

    [Fact]
    public void Wrapping_up_leaves_stories_and_cards_with_a_live_agent()
    {
        var wrap = Stories.ToWrap([C(1, "a"), C(2, "Themes", "Story"), C(3, "b"), C(4, "c", "Bug")], ["wor-0003"]);
        Assert.Equal(["WOR-0001", "WOR-0004"], wrap.Select(c => c.Id));
    }

    [Fact]
    public void Story_docs_go_in_the_docs_folder_and_none_without_one()
    {
        Assert.Equal(Path.Combine("D:/vault/Code/X", "Stories"), Stories.Folder(new ProjectConfig { Id = "x", Name = "X", Docs = "D:/vault/Code/X" }));
        Assert.Null(Stories.Folder(new ProjectConfig { Id = "x", Name = "X" }));
    }

    [Fact]
    public void The_prompt_names_the_cards_the_doc_the_story_card_and_completed_and_asks_first()
    {
        var p = Stories.Prompt("Darg", "C:/b/Main", Done, [C(1, "Logo"), C(2, "Themes")], [C(9, "Look and feel", "Story")], ["WOR-0005"],
            "D:/vault/Code/Darg/Stories", "C:/r/reports", ["D:/repo"], CommitRule.Never, CommitRule.Commit);
        Assert.Contains("WOR-0001 Logo; WOR-0002 Themes", p);
        Assert.Contains("WOR-0009 Look and feel", p);         // an existing story a card can join
        Assert.Contains("AskUserQuestion before changing anything", p);
        Assert.Contains("D:/vault/Code/Darg/Stories/<story name>.md", p);
        Assert.Contains("- **Type:** Story", p);
        Assert.Contains("order.c3", p);
        Assert.Contains("order.completed", p);
        Assert.Contains("Status line to Completed", p);
        Assert.Contains("C:/r/reports", p);
        Assert.Contains("git log --grep", p);
        Assert.Contains("In C:/b/Main, never commit or push.", p);
        Assert.Contains("In D:/vault/Code/Darg/Stories, commit your work without asking, but never push.", p);
        Assert.Contains("Agents are working on WOR-0005", p);
    }

    [Fact]
    public void A_wrap_up_prompt_names_the_plan_the_cards_came_from()
    {
        var p = Stories.Prompt("Darg", "C:/b/Main", Done, [C(1, "Logo")], [], [], "D:/docs/Stories", null, [], plans: ["D:/docs/design/phone.md"]);
        Assert.Contains("D:/docs/design/phone.md", p);
        Assert.Contains("Plan: <its full path>", p);
        Assert.Contains("Don't edit the plan", p);
        Assert.DoesNotContain("Plans: these plan files", Stories.Prompt("Darg", "C:/b/Main", Done, [C(1, "Logo")], [], [], "D:/docs/Stories", null, []));
    }

    [Fact]
    public void Without_a_docs_folder_the_story_card_holds_the_write_up()
    {
        var p = Stories.Prompt("Darg", "C:/b/Main", Done, [C(1, "Logo")], [], [], null, null, []);
        Assert.DoesNotContain("Its doc:", p);
        Assert.DoesNotContain("Doc: <", p);
        Assert.Contains("in place of a doc", p);
        Assert.DoesNotContain("Agents are working", p);
    }
}
