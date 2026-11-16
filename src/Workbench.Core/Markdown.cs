using System.Text;
using System.Text.RegularExpressions;

namespace Workbench.Core;

/// <summary>A run of inline text with its styling.</summary>
public sealed record Span(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null);

public abstract record MdBlock;
public sealed record MdHeading(int Level, IReadOnlyList<Span> Spans) : MdBlock;
public sealed record MdParagraph(IReadOnlyList<Span> Spans) : MdBlock;
public sealed record MdList(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<Span>> Items) : MdBlock;
public sealed record MdCode(string Language, string Text) : MdBlock;
public sealed record MdQuote(IReadOnlyList<Span> Spans) : MdBlock;
public sealed record MdTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<Span>>> Rows) : MdBlock; // first row is the header
public sealed record MdRule : MdBlock;

/// <summary>The markdown Claude writes, as blocks to draw: headings, paragraphs, lists, fenced code,
/// quotes, tables and rules, with bold, italic, inline code and links inside them. Not a full
/// CommonMark parser: enough for transcripts, and anything it doesn't know stays plain text.</summary>
public static class Markdown
{
    static readonly Regex Bullet = new(@"^\s*[-*+]\s+(.*)$");
    static readonly Regex Numbered = new(@"^\s*(\d+)[.)]\s+(.*)$");
    static readonly Regex Heading = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
    static readonly Regex TableSep = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$");

    /// <summary>Text without Obsidian's properties block (a --- fenced block at the very top).</summary>
    public static string StripProperties(string text)
    {
        var t = text.Replace("\r\n", "\n");
        if (!t.StartsWith("---\n")) return t;
        var end = t.IndexOf("\n---", 4, StringComparison.Ordinal);
        return end < 0 ? t : t[(end + 4)..].TrimStart('\n');
    }

