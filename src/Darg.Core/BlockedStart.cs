namespace Darg.Core;

/// <summary>Blocked cards don't start by themselves (WOR-0173, Philip's pick on 2026-11-20: hold until done): ▶ Work All
/// and ▶ Work on N leave out a card with an open blocker (<see cref="Blockers.Open"/>) and say so, Work on one blocked card
/// asks first, and the API's <c>work-on</c> / <c>work-all</c> refuse one unless told <c>force</c>. A card agent's prompt
/// names its blockers with their columns and newest reports, so it builds on their work rather than guessing at it.</summary>
public static class BlockedStart
{
    /// <summary>"WOR-0172, In Progress" for each open blocker, joined: what the card waits on, and where that is.</summary>
    public static string Waits(IReadOnlyList<BlockerState> open) =>
        string.Join("; ", open.Select(b => b.Card is { } c ? $"{b.Id}, {c.Column}" : b.Id));

    /// <summary>The inline question before Work on starts a blocked card: "Blocked by AND-0003, In Progress. Start anyway?"</summary>
    public static string Question(IReadOnlyList<BlockerState> open) => $"Blocked by {Waits(open)}. Start anyway?";

    /// <summary>The API's refusal: names the card, its open blockers and the way past.</summary>
    public static string Refusal(string cardId, IReadOnlyList<BlockerState> open) =>
        $"{cardId} is blocked by {Waits(open)}: it waits until {(open.Count == 1 ? "that is" : "those are")} in QA or done. " +
        "Send force: true to start it anyway.";

    /// <summary>What the status line says about cards left out for their blockers: "left out 2 blocked: WOR-0173 (waits on
    /// WOR-0172), WOR-0175 (waits on WOR-0174)". Empty when none were.</summary>
    public static string LeftOut(IReadOnlyList<(string Id, IReadOnlyList<BlockerState> Open)> blocked) =>
        blocked.Count == 0 ? ""
            : $"left out {blocked.Count} blocked: " + string.Join(", ", blocked.Select(b => $"{b.Id} (waits on {string.Join(", ", b.Open.Select(o => o.Id))})"));

    /// <summary>The lines a card agent's prompt carries about its blockers, open or cleared: each one's title, column and the
    /// path of its newest report (<paramref name="newestReport"/>, null when it has none), so the agent starts from what they
    /// did. Empty when the card waits on nothing. An id no board has is said to be missing rather than left out.</summary>
    public static IReadOnlyList<string> PromptLines(IReadOnlyList<BlockerState> blockers, Func<string, string?> newestReport)
    {
        if (blockers.Count == 0) return [];
        var lines = new List<string> { "This card is blocked by (its Blocked by line):" };
        foreach (var b in blockers)
        {
            if (b.Card is not { } c) { lines.Add($"- {b.Id}: no board of this project has it."); continue; }
            var state = b.Cleared ? "cleared" : "not finished yet";
            var report = newestReport(b.Id) is { } file ? $" Its newest report: {file}" : " It has no report yet.";
            lines.Add($"- {b.Id}: {c.Card.Title} ({c.Column}, {state}).{report}");
        }
        lines.Add(blockers.Any(b => b.Found && !b.Cleared)
            ? "Read their reports and build on their work. One is not finished yet: it was started anyway, so check what of it has landed before relying on it, and say in your report what you had to assume."
            : "Read their reports and build on their work rather than redoing or guessing at it.");
        return lines;
    }
}
