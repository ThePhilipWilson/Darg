using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>How ready a card is to be picked up cold (WOR-0156): a star out of 3 beside its id, a point each for
/// saying what's wanted, where it lives and when it's done, the three things Improve (<see cref="CardImprover"/>)
/// writes. Read from the card's text every time it's shown, so it follows the file: an Improve agent's rewrite
/// rescores the card as the board reloads. Black at 0, red at 1, amber at 2, green at 3; the colour says it, so the star carries no digit (the tooltip does).</summary>
public sealed record TicketScore(int Stars, IReadOnlyList<string> Has, IReadOnlyList<string> Missing)
{
    public const int Max = 3;
    public bool Zero => Stars <= 0;
    public bool Red => Stars == 1;
    public bool Amber => Stars == 2;
    public bool Green => Stars >= Max;
    public string Colour => Green ? "green" : Amber ? "amber" : Red ? "red" : "black";
    /// <summary>"★": what the card's tile shows; the colour is the score.</summary>
    public string Text => "★";
    /// <summary>What the card's panel shows: the same star, no digit.</summary>
    public string Long => "★";
    /// <summary>The tooltip: the score, what the card says and what it's missing.</summary>
    public string Tip =>
        $"Ticket quality {Stars}/{Max}" +
        (Has.Count > 0 ? $"\nSays {string.Join(", ", Has)}." : "") +
        (Missing.Count > 0 ? $"\nDoesn't say {string.Join(", ", Missing)}: ✦ Improve this card fills it out." : "");
}

public static partial class TicketQuality
{
    public const string Wanted = "what's wanted", Where = "where it lives", Done = "when it's done";

    /// <summary>Words of description it takes to say what's wanted, besides the title.</summary>
    public const int WantedWords = 20;

    // the bookkeeping lines a card carries that say nothing of what's wanted
    [GeneratedRegex(@"^\s*From a note on the wall.*$|!\[[^\]]*\]\([^)]*\)|<img[^>]*>", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex Bookkeeping();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*")]
    private static partial Regex Word();

    // a Where label or heading; a file or path; code in backticks
    [GeneratedRegex(@"^\s*#+\s*(?:Where|Files?|Location|Code)\b" +
                    @"|^\s*(?:[-*]\s*)?(?:\*\*|__)?(?:Where|Files?|Location)\b[^\n]{0,12}?:" +
                    @"|\b[\w.\-]+\.(?:cs|axaml|xaml|csproj|sln|md|ps1|psm1|py|js|mjs|ts|tsx|jsx|json|css|scss|html|yml|yaml|toml|xml|sh|bat|cmd|shader|uss|uxml|asset|prefab|unity)\b" +
                    @"|(?:\b[A-Za-z]:[/\\]|~[/\\]|\b(?:src|tools|tests|docs|Assets|Scripts)[/\\])[\w.\-/\\]+" +
                    @"|`[^`\n]{2,}`",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex WhereMark();

    // a Done when label or heading, acceptance or expected behaviour, repro steps, or a checklist
    [GeneratedRegex(@"^\s*(?:[-*]\s*)?(?:#+\s*|\*\*|__)?(?:Done when|Done if|Definition of done|Acceptance|Expected|Steps to reproduce|Repro(?:duce)?|Success when)\b" +
                    @"|^\s*[-*]\s*\[[ xX]\]",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DoneMark();

    // sections the work adds to a card, not the ticket: how to check it, QA's notes, a decision, a CNR
    static readonly string[] WorkSections = ["How to check", "QA notes", "Decision", "Could not reproduce"];

    /// <summary>The card's description and the sections after it that are part of the ticket (## Where, ## Done
    /// when, ## Proposal…), leaving out those its work added (<see cref="WorkSections"/>).</summary>
    public static string TicketText(Card card) => TicketText(card.Description, card.Sections);

    public static string TicketText(string description, string sections)
    {
        var parts = new List<string> { description };
        foreach (var section in ("\n" + sections.Replace("\r\n", "\n")).Split("\n## ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var heading = section.Split('\n', 2)[0].Trim();
            if (!WorkSections.Any(w => heading.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
                parts.Add("## " + section.Trim());
        }
        return string.Join("\n\n", parts);
    }

    public static TicketScore Score(Card card) => Score(TicketText(card));

    /// <summary>Score a ticket's text: its description and any sections of the ticket after it.</summary>
    public static TicketScore Score(string ticket)
    {
        var text = Bookkeeping().Replace(ticket ?? "", " ");
        var has = new List<string>();
        var missing = new List<string>();
        void Mark(bool ok, string what) => (ok ? has : missing).Add(what);
        Mark(Word().Matches(text).Count >= WantedWords, Wanted);
        Mark(WhereMark().IsMatch(text), Where);
        Mark(DoneMark().IsMatch(text), Done);
        return new TicketScore(has.Count, has, missing);
    }
}
