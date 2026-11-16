using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class MarkdownTests
{
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
