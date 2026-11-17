using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A title for a card made from a post-it, written by Claude Haiku after the card exists (WOR-0082).
/// The card is made at once with <see cref="NoteStore.TicketText"/>'s title, which loses no words; Haiku then
/// retitles it in the background, about ten seconds later. Tiny local models were tried first and failed:
/// qwen3:1.7b titled a quarter of the wall "bug", mistral 7B invented things the note never said.</summary>
public static class TicketTitle
{
    /// <summary>At most this many Haiku calls at once, so making twenty tickets doesn't start twenty processes.</summary>
    static readonly SemaphoreSlim Slots = new(3);

    public static string Prompt(string note)
    {
        note = note.Trim();
        if (note.Length > 2000) note = note[..2000];
        return "Write a short title for a ticket made from this post-it note: at most 60 characters, in the note's own " +
               "words and voice (lower case is fine), saying what is wanted. Use only what the note says. Plain words: no " +
               "quotes, no full stop, no emoji. Reply with the title and nothing else.\n\nNote:\n" + note;
    }

    static readonly Regex Word = new(@"[\p{L}\p{N}']+");

    /// <summary>A model's reply as a title, or null when it isn't one. What comes back wrong from a model asked for
    /// a title, and is refused here: wrapped (quotes, backticks, a code fence, "Title:", bold, a heading, a full
    /// stop: stripped); talk around it (a second line, "Here's a title"); too long or too short; and words the
    /// note never said (an invented feature, an echo of the instructions), caught by needing more than half of
    /// the title's longer words to start as one of the note's words do.</summary>
    public static string? Clean(string reply, string note)
    {
        var lines = reply.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("```", StringComparison.Ordinal)).ToList();
        if (lines.Count != 1) return null;
        var t = Regex.Replace(lines[0], @"^(?:#+\s*)?(?:\*\*)?(?:ticket\s+)?title\s*:\s*", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t.Trim().Trim('"', '\'', '*', '`', '“', '”', '‘', '’', ' '), @"\s+", " ").TrimEnd('.', ' ');
        if (t.Length < 3 || t.Length > NoteStore.TitleMax || t.Split(' ').Length > 12) return null;
        if (Regex.IsMatch(t, @"^(?:here(?:'s| is)|sure|okay|ok)\b", RegexOptions.IgnoreCase)) return null;
        static string Stem(string w) => w.ToLowerInvariant()[..Math.Min(4, w.Length)];
        var noteStems = Word.Matches(note).Select(m => Stem(m.Value)).ToHashSet();
        var long_ = Word.Matches(t).Select(m => m.Value).Where(w => w.Length >= 4).ToList();
        if (long_.Count > 0 && long_.Count(w => noteStems.Contains(Stem(w))) * 2 <= long_.Count) return null;
        return t;
    }

    /// <summary>Haiku's title for a note, or null when Claude isn't there, is slow, or replies with something
    /// <see cref="Clean"/> refuses. Never throws.</summary>
    public static async Task<string?> Suggest(string note, TimeSpan? timeout = null)
    {
        await Slots.WaitAsync();
        try
        {
            var reply = await Haiku.Ask(Prompt(note), timeout ?? TimeSpan.FromSeconds(60));
            return reply is null ? null : Clean(reply, note);
        }
        finally { Slots.Release(); }
    }

    /// <summary>Give a card made from a note Haiku's title, if the card still has the title it was made with
    /// (someone renaming it meanwhile wins). A title that wasn't cut ("…") was the note's own words, so it goes
    /// to the top of the description to keep them on the card. The card as saved, or null if left alone.
    /// Never throws.</summary>
    public static async Task<Card?> Retitle(string boardFolder, string cardId, string madeAs, string note, Func<string, Task<string?>>? suggest = null)
    {
        try
        {
            var title = await (suggest ?? (n => Suggest(n)))(note);
            if (title is null || title == madeAs) return null;
            var card = BoardStore.ReadCards(boardFolder).FirstOrDefault(c => c.Id == cardId);
            if (card is null || card.Title != madeAs) return null;
            var description = madeAs.EndsWith('…') ? null : madeAs + "\n\n" + card.Description.Trim();
            return BoardStore.Update(boardFolder, cardId, title: title, description: description);
        }
        catch (Exception) { return null; }
    }
}
