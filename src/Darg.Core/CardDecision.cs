using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>What a decision option does when the person picks it (WOR-0124): the answer goes to the agent (the
/// default, for choices only the agent can act on), the card goes to done, it is sent back as with QA notes, or it
/// stays where it is for now.</summary>
public enum DecisionAction { Agent, Done, Back, Stay }

public sealed record DecisionOption(string Label, string Description, DecisionAction Action, bool Recommended)
{
    /// <summary>What the click does, in a couple of words for the button's corner.</summary>
    public string ActionText => Action switch
    {
        DecisionAction.Done => "→ done",
        DecisionAction.Back => "→ send back",
        DecisionAction.Stay => "→ stays here",
        _ => "→ to the agent",
    };
}

/// <summary>The decision a card in For Human Attention waits on (WOR-0124): a <c>## Decision</c> section of the card
/// file, below the description, in the shape of an AskUserQuestion: one question, two to four options (one marked
/// recommended, each optionally saying what picking it does), and a Context line with what's needed to answer.
/// <code>
/// ## Decision
///
/// **Question:** Does dictation work for you in a build from 5b85baa?
/// **Asked by:** agent 0fa5c0cd01, 2026-11-18 00:00
///
/// - **Works** (recommended) → done: words land in the box.
/// - **Still fails** → back: say what the box showed.
/// - **Not tried yet** → stay: leave it here.
///
/// **Context:** builds and tests pass; a real microphone can't be tried from an agent.
/// </code>
/// An answered decision keeps its text, gains an Answer line and is renamed <c>## Decision answered (date)</c>, so the
/// card keeps the record and the next round can have a fresh one. Written by agents as plain markdown, so the parser
/// is lenient about the shapes they drift into: bold or plain labels, (Recommended) inside or after the bold,
/// → or -> or =>, and a colon or a dash before the description.</summary>
public sealed partial record CardDecision(string Question, IReadOnlyList<DecisionOption> Options, string Context, string AskedBy)
{
    public const string Heading = "## Decision";
    public const string AnsweredHeading = "## Decision answered";

    /// <summary>Something the person can answer by picking: a question and at least two options.</summary>
    public bool Valid => Question.Length > 0 && Options.Count >= 2;

    /// <summary>Darg drafted it from the card (no agent supplied one): shown marked as such.</summary>
    public bool Drafted => AskedBy.Contains("drafted", StringComparison.OrdinalIgnoreCase);

    /// <summary>The id of the agent that asked, when the Asked by line names one.</summary>
    public string? AgentId => AgentIdRx().Match(AskedBy) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The option whose label is this answer (case aside), or null for an answer of the person's own.</summary>
    public DecisionOption? OptionFor(string answer) =>
        Options.FirstOrDefault(o => o.Label.Equals(answer.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The card's open decision, or null when it has no <c>## Decision</c> section. Check <see cref="Valid"/>.</summary>
    public static CardDecision? Read(string cardText)
    {
        var body = DecisionReport.Section(cardText, "Decision");
        return body.Length == 0 ? null : Parse(body);
    }

    /// <summary>Read a decision from its section's body (or from a draft, which is the same thing without the heading).</summary>
    public static CardDecision Parse(string body)
    {
        string question = "", askedBy = "";
        var context = new StringBuilder();
        var options = new List<DecisionOption>();
        var inContext = false;
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("#")) { inContext = false; continue; }
            if (FieldRx().Match(line) is { Success: true } f)
            {
                var value = f.Groups[2].Value.Trim();
                inContext = false;
                switch (f.Groups[1].Value.ToLowerInvariant())
                {
                    case "question": question = value; break;
                    case "asked by": askedBy = value; break;
                    case "context": context.Append(value); inContext = true; break;
                }
                continue;
            }
            if (BulletRx().Match(line) is { Success: true } b)
            {
                inContext = false;
                if (Option(b.Groups[1].Value) is { } o) options.Add(o);
                continue;
            }
            if (inContext && line.Trim().Length > 0) context.Append('\n').Append(line.Trim());
            // a question written as a bare line ending in ?, with no Question: field
            else if (question.Length == 0 && options.Count == 0 && line.Trim().EndsWith('?')) question = line.Trim().Trim('*').Trim();
        }
        return new CardDecision(question, options, context.ToString().Trim(), askedBy);
    }

    /// <summary>One option line, without its bullet; null when there's no label in it.</summary>
    static DecisionOption? Option(string text)
    {
        text = text.Trim();
        string label, rest;
        if (text.StartsWith("**") && text.IndexOf("**", 2, StringComparison.Ordinal) is var close and > 2)
        {
            label = text[2..close];
            rest = text[(close + 2)..];
        }
        else
        {
            var cut = PlainLabelEndRx().Match(text);
            label = cut.Success ? text[..cut.Index] : text;
            rest = cut.Success ? text[cut.Index..] : "";
        }
        var recommended = false;
        if (RecommendedRx().IsMatch(label)) { recommended = true; label = RecommendedRx().Replace(label, ""); }
        label = label.Trim().TrimEnd(':').Trim();
        if (label.Length == 0) return null;
        rest = rest.Trim();
        if (RecommendedLeadRx().Match(rest) is { Success: true } r) { recommended = true; rest = rest[r.Length..].Trim(); }
        var action = DecisionAction.Agent;
        if (ArrowRx().Match(rest) is { Success: true } a)
        {
            action = a.Groups[1].Value.ToLowerInvariant().Replace(" ", "") switch
            {
                "done" or "approve" or "approved" => DecisionAction.Done,
                "back" or "sendback" or "rework" => DecisionAction.Back,
                "stay" or "wait" or "leave" or "later" => DecisionAction.Stay,
                _ => DecisionAction.Agent,
            };
            rest = rest[a.Length..].Trim();
        }
        if (RecommendedLeadRx().Match(rest) is { Success: true } r2) { recommended = true; rest = rest[r2.Length..].Trim(); }
        rest = Regex.Replace(rest, @"^[:\-–—]+\s*", "");
        return new DecisionOption(label, rest.Trim(), action, recommended);
    }

    /// <summary>The section as markdown, heading included.</summary>
    public string Format()
    {
        static string Act(DecisionAction a) => a switch { DecisionAction.Done => " → done", DecisionAction.Back => " → back", DecisionAction.Stay => " → stay", _ => "" };
        var sb = new StringBuilder(Heading).Append("\n\n");
        sb.Append("**Question:** ").Append(Question).Append('\n');
        if (AskedBy.Length > 0) sb.Append("**Asked by:** ").Append(AskedBy).Append('\n');
        sb.Append('\n');
        foreach (var o in Options)
            sb.Append("- **").Append(o.Label).Append("**").Append(o.Recommended ? " (recommended)" : "").Append(Act(o.Action))
              .Append(o.Description.Length > 0 ? ": " + o.Description : "").Append('\n');
        if (Context.Length > 0) sb.Append("\n**Context:** ").Append(Context).Append('\n');
        return sb.ToString();
    }

    /// <summary>Put this decision in the card file: in place of its open one, else below the description (before the
    /// card's next section), else at the end.</summary>
    public void WriteTo(string file)
    {
        var raw = File.ReadAllText(file);
        var crlf = raw.Contains("\r\n");
        var text = raw.Replace("\r\n", "\n");
        var section = Format().TrimEnd('\n') + "\n";
        if (OpenSectionRx().Match(text) is { Success: true } open)
            text = text[..open.Index] + section + (open.Index + open.Length < text.Length ? "\n" : "") + text[(open.Index + open.Length)..].TrimStart('\n');
        else if (Regex.Match(text, @"(?:^|\n)##[ \t]+Description[ \t]*\n") is { Success: true } d
                 && Regex.Match(text[(d.Index + d.Length)..], @"\n##[ \t]") is var next)
        {
            var at = next.Success ? d.Index + d.Length + next.Index + 1 : text.Length;
            var before = text[..at].TrimEnd('\n') + "\n\n";
            text = before + section + (at < text.Length ? "\n" + text[at..] : "");
        }
        else text = text.TrimEnd('\n') + "\n\n" + section;
        File.WriteAllText(file, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
    }

    /// <summary>Record the person's answer in the card: an Answer line (and their notes) at the end of the open
    /// section, which is renamed so the card no longer has an open decision. Returns the decision answered, or null
    /// when the card had none open.</summary>
    public static CardDecision? Answer(string file, string answer, string notes, DateTime when)
    {
        var raw = File.ReadAllText(file);
        var crlf = raw.Contains("\r\n");
        var text = raw.Replace("\r\n", "\n");
        if (OpenSectionRx().Match(text) is not { Success: true } open) return null;
        var decision = Parse(DecisionReport.Section(text, "Decision"));
        var body = open.Value.TrimEnd('\n');
        body = Regex.Replace(body, @"^##[ \t]+Decision[ \t]*$", $"{AnsweredHeading} {when:yyyy-MM-dd HH:mm}", RegexOptions.Multiline);
        body += $"\n\n**Answer:** {answer.Trim()} ({when:yyyy-MM-dd HH:mm})";
        if (notes.Trim().Length > 0) body += $"\n**Notes:** {notes.Trim()}";
        var after = text[(open.Index + open.Length)..];
        text = text[..open.Index] + body + "\n" + (after.Length > 0 ? "\n" + after.TrimStart('\n') : "");
        File.WriteAllText(file, crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
        return decision;
    }

    /// <summary>What agents are told wherever they're told about the attention column. Free of double quotes, angle
    /// brackets, ampersands and pipes.</summary>
    public static string Rules(string attentionColumn) =>
        $"Before moving a card to {attentionColumn}, write what I have to decide into the card file as a section headed {Heading}, right below the description, " +
        "shaped like an AskUserQuestion: a line **Question:** with one specific question I can answer by picking (a decision, not a list of steps); " +
        "a line **Asked by:** agent and your agent id if you know it, then the date; then two to four options, one per line, as " +
        "- **Label** (recommended) → done: what it means. Mark exactly one (recommended). After the arrow say what picking it does: done (the card goes to done), " +
        "back (it is sent back for more work with my notes), stay (it stays for now), or leave the arrow out and my answer comes back to you as a message to act on. " +
        "Last, a line **Context:** with only what I need to answer, in a sentence or two (the detail stays in How to check). " +
        "A card that lands there without one gets you a message asking for it.";

    /// <summary>The message to the agent that moved a card to the attention column without a decision.</summary>
    public static string Nudge(Card card, string file, string attentionColumn) =>
        $"{card.Id} is in {attentionColumn} but its card file has no {Heading} section, so I'd have to go investigating to see what you need from me. " +
        $"Card file: {file}. Add one now, and change nothing else. " + Rules(attentionColumn) + " " +
        "Then log this in the project's mistake log (Category: decision missing), as the briefing says.";

    /// <summary>Put before a card agent's usual prompt when the person answered a decision and its asker is gone.</summary>
    public static string AnswerPreface(CardDecision d, string answer, string notes) =>
        "This card was in the attention column waiting on a decision from me, and I have answered it. " +
        $"The question: {d.Question}{Environment.NewLine}My answer: {answer.Trim()}" +
        (notes.Trim().Length > 0 ? $"{Environment.NewLine}My notes: {notes.Trim()}" : "") + Environment.NewLine + Environment.NewLine +
        "The card file keeps the question and answer under a Decision answered heading. Read its How to check section, if it has one, for the work so far, " +
        "carry on from there by my answer, and leave what already works alone.";

    /// <summary>The message to the agent that asked, when it is still listed.</summary>
    public static string AnswerMessage(Card card, CardDecision d, string answer, string notes) =>
        $"From {card.Id}'s decision: you asked {d.Question}{Environment.NewLine}I picked: {answer.Trim()}" +
        (notes.Trim().Length > 0 ? $"{Environment.NewLine}Notes: {notes.Trim()}" : "") + Environment.NewLine +
        "The card is back in the working column. Carry on by my answer.";

    /// <summary>The Haiku prompt for drafting a decision from the card when no agent supplied one.</summary>
    public static string DraftPrompt(Card card, string cardText, string attentionColumn) =>
        $"A card on a kanban board was moved to {attentionColumn}: the person has to decide something about it, but whoever moved it didn't say what. " +
        "From the card below, write the decision they face as one question they can answer by picking, with two to four short options. " +
        "Reply with only these lines, nothing before or after:\n" +
        "Question: the question, ending with a question mark\n" +
        "- Label (recommended) → done: what it means\n" +
        "- Label → back: what it means\n" +
        "- Label → stay: what it means\n" +
        "Context: one sentence of what they need to know to answer\n\n" +
        "Mark exactly one option (recommended). After the arrow: done moves the card to done, back sends it back for more work, stay leaves it, " +
        "and no arrow sends the answer to an agent to act on. Use only what the card says; if it doesn't say what was done, ask whether it is right as it stands.\n\n" +
        $"The card ({card.Id}: {card.Title}):\n\n" + (cardText.Length > 6000 ? cardText[..6000] : cardText);

    /// <summary>Haiku's draft, or null unless it is exactly the shape asked for: a question ending in ?, two to four
    /// options with labels of a few words, and nothing else of length around them.</summary>
    public static CardDecision? ParseDraft(string? reply, DateTime when)
    {
        if (reply is null) return null;
        var d = Parse(reply.Trim().Trim('`').Trim());
        if (!d.Valid || d.Options.Count > 4 || !d.Question.EndsWith('?') || d.Question.Length > 300) return null;
        if (d.Options.Any(o => o.Label.Length > 60 || o.Label.Contains('\n'))) return null;
        // everything Haiku said must be part of the decision: no preamble or sign-off that could be advice in disguise
        var lines = reply.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim().Trim('`')).Where(l => l.Length > 0).ToList();
        if (lines.Any(l => !FieldRx().IsMatch(l) && !BulletRx().IsMatch(l) && !(d.Context.Contains(l)))) return null;
        // one recommendation at most: the first marked
        var first = d.Options.ToList().FindIndex(o => o.Recommended);
        d = d with { Options = d.Options.Select((o, i) => o with { Recommended = i == first }).ToList() };
        return d with { AskedBy = $"Darg, drafted from the card ({when:yyyy-MM-dd HH:mm}); check it" };
    }

    /// <summary>Ask Haiku for a draft; null when it fails or its reply is refused.</summary>
    public static async Task<CardDecision?> Draft(Card card, string cardText, string attentionColumn, TimeSpan? timeout = null) =>
        ParseDraft(await Haiku.Ask(DraftPrompt(card, cardText, attentionColumn), timeout ?? TimeSpan.FromSeconds(60)), DateTime.Now);

    /// <summary>Why the card is there, for the no-decision box: the newest Why it needs attention entry, else the
    /// start of How to check, else nothing.</summary>
    public static string WhyHere(string cardText)
    {
        var why = DecisionReport.Section(cardText, "Why it needs attention");
        if (why.Length > 0)
        {
            // dated ### entries, newest last
            var parts = Regex.Split(why, @"\n(?=###\s)");
            var last = Regex.Replace(parts[^1], @"^###[^\n]*\n?", "").Trim();
            return Short(last.Length > 0 ? last : why);
        }
        var how = DecisionReport.Section(cardText, "How to check");
        return how.Length > 0 ? Short(how) : "";
    }

    static string Short(string s) { s = s.Trim(); return s.Length <= 400 ? s : s[..400].TrimEnd() + "…"; }

    [GeneratedRegex(@"^\s*\**\s*(Question|Asked by|Context)\s*\**\s*:\s*\**\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FieldRx();
    [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+(?:\[[ xX]?\]\s*)?(.+)$")]
    private static partial Regex BulletRx();
    [GeneratedRegex(@"\s*(?:\(|\s→|\s?->|\s?=>|:|\s[–—]\s|\s-\s)")]
    private static partial Regex PlainLabelEndRx();
    [GeneratedRegex(@"\s*[\(\[]\s*recommended\s*[\)\]]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex RecommendedRx();
    [GeneratedRegex(@"^(?:[\(\[]\s*recommended\s*[\)\]]|[–—-]\s*recommended\b|,\s*recommended\b)", RegexOptions.IgnoreCase)]
    private static partial Regex RecommendedLeadRx();
    [GeneratedRegex(@"^(?:→|->|=>)\s*(send back|[a-z]+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ArrowRx();
    [GeneratedRegex(@"(?:^|(?<=\n))##[ \t]+Decision[ \t]*\n(?:.*?)(?=\n##[ \t]|\z)", RegexOptions.Singleline)]
    private static partial Regex OpenSectionRx();
    [GeneratedRegex(@"\bagent\s+([0-9a-f]{6,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex AgentIdRx();
}
