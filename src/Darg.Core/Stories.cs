namespace Darg.Core;

/// <summary>Wrapping up a board's done column into stories (WOR-0074), so done isn't every card ever finished.
/// An agent groups the done cards into a few stories (a feature, an area of work), asks, then writes each one
/// twice: a doc in the project's docs folder under Stories/ (what was built, from the cards, their reports and
/// commits) and a card of type Story in the done column that names its cards and links the doc. The cards
/// themselves go to Completed, Tatty's archive list (hidden on the board unless asked for), files kept.</summary>
public static class Stories
{
    /// <summary>Where a project's story docs go: Stories/ in its docs folder; null when it has none (then a
    /// story is its card alone, with the write-up in the card).</summary>
    public static string? Folder(ProjectConfig project) =>
        string.IsNullOrWhiteSpace(project.Docs) ? null : Path.Combine(project.Docs, "Stories");

    /// <summary>The done column's cards that a wrap-up would fold away: everything in it but story cards
    /// (those are the stories) and cards an agent is still on.</summary>
    public static List<Card> ToWrap(IEnumerable<Card> doneColumn, IEnumerable<string> busy)
    {
        var skip = busy.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return doneColumn.Where(c => !c.IsStory && !skip.Contains(c.Id)).ToList();
    }

    /// <summary>What the wrap-up agent is asked.</summary>
    /// <param name="boardFolder">The board (its board.json and items folder); the agent's working folder.</param>
    /// <param name="done">The board's done column (its last).</param>
    /// <param name="cards">The done column's cards to wrap up (<see cref="ToWrap"/>).</param>
    /// <param name="stories">The story cards already in the done column, which new cards can join.</param>
    /// <param name="busy">Cards with a live agent: left where they are.</param>
    /// <param name="storiesFolder">Where the story docs go (<see cref="Folder"/>); null for none.</param>
    /// <param name="reportsFolder">The project's agent reports (WOR-0061), read for what each card did.</param>
    /// <param name="repos">The project's repos, for the commits.</param>
    /// <param name="plans">Plan files (WOR-0178) some of the cards were made from: a story doc links the plan its cards came from.</param>
    public static string Prompt(string projectName, string boardFolder, Column done, IReadOnlyList<Card> cards, IReadOnlyList<Card> stories,
        IReadOnlyList<string> busy, string? storiesFolder, string? reportsFolder, IReadOnlyList<string> repos,
        CommitRule? boardCommits = null, CommitRule? docsCommits = null, IReadOnlyList<string>? plans = null)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var lines = new List<string>
        {
            $"Wrap up the {done.Name} column of the {projectName} board in {boardFolder} (your working folder) into stories, " +
            $"so {done.Name} holds a few stories rather than every card ever finished.",
            "",
            $"The cards to wrap up ({cards.Count}): " + string.Join("; ", cards.Select(c => $"{c.Id} {c.Title}")) + ".",
        };
        if (stories.Count > 0)
            lines.Add($"Stories already in {done.Name}, which a card can join: " + string.Join("; ", stories.Select(c => $"{c.Id} {c.Title}")) + ".");
        lines.AddRange([
            "",
            "1. Read each card's file in items/. " +
            (reportsFolder is not null ? $"Read its report in {reportsFolder} (file names end with the card id) for what was done and what was left. " : "") +
            (repos.Count > 0 ? $"Find its commits with git log --grep=<card id> in {string.Join(", ", repos)}. " : "") +
            "Don't change any code.",
            "2. Group them into stories: a story is one piece of work someone would name in a line (a feature, an area of the app, " +
            "a run of fixes to one thing), usually 3 to 15 cards. Every card goes in exactly one story. " +
            (stories.Count > 0 ? "Put a card in an existing story when it fits rather than making a near-copy. " : "") +
            "A card that fits nowhere can be a story of its own, or stay in the column if it's still being talked about.",
            "3. Ask me with AskUserQuestion before changing anything. Put the proposal in the question itself, since I may only see " +
            "the question: each story's name, one line on what it is, and its card ids. Make your proposal the first option, marked " +
            $"(Recommended); add one real alternative (fewer, bigger stories, or a different cut) and a \"Leave {done.Name} as it is\" option.",
            "4. Then, for each story:",
        ]);
        if (storiesFolder is not null)
            lines.Add(
                $"   - Its doc: {storiesFolder.Replace('\\', '/')}/<story name>.md (make the folder if needed; an existing story's doc is " +
                "added to, not rewritten). Obsidian properties between --- lines: type: story, card: <the story card's id>, " +
                $"cards: <the card ids, space-separated>, updated: {today}. Then # <story name>, a paragraph or two on what was built and how " +
                "it works now (written for someone who wasn't there, from the cards, reports and commits, not a list of tickets), " +
                "## Cards (each: id, title, one line on what it did, its short commit ids) and ## Left (open threads the reports name, or None)." +
                (plans is { Count: > 0 } ? $" Plans: these plan files hold some of the cards ({string.Join("; ", plans.Select(p => p.Replace('\\', '/')))}). " +
                    "A story whose cards came from a plan gets a property plan: <the plan's file name without .md> and a line Plan: <its full path> under the heading, " +
                    "so the plan says what was meant and the story what was made. Don't edit the plan." : ""));
        lines.AddRange([
            "   - Its card, made exactly as Darg makes one: the next id is board.json's nextId (one past the highest number any board with " +
            "this prefix uses, if that's higher), the file is items/<ID> - <title>.md (title with / \\ : * ? \" < > | taken out, cut to 60 " +
            "characters), and nextId goes up by one. Its text: # <ID>: <story name>, a blank line, then the lines - **Status:** " +
            $"{done.Name}, - **Priority:**, - **Type:** Story, - **Labels:**, - **Created:** {today}, - **Done:** {today}, a blank line, " +
            "## Description, a blank line, a short paragraph on what the story is, " +
            (storiesFolder is not null ? "a line Doc: <the doc's full path>, " : "the write-up the doc would hold (what was built, ## Cards, ## Left) in place of a doc, ") +
            "and a line Cards: <the card ids, comma-separated>. Put its id at the top of the " +
            $"{done.Name} list in board.json's order (order.{done.Id}). An existing story's card gets the new ids added to its Cards line.",
            "   - Each of its cards goes to Completed: set its Status line to Completed (keep its Done date), add a line Story: <story id> " +
            $"<story name> at the end of its description, and move its id from order.{done.Id} to the end of order.completed in board.json. " +
            "Its file stays in items/, unchanged otherwise.",
            "   - Don't renumber, rename or delete any card, and leave Story cards in the column: they are the stories.",
            "5. When you stop, list the stories and which cards went into each. Darg shows the board change as you make it.",
            "",
            boardCommits is { } b ? Commits.Describe(b, boardFolder) : $"In {boardFolder}, never commit or push.",
        ]);
        if (storiesFolder is not null)
            lines.Add(docsCommits is { } d ? Commits.Describe(d, storiesFolder) : $"In {storiesFolder}, never commit or push.");
        if (busy.Count > 0)
            lines.AddRange(["", $"Agents are working on {string.Join(", ", busy)} right now: leave those cards where they are."]);
        return string.Join(Environment.NewLine, lines);
    }
}
