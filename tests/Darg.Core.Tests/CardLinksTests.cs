using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0158: card links in markdown, written as [ID](card:ID) or as a bare ID.</summary>
public sealed class CardLinksTests
{
    static bool Wor(string prefix) => prefix == "WOR";
    static string Show(IEnumerable<Span> spans) => string.Join(" | ", spans.Select(s => s.Link is null ? s.Text : $"{s.Text}->{s.Link}"));

    [Fact]
    public void A_card_link_parses_and_names_its_card()
    {
        var link = CardLinks.For("WOR-0158");
        Assert.Equal("[WOR-0158](card:WOR-0158)", link);
        var span = Assert.Single(Markdown.Inline(link));
        Assert.Equal("WOR-0158", span.Text);
        Assert.Equal("WOR-0158", CardLinks.IdOf(span.Link));
    }

    [Theory]
    [InlineData("card:WOR-0158", "WOR-0158")]
    [InlineData("card://WOR-0158", "WOR-0158")]
    [InlineData("CARD:WOR-7", "WOR-7")]
    [InlineData("card:<WOR-0158>", "WOR-0158")]
    [InlineData("card:wor-0158", null)] // a branch name, not an ID
    [InlineData("card:", null)]
    [InlineData("https://example.com/WOR-0158", null)]
    [InlineData("wiki:WOR-0158", null)]
    [InlineData(null, null)]
    public void IdOf_takes_only_card_links(string? link, string? id) => Assert.Equal(id, CardLinks.IdOf(link));

    [Theory]
    [InlineData("Made WOR-0160: CSV export.", "Made  | WOR-0160->card:WOR-0160 | : CSV export.")]
    [InlineData("WOR-0160", "WOR-0160->card:WOR-0160")]
    [InlineData("WOR-1 and WOR-2.", "WOR-1->card:WOR-1 |  and  | WOR-2->card:WOR-2 | .")]
    [InlineData("(WOR-0160)", "( | WOR-0160->card:WOR-0160 | )")]
    public void A_bare_ID_with_a_boards_prefix_is_linked(string text, string expected) =>
        Assert.Equal(expected, Show(CardLinks.Link(Markdown.Inline(text), Wor)));

    [Theory]
    [InlineData("on branch wor-0160")] // lower case: a branch
    [InlineData("ABC-0160 is another instance's")] // no board has the prefix
    [InlineData("see XWOR-0160 or WOR-0160x or WOR-0160-2")] // part of a longer word
    [InlineData("run `git log --grep WOR-0160`")] // code stays code
    [InlineData("[the card](https://example.com/WOR-0160)")] // already a link
    [InlineData("[WOR-0160](card:WOR-0160)")] // already a card link: not linked twice
    [InlineData("a plain sentence")]
    public void Anything_else_stays_as_it_was(string text)
    {
        var spans = Markdown.Inline(text);
        Assert.Same(spans, CardLinks.Link(spans, Wor));
    }

    [Fact]
    public void Styling_is_kept_on_a_linked_ID()
    {
        var spans = CardLinks.Link(Markdown.Inline("Made **WOR-0160 and** done"), Wor);
        var link = Assert.Single(spans, s => s.Link is not null);
        Assert.True(link.Bold);
        Assert.Equal("Made WOR-0160 and done", string.Concat(spans.Select(s => s.Text)));
    }

    [Fact]
    public void Prefixes_come_from_each_boards_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "darg-cardlinks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var boards = Path.Combine(root, "boards");
            BoardStore.CreateBoard(boards, "Main", ["To Do"], "WOR");
            BoardStore.CreateBoard(boards, "Art", ["To Do"], "ART");
            Directory.CreateDirectory(Path.Combine(boards, "Broken"));
            File.WriteAllText(Path.Combine(boards, "Broken", "board.json"), "{ not json");
            Assert.Equal(["ART", "WOR"], CardLinks.Prefixes([boards, null, Path.Combine(root, "missing")]).Order());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
