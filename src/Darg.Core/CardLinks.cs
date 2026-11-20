using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>Links to board cards in markdown (WOR-0158): <c>[WOR-0158](card:WOR-0158)</c>, which a transcript
/// (Fettle's, an agent's) or a doc draws as a link that opens the card's panel. Fettle is told to write one for a
/// card it has made, and new-card hands it the link ready-made; a card ID written bare in the text (Fettle forgot,
/// or wrote "made WOR-0160") is linked too, when its prefix is one of the instance's boards', so the reply still
/// links whichever way it was written. IDs in inline code and in another link are left alone.</summary>
public static class CardLinks
{
    public const string Scheme = "card:";

    /// <summary>The markdown link to a card: its ID, opening its panel.</summary>
    public static string For(string id) => $"[{id}]({Scheme}{id})";

    /// <summary>The card ID a link points at, or null for any other link. Takes card:WOR-1, card://WOR-1 and card:&lt;WOR-1&gt;.</summary>
    public static string? IdOf(string? link)
    {
        if (link is null || !link.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return null;
        var id = link[Scheme.Length..].Trim().TrimStart('/').Trim('<', '>', ' ');
        return CardId.IsMatch(id) ? id : null;
    }

    static readonly Regex CardId = new(@"^[A-Z][A-Z0-9]*-\d+$");
    // an ID standing alone: not part of a longer word, path segment or branch (wor-0158 is a branch, not a card)
    static readonly Regex BareId = new(@"(?<![\w-])(?<prefix>[A-Z][A-Z0-9]*)-\d+(?![\w-])");

    /// <summary>The spans with each bare card ID in plain text made a card link; <paramref name="isPrefix"/> says
    /// whether a prefix (WOR) is a board's. Code, links and anything else are as they were.</summary>
    public static IReadOnlyList<Span> Link(IReadOnlyList<Span> spans, Func<string, bool> isPrefix)
    {
        List<Span>? result = null; // built only once something is linked
        for (var i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (s.Code || s.Link is not null || !s.Text.Contains('-'))
            {
                result?.Add(s);
                continue;
            }
            var at = 0;
            foreach (Match m in BareId.Matches(s.Text))
            {
                if (!isPrefix(m.Groups["prefix"].Value)) continue;
                result ??= [.. spans.Take(i)];
                if (m.Index > at) result.Add(s with { Text = s.Text[at..m.Index] });
                result.Add(s with { Text = m.Value, Link = Scheme + m.Value });
                at = m.Index + m.Length;
            }
            if (result is null) continue;
            if (at < s.Text.Length) result.Add(at == 0 ? s : s with { Text = s.Text[at..] });
        }
        return result ?? spans;
    }

    /// <summary>The card ID prefixes of the boards in these boards folders (each board.json's prefix; T when it has none, as BoardStore.Create numbers).</summary>
    public static HashSet<string> Prefixes(IEnumerable<string?> boardsFolders)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in boardsFolders)
            foreach (var board in BoardStore.List(folder))
            {
                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(Path.Combine(folder!, board, "board.json")));
                    set.Add((string?)json?["prefix"] is { Length: > 0 } p ? p : "T");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
            }
        return set;
    }
}
