namespace Darg.Core;

/// <summary>What one thing waiting on the person in a plan is (WOR-0200).</summary>
public enum PlanNeedKind
{
    /// <summary>A question in the plan with no answer yet.</summary>
    Question,
    /// <summary>An answer a card agent chose (WOR-0178), waiting for Keep or Change.</summary>
    Decided,
    /// <summary>One of the plan's made cards in the attention column, with its decision if it has one.</summary>
    Decision,
    /// <summary>One of the plan's made cards in the QA column, to check.</summary>
    Qa,
}

/// <summary>One thing in a plan that waits on the person: the plan it's in, what it is, and the question or card it
/// points at, so a click can land there.</summary>
public sealed record PlanNeed(PlanNeedKind Kind, string PlanFile, string Text, string? Question = null, string? Card = null);

/// <summary>What waits on the person in a plan (WOR-0200, the Design section's Needs you strip and the tree's count):
/// its open questions, the answers card agents chose for it, and its made cards waiting in For Human Attention or QA.
/// Read each time from the plan and the boards, never written to the plan (docs/FEATURE-PLANNING.md, "Progress is
/// read, never written"). A planner asking something is the app's to add: Core doesn't see agents.</summary>
public static class PlanNeeds
{
    /// <summary>The plan's own items, in the order the strip lists them: questions, agents' choices, then cards
    /// (decisions before QA, each in the plan's order). <paramref name="decisionOf"/> reads a card's open decision
    /// question from its file (null for none); the default reads the file.</summary>
    public static List<PlanNeed> Of(PlanDoc plan, IReadOnlyDictionary<string, BoardCard> index, Func<BoardCard, string?>? decisionOf = null)
    {
        if (plan.Status.Equals("dropped", StringComparison.OrdinalIgnoreCase)) return [];
        decisionOf ??= DecisionQuestion;
        var list = new List<PlanNeed>();
        foreach (var q in plan.Questions.Where(q => q.Open))
            list.Add(new(PlanNeedKind.Question, plan.File, q.Question, Question: q.Question));
        foreach (var q in plan.Questions.Where(q => q.ByAgent))
            list.Add(new(PlanNeedKind.Decided, plan.File,
                $"{(q.DecidedCard is { } c ? c + "'s agent" : "An agent")} chose \"{q.Pick}\" for \"{q.Question}\": keep it or change it",
                Question: q.Question, Card: q.DecidedCard));
        var made = plan.Cards.Where(c => c.Made && index.ContainsKey(c.Id)).Select(c => (c, b: index[c.Id])).ToList();
        foreach (var (c, b) in made.Where(m => m.b.InAttention && !m.b.Done))
        {
            var question = decisionOf(b);
            list.Add(new(PlanNeedKind.Decision, plan.File,
                question is { Length: > 0 } ? $"{c.Id} {c.Title} asks: {question}" : $"{c.Id} {c.Title} waits in {b.Column} with no decision written",
                Card: c.Id));
        }
        foreach (var (c, b) in made.Where(m => m.b.InQa && !m.b.InAttention && !m.b.Done))
            list.Add(new(PlanNeedKind.Qa, plan.File, $"{c.Id} {c.Title} is in {b.Column}, to check", Card: c.Id));
        return list;
    }

    /// <summary>A card's open decision question, read from its file; null when it has none or the file can't be read.</summary>
    public static string? DecisionQuestion(BoardCard card)
    {
        try { return CardDecision.Read(File.ReadAllText(card.File)) is { Valid: true } d ? d.Question : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
