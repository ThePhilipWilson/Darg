namespace Darg.Core;

/// <summary>A project's cap on agents at work at once (WOR-0245, <see cref="ProjectConfig.MaxAgents"/>). A new agent of the
/// project (a card's work, rework, QA or Improve agent, a queue's next card, a flow's stage, a chat, a planner) starts only
/// while fewer than the cap are at work and none is already waiting; otherwise it is pending, and the pending ones start in
/// the order they were asked for as agents stop. At work means starting or mid-turn: one waiting on the person (a question,
/// a permission) or done with its turn isn't, as on the rail's count. Only starting a new agent is held: a message to one
/// already started, the person's or Darg's (carry on after a usage reset or a cut-off turn), goes at once.</summary>
public static class AgentCap
{
    /// <summary>The cap, or null for none (unset, 0 or less).</summary>
    public static int? Of(int? max) => max is > 0 ? max : null;

    /// <summary>Whether a new agent waits: there is a cap, and it is reached or others are already waiting (first asked,
    /// first started).</summary>
    public static bool MustWait(int? max, int running, int pending) => Of(max) is { } cap && (pending > 0 || running >= cap);

    /// <summary>How many pending agents may start now.</summary>
    public static int Free(int? max, int running) => Of(max) is { } cap ? Math.Max(0, cap - running) : int.MaxValue;

    /// <summary>A pending agent's state, as the list and its card's tile show it ("waiting to start · 2nd in line").</summary>
    public static string State(int place) => place > 0 ? $"waiting to start · {Ordinal(place)} in line" : "waiting to start";

    /// <summary>Why it waits, its activity line: the cap and how many are at work.</summary>
    public static string Why(int? max, int running) => Of(max) is { } cap
        ? $"Waiting: {running} of this project's agents {(running == 1 ? "is" : "are")} at work, and its most at once is {cap} (Setup). Starts by itself when one stops."
        : "Waiting to start.";

    /// <summary>The setting as Setup's box shows it: empty for no cap.</summary>
    public static string Render(int? max) => Of(max)?.ToString() ?? "";

    /// <summary>Setup's box read back: a whole number over 0, else no cap (empty, 0, words).</summary>
    public static int? Parse(string? text) => int.TryParse(text?.Trim(), out var n) && n > 0 ? n : null;

    public static string Ordinal(int n) => (n % 100) is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch
    {
        1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th",
    };
}
