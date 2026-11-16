using Darg.Core;

namespace Darg.Core.Tests;

public class CardBranchTests
{
    static Card Card(string description = "", params string[] labels) =>
        new("WOR-0035", "A task", "To Do", "", "Task", labels, "WOR-0035 - A task.md", description);

    [Fact]
    public void A_card_naming_no_branch_gets_its_own()
    {
        Assert.Null(CardBranch.Named(Card("Make the button blue.")));
        Assert.Equal("wor-0035", CardBranch.Own(Card()));
        var rules = string.Join("\n", CardBranch.Rules(Card(), "D:/repo", alongside: false));
        Assert.Contains("new branch of its own, `wor-0035`", rules);
        Assert.Contains("git worktree add .claude/worktrees/wor-0035 -b wor-0035 HEAD", rules);
        Assert.Contains("git merge --ff-only wor-0035", rules);
        Assert.DoesNotContain("another agent building", rules);
    }

    [Theory]
    [InlineData("Branch: feature/login")]
    [InlineData("- **Branch:** feature/login")]
    [InlineData("Branch: `feature/login`")]
    [InlineData("Some words first.\n\nbranch:feature/login\n")]
    [InlineData("Continue on branch feature/login.")]
    [InlineData("This is part two: carry on the existing branch `feature/login`, please")]
    [InlineData("continue with branch: feature/login")]
    public void A_card_can_name_an_existing_branch(string description) =>
        Assert.Equal("feature/login", CardBranch.Named(Card(description)));

    [Fact]
    public void A_branch_label_names_one_too() =>
        Assert.Equal("feature/login", CardBranch.Named(Card("", "ui", "branch:feature/login")));

    [Theory]
    [InlineData("The branch protection rules are wrong.")]
    [InlineData("Branch: ..")]
    [InlineData("Branch: -x")]
    [InlineData("Branch: a:b")]
    public void Prose_and_bad_names_are_not_branches(string description) =>
        Assert.Null(CardBranch.Named(Card(description)));

    [Fact]
    public void A_named_branch_is_carried_on_not_merged()
    {
        var rules = string.Join("\n", CardBranch.Rules(Card("Branch: feature/login"), "D:/repo", alongside: true));
        Assert.Contains("carry on the existing branch `feature/login`", rules);
        Assert.Contains("git worktree add .claude/worktrees/feature-login feature/login`", rules);
        Assert.DoesNotContain("-b feature/login", rules);
        Assert.DoesNotContain("merge --ff-only", rules);
        Assert.Contains("another agent building", rules);
    }
}
