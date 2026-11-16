using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>Short titles for chats: what the chat is about in a few words, so a list of them reads
/// like a list of jobs rather than of times.</summary>
public static class ChatTitle
{
    static readonly Regex Context = new(@"^\[Philip is looking at:[^\]]*\]\s*", RegexOptions.Singleline);
    static readonly Regex Filler = new(@"^(?:(?:please|pls|can you|could you|would you|will you|i want you to|i'd like you to|hey|hi|ok|okay|so|bench)[,!.]?(?:\s+|$))+", RegexOptions.IgnoreCase);

    /// <summary>A title from the words alone (no model): the first line, polite openers and the context line
    /// dropped, cut at a word near 48 characters, first letter capitalised.</summary>
    public static string Guess(string message)
    {
        var t = Context.Replace(message.Replace("\r\n", "\n"), "").Trim();
        t = t.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        t = Filler.Replace(t, "").Trim().TrimEnd('?', '.', '!', ',');
        if (t.Length == 0) return "Chat";
        if (t.Length > 48)
        {
            var cut = t.LastIndexOf(' ', 48);
            t = (cut > 20 ? t[..cut] : t[..48]).TrimEnd(',', ';', ':', '-') + "…";
        }
        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    /// <summary>A title written by a small model (Claude Haiku, one turn, no tools), or the guess if that
    /// fails or takes too long. Never throws.</summary>
    public static async Task<string> Make(string message, TimeSpan? timeout = null)
    {
        var guess = Guess(message);
        var text = WithoutContext(message);
        if (text.Length > 2000) text = text[..2000];
        var prompt = "Write a title of 3 to 6 words for this request to an assistant, saying what it's about, like a task name. " +
                     "Plain words only: no quotes, no full stop, no emoji. Reply with the title and nothing else.\n\nRequest:\n" + text;
        var output = await Haiku.Ask(prompt, timeout ?? TimeSpan.FromSeconds(45));
        return output is null ? guess : Clean(output) ?? guess;
    }

    /// <summary>A message without the "[Philip is looking at: …]" line Fettle's chats carry in front.</summary>
    public static string WithoutContext(string message) => Context.Replace(message, "").Trim();

    /// <summary>A model's reply as a title, or null if it doesn't look like one.</summary>
    public static string? Clean(string reply)
    {
        var t = reply.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        t = Regex.Replace(t, @"^(title:\s*)", "", RegexOptions.IgnoreCase).Trim().Trim('"', '\'', '*', '`', '#', ' ', '.');
        if (t.Length < 3 || t.Length > 60 || t.Split(' ').Length > 9) return null;
        return char.ToUpperInvariant(t[0]) + t[1..];
    }
}
