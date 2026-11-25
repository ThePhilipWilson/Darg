using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>One card a story holds: the id named, and the card it is (null when no board of the project has it).</summary>
public sealed record StoryMember(string Id, BoardCard? Card)
{
    public bool Found => Card is not null;
    /// <summary>A line saying what it is and where, for the story's panel.</summary>
    public string Text => Card is not { } c ? $"{Id}: no board of this project has it"
        : $"{Id}: {c.Card.Title} ({c.Column}{(c.Board.Length > 0 ? " on " + c.Board : "")})";
}

/// <summary>The cards a story holds (WOR-0217). The wrap-up (<see cref="Stories.Prompt"/>) writes a <c>Cards: WOR-0100,
/// WOR-0101</c> line into the story's description and a <c>Story: WOR-0300 name</c> line into each card it wraps; both
/// are read, so a card named only one way still shows. Agents write these lines by hand, so the shapes they drift into
/// are taken too: a list marker, bold (<c>**Cards:**</c>), any case, ids as card links, and a <c>## Cards</c> section
/// whose list items start with the id.</summary>
public static class StoryCards
{
    static readonly Regex Id = new(@"\b([A-Za-z][A-Za-z0-9]*-\d+)\b");
    /// <summary>A <c>Cards:</c> line: optional list marker, the word plain or bold, the colon inside or outside the bold.</summary>
    static readonly Regex CardsLine = new(@"^[ \t]*(?:[-*+][ \t]+)?(?:\*\*|__)?Cards(?:\*\*|__)?[ \t]*:(?:\*\*|__)?[ \t]*(.*)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);
    /// <summary>A <c>Story:</c> line naming the story's id first (a card link or bare).</summary>
    static readonly Regex StoryLine = new(@"^[ \t]*(?:[-*+][ \t]+)?(?:\*\*|__)?Story(?:\*\*|__)?[ \t]*:(?:\*\*|__)?[ \t]*\[?([A-Za-z][A-Za-z0-9]*-\d+)\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);
    /// <summary>A list item under a <c>## Cards</c> heading that starts with an id (bold, a link or bare).</summary>
    static readonly Regex SectionItem = new(@"^[ \t]*(?:[-*+]|\d+\.)[ \t]+(?:\*\*|__)?\[?([A-Za-z][A-Za-z0-9]*-\d+)\b", RegexOptions.Multiline);

    /// <summary>The ids the story names as its cards, upper-cased, each once, in the order written: its <c>Cards:</c>
    /// lines, then a <c>## Cards</c> section's items. Never the story's own id.</summary>
    public static IReadOnlyList<string> Named(Card story)
    {
        var text = (story.Description + "\n" + story.Sections).Replace("\r\n", "\n");
        var ids = new List<string>();
        void Add(string id)
        {
            id = id.ToUpperInvariant();
            if (!id.Equals(story.Id, StringComparison.OrdinalIgnoreCase) && !ids.Contains(id)) ids.Add(id);
        }
        foreach (Match line in CardsLine.Matches(text))
            foreach (Match m in Id.Matches(line.Groups[1].Value)) Add(m.Groups[1].Value);
        if (CardsSection(text) is { } section)
            foreach (Match m in SectionItem.Matches(section)) Add(m.Groups[1].Value);
        return ids;
    }

    /// <summary>The story id a card's <c>Story:</c> line names, or null.</summary>
    public static string? StoryOf(Card card)
    {
        var m = StoryLine.Match((card.Description + "\n" + card.Sections).Replace("\r\n", "\n"));
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>The cards the story holds, as things stand: those it names (found or not, in the order written), then
    /// any other card whose <c>Story:</c> line names it, in board order.</summary>
    public static IReadOnlyList<StoryMember> Of(Card story, IReadOnlyList<BoardCard> all)
    {
        var index = Blockers.Index(all);
        var members = Named(story).Select(id => new StoryMember(id, index.GetValueOrDefault(id))).ToList();
        foreach (var c in all)
        {
            if (c.Card.Id.Equals(story.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (!story.Id.Equals(StoryOf(c.Card), StringComparison.OrdinalIgnoreCase)) continue;
            if (members.Any(m => m.Id.Equals(c.Card.Id, StringComparison.OrdinalIgnoreCase))) continue;
            members.Add(new StoryMember(c.Card.Id.ToUpperInvariant(), c));
        }
        return members;
    }

    /// <summary>The body of a <c>## Cards</c> section, or null.</summary>
    static string? CardsSection(string text)
    {
        var m = Regex.Match("\n" + text, @"\n##[ \t]+Cards[ \t]*\n", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var rest = ("\n" + text)[(m.Index + m.Length)..];
        var next = rest.IndexOf("\n## ", StringComparison.Ordinal);
        return next >= 0 ? rest[..next] : rest;
    }
}