    public static List<MdBlock> Parse(string text)
    {
        var lines = StripProperties(text).Split('\n');
        var blocks = new List<MdBlock>();
        var para = new List<string>();
        void Flush()
        {
            // a line break inside a paragraph stays a line break (as chat and GitHub comments show it), not a space
            if (para.Count > 0) blocks.Add(new MdParagraph(Inline(string.Join("\n", para.Select(l => l.Trim())))));
            para.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```"))
            {
                Flush();
                var lang = line.Trim()[3..].Trim();
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Append(lines[i]).Append('\n');
                blocks.Add(new MdCode(lang, code.ToString().TrimEnd('\n')));
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) { Flush(); continue; }
            if (Heading.Match(line) is { Success: true } h) { Flush(); blocks.Add(new MdHeading(h.Groups[1].Length, Inline(h.Groups[2].Value))); continue; }
            if (Regex.IsMatch(line, @"^\s*([-*_])(\s*\1){2,}\s*$")) { Flush(); blocks.Add(new MdRule()); continue; }
            if (line.TrimStart().StartsWith('>'))
            {
                Flush();
                var q = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('>'); i++) q.Add(lines[i].TrimStart()[1..].Trim());
                i--;
                blocks.Add(new MdQuote(Inline(string.Join(" ", q))));
                continue;
            }
            if (line.Contains('|') && i + 1 < lines.Length && TableSep.IsMatch(lines[i + 1]))
            {
                Flush();
                var rows = new List<IReadOnlyList<IReadOnlyList<Span>>> { Cells(line) };
                for (i += 2; i < lines.Length && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i]); i++) rows.Add(Cells(lines[i]));
                i--;
                blocks.Add(new MdTable(rows));
                continue;
            }
            var bullet = Bullet.Match(line);
            var num = Numbered.Match(line);
            if (bullet.Success || num.Success)
            {
                Flush();
                var ordered = num.Success && !bullet.Success;
                var start = ordered ? int.Parse(num.Groups[1].Value) : 1;
                var items = new List<string>();
                for (; i < lines.Length; i++)
                {
                    var b = Bullet.Match(lines[i]); var n = Numbered.Match(lines[i]);
                    if (ordered ? n.Success : b.Success) items.Add((ordered ? n.Groups[2] : b.Groups[1]).Value);
                    else if (items.Count > 0 && lines[i].Length > 0 && char.IsWhiteSpace(lines[i][0]) && !string.IsNullOrWhiteSpace(lines[i]))
                        items[^1] += " " + lines[i].Trim(); // a wrapped or nested line joins its item
                    else break;
                }
                i--;
                blocks.Add(new MdList(ordered, start, items.Select(Inline).ToList()));
                continue;
            }
            para.Add(line);
        }
        Flush();
        return blocks;
    }

    static IReadOnlyList<IReadOnlyList<Span>> Cells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Split('|').Select(c => (IReadOnlyList<Span>)Inline(c.Trim())).ToList();
    }

    /// <summary>Inline styling: `code`, **bold**, *italic* / _italic_, [text](url), [[wikilinks]], &lt;https://…&gt; and
    /// bare https:// links. Unclosed markers stay as text.</summary>
    public static List<Span> Inline(string s)
    {
        var spans = new List<Span>();
        var buf = new StringBuilder();
        bool bold = false, italic = false;
        void Emit() { if (buf.Length > 0) { spans.Add(new Span(buf.ToString(), bold, italic)); buf.Clear(); } }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '`')
            {
                var end = s.IndexOf('`', i + 1);
                if (end > i) { Emit(); spans.Add(new Span(s[(i + 1)..end], bold, italic, Code: true)); i = end; continue; }
            }
            if (c == '[' && i + 1 < s.Length && s[i + 1] == '[')
            {
                // Obsidian's [[note]] or [[note|shown text]]
                var end = s.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    Emit();
                    var inner = s[(i + 2)..end];
                    var bar = inner.IndexOf('|');
                    spans.Add(new Span(bar >= 0 ? inner[(bar + 1)..] : inner, bold, italic, Link: "wiki:" + (bar >= 0 ? inner[..bar] : inner)));
                    i = end + 1;
                    continue;
                }
            }
            if (c == '[')
            {
                var close = s.IndexOf("](", i + 1, StringComparison.Ordinal);
                var paren = close < 0 ? -1 : CloseParen(s, close + 2);
                if (close > i && paren > close)
                {
                    Emit();
                    spans.Add(new Span(s[(i + 1)..close], bold, italic, Link: s[(close + 2)..paren]));
                    i = paren;
                    continue;
                }
            }
            if (c == '<' && Url(s, i + 1) is { } inAngles && i + 1 + inAngles < s.Length && s[i + 1 + inAngles] == '>')
            {
                // <https://example.com>
                Emit();
                var url = s.Substring(i + 1, inAngles);
                spans.Add(new Span(url, bold, italic, Link: url));
                i += inAngles + 1;
                continue;
            }
            if ((c == 'h' || c == 'H') && (i == 0 || !char.IsLetterOrDigit(s[i - 1])) && Url(s, i) is { } bare)
            {
                // a bare https://example.com/page, without the full stop or bracket after it
                var url = s.Substring(i, bare).TrimEnd('.', ',', ';', ':', '!', '?', '\'', '"', '*');
                while (url.EndsWith(')') && url.Count(ch => ch == ')') > url.Count(ch => ch == '(')) url = url[..^1];
                Emit();
                spans.Add(new Span(url, bold, italic, Link: url));
                i += url.Length - 1;
                continue;
            }
            if ((c == '*' || c == '_') && i + 1 < s.Length && s[i + 1] == c && s.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal) > 0 | bold)
            {
                Emit(); bold = !bold; i++; continue;
            }
            if ((c == '*' || c == '_') && (italic || (i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]) && s.IndexOf(c, i + 1) > 0))
                && !(c == '_' && i > 0 && char.IsLetterOrDigit(s[i - 1]) && i + 1 < s.Length && char.IsLetterOrDigit(s[i + 1])))
            {
                Emit(); italic = !italic; continue;
            }
            buf.Append(c);
        }
        Emit();
        return spans;
    }

    /// <summary>The length of an http(s) URL starting at <paramref name="at"/> (up to whitespace or &lt;&gt;), or null.</summary>
    static int? Url(string s, int at)
    {
        var rest = s.AsSpan(at);
        var scheme = rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8 : rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7 : 0;
        if (scheme == 0) return null;
        var n = scheme;
        while (n < rest.Length && !char.IsWhiteSpace(rest[n]) && rest[n] is not ('<' or '>' or '`')) n++;
        return n > scheme ? n : null;
    }

    /// <summary>The ')' that closes a link target opened just before <paramref name="from"/>, allowing
    /// balanced brackets inside it (https://en.wikipedia.org/wiki/Foo_(bar)) and a &lt;target in angles&gt;; -1 if none.</summary>
    static int CloseParen(string s, int from)
    {
        if (from < s.Length && s[from] == '<')
        {
            var gt = s.IndexOf('>', from);
            if (gt > 0) return s.IndexOf(')', gt);
        }
        var depth = 0;
        for (var i = from; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && depth-- == 0) return i;
        }
        return s.IndexOf(')', from);
    }
}
