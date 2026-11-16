using System.Text.RegularExpressions;

namespace Workbench.Core;

/// <summary>Which branch a card's agent works on, and the git rules it is given. A card gets a new branch
/// of its own, named for it, unless it says to carry on an existing one (feature work across cards):
/// a "Branch: name" line in its description, or a "branch:name" label.</summary>
public static partial class CardBranch
{
    // a line "Branch: x", "- **Branch:** x", "Branch: `x`"; or anywhere "continue on branch x",
    // "carry on the existing branch `x`", "continue with branch: x"
    [GeneratedRegex(@"^[\s>*_-]*branch[*_]*\s*:[*_]*\s*`?(?<b>[^\s`]+)`?" +
                    @"|\b(?:continue|carry\s+on)\s+(?:on\s+|with\s+|in\s+)?(?:the\s+)?(?:existing\s+)?branch\s*:?\s*`?(?<b>[^\s`]+)`?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BranchLine();

    /// <summary>The existing branch the card says to work on, or null for a new branch of its own.</summary>
    public static string? Named(Card card)
    {
        foreach (var label in card.Labels)
            if (label.StartsWith("branch:", StringComparison.OrdinalIgnoreCase) && label[7..].Trim() is { Length: > 0 } b)
                return b;
        var m = BranchLine().Match(card.Description);
        if (!m.Success) return null;
        var name = m.Groups["b"].Value.TrimEnd('.', ',', ';', ')', '!'); // the end of a sentence, not of the name
        return Valid(name) ? name : null;
    }

    // git's own naming rules
    static bool Valid(string b) =>
        b.Length > 0 && !b.EndsWith(".lock") &&!b.EndsWith('/') && !b.Contains("..") && !b.StartsWith('-') &&
        b.IndexOfAny([':', '?', '*', '[', '\\', '^', '~']) < 0;

    /// <summary>The new branch a card gets when it names none: its id, lower case (wor-0035).</summary>
    public static string Own(Card card) => card.Id.ToLowerInvariant();

    /// <summary>The git rules for a card's agent working in <paramref name="repo"/>.</summary>
    /// <param name="alongside">Other agents are at work in the same folder.</param>
    public static IEnumerable<string> Rules(Card card, string repo, bool alongside)
    {
        var named = Named(card);
        var branch = named ?? Own(card);
        var dir = $".claude/worktrees/{Regex.Replace(branch, @"[^A-Za-z0-9._-]", "-")}";
        yield return "";
        yield return named is null
            ? $"Do this card's work on a new branch of its own, `{branch}`, never straight on the branch {repo} is on: a new task " +
              "gets a new branch unless it is told to carry on an existing one (feature work, say), and this card names none. " +
              $"If `{branch}` or its worktree already exists, an earlier agent on this card made it: carry on in it."
            : $"This card says to carry on the existing branch `{branch}`: do its work there, not on a new branch, " +
              $"and don't merge `{branch}` into anything unless the card says to.";
        yield return "Work on that branch in a git worktree of your own if you will commit anything to the repo (edit source code, build, " +
                     "run tests or the app), so this folder stays on its branch for whatever starts here next" +
                     (alongside ? "; another agent building or editing here at the same moment can also break your build, lock its output, or end up in your commit. " : ". ") +
                     "Stay in this folder, with no branch, if you will only read, answer a question, or edit notes, docs or card files " +
                     (alongside ? "the other cards don't touch. " : "outside the repo. ") +
                     "I don't want to be asked: make the call, and say in your last message which you chose and why.";
        yield return "To use one: add `.claude/worktrees/` to `.git/info/exclude` if it isn't there; " +
                     (named is null
                         ? $"`git worktree add {dir} -b {branch} HEAD` (from local HEAD, so unpushed commits come too)"
                         : $"`git worktree add {dir} {branch}` (if {repo} itself has `{branch}` checked out, work there instead)") +
                     "; then EnterWorktree with that path (without the tool, work there by absolute path). Untracked local files " +
                     $"such as CLAUDE.local.md aren't in the worktree: read them from {repo}. Commit on `{branch}`.";
        yield return named is null
            ? $"When the work is done: rebase `{branch}` onto the branch {repo} is on, then in {repo} run `git merge --ff-only {branch}` " +
              "and push if the project's rules say to. If the merge is refused (another agent's uncommitted change in the way, or a conflict " +
              "you can't settle), leave the branch and say so. Once merged, step out before cleaning up (Windows won't delete a folder " +
              $"a session is still in): ExitWorktree with keep, then `cd {repo}`, then `git -C {repo} worktree remove {dir}` " +
              $"and `git -C {repo} branch -d {branch}`."
            : $"When the work is done: push `{branch}` if the project's rules say to, and leave it for the next card on it. " +
              $"Step out before cleaning up: ExitWorktree with keep, then `cd {repo}`, then `git -C {repo} worktree remove {dir}` " +
              "(the branch stays).";
    }
}
