using Darg.Core;

namespace Darg.Core.Tests;

public sealed class MarkdownTests
{
    [Theory]
    [InlineData("see https://example.com/page.", "https://example.com/page", "see ", ".")]
    [InlineData("(https://example.com/a)", "https://example.com/a", "(", ")")]
    [InlineData("go to <https://example.com> now", "https://example.com", "go to ", " now")]
    [InlineData("[wiki](https://en.wikipedia.org/wiki/Foo_(bar)) after", "https://en.wikipedia.org/wiki/Foo_(bar)", "", " after")]
    [InlineData("[notes](<My Notes.md>) after", "<My Notes.md>", "", " after")]
    public void Links_are_found(string text, string link, string before, string after)
    {
        var spans = Markdown.Inline(text);
        var i = spans.FindIndex(s => s.Link == link);
        Assert.True(i >= 0, string.Join(" | ", spans.Select(s => $"{s.Text}->{s.Link}")));
        Assert.Equal(before, string.Concat(spans.Take(i).Select(s => s.Text)));
        Assert.Equal(after, string.Concat(spans.Skip(i + 1).Select(s => s.Text)));
    }

    [Fact]
    public void Urls_in_code_and_words_stay_text()
    {
        Assert.DoesNotContain(Markdown.Inline("run `curl https://x.io` now"), s => s.Link is not null);
        Assert.DoesNotContain(Markdown.Inline("nothttps://x.io"), s => s.Link is not null);
    }

    [Fact]
    public void Blocks_come_out_in_order()
    {
        var b = Markdown.Parse("""
            ## Done

            I changed **two** cards:
            - TST-0001
            - TST-0002, which
              wraps

            1. first
            2. second

            ```cs
            var x = 1;
            ```

            | Card | Label |
            |---|---|
            | TST-0001 | `checked` |

            > a quote
            ---
            """);
        Assert.Collection(b,
            x => Assert.Equal(2, Assert.IsType<MdHeading>(x).Level),
            x => Assert.Contains(Assert.IsType<MdParagraph>(x).Spans, s => s.Bold && s.Text == "two"),
            x => { var l = Assert.IsType<MdList>(x); Assert.False(l.Ordered); Assert.Equal("TST-0002, which wraps", string.Concat(l.Items[1].Select(s => s.Text))); },
            x => { var l = Assert.IsType<MdList>(x); Assert.True(l.Ordered); Assert.Equal(2, l.Items.Count); },
            x => Assert.Equal(("cs", "var x = 1;"), (Assert.IsType<MdCode>(x).Language, ((MdCode)x).Text)),
            x => { var t = Assert.IsType<MdTable>(x); Assert.Equal(2, t.Rows.Count); Assert.True(t.Rows[1][1][0].Code); },
            x => Assert.IsType<MdQuote>(x),
            x => Assert.IsType<MdRule>(x));
    }

    [Fact]
    public void Line_breaks_in_a_paragraph_stay()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(Markdown.Parse("1 Apple\n2 Banana\n3 Cherry")));
        Assert.Equal("1 Apple\n2 Banana\n3 Cherry", string.Concat(p.Spans.Select(s => s.Text)));
    }

    [Fact]
    public void Inline_styles()
    {
        var s = Markdown.Inline("a **bold** and *it* `code` [link](http://x) end");
        Assert.Contains(s, x => x is { Text: "bold", Bold: true });
        Assert.Contains(s, x => x is { Text: "it", Italic: true });
        Assert.Contains(s, x => x is { Text: "code", Code: true });
        Assert.Contains(s, x => x is { Text: "link", Link: "http://x" });
        Assert.Equal("a bold and it code link end", string.Concat(s.Select(x => x.Text)));
    }

    [Fact]
    public void Obsidian_properties_are_left_out_and_wikilinks_read_as_links()
    {
        var b = Markdown.Parse("---\ntags: [a]\ncreated: x\n---\n# Title\nSee [[DESIGN-NOTES]] and [[UI-FIGHT|the fight screen]].");
        Assert.Equal("Title", string.Concat(Assert.IsType<MdHeading>(b[0]).Spans.Select(s => s.Text)));
        var spans = Assert.IsType<MdParagraph>(b[1]).Spans;
        Assert.Contains(spans, s => s is { Text: "DESIGN-NOTES", Link: "wiki:DESIGN-NOTES" });
        Assert.Contains(spans, s => s is { Text: "the fight screen", Link: "wiki:UI-FIGHT" });
    }

    [Theory] // markers that aren't styling stay as text
    [InlineData("snake_case_name stays", "snake_case_name stays")]
    [InlineData("2 * 3 = 6", "2 * 3 = 6")]
    [InlineData("an unclosed `tick", "an unclosed `tick")]
    [InlineData("C:\\path\\to [not a link", "C:\\path\\to [not a link")]
    public void Plain_text_survives(string input, string expected) =>
        Assert.Equal(expected, string.Concat(Markdown.Inline(input).Select(x => x.Text)));
}
