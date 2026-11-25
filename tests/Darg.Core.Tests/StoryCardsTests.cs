using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0217: reading which cards a story holds, from its Cards: line (in the shapes agents write it), a ## Cards
/// section, and the Story: lines of the cards it wraps.</summary>
public sealed class StoryCardsTests
{
    static Card C(string id, string description = "", string type = "Task", string sections = "") =>
        new(id, "t " + id, "Done", "", type, [], $"{id} - t.md", description) { Sections = sections };

    static BoardCard On(Card c, string column = "Done") => new(c, "Main", column, c.File, false, false, column == "Done");

    [Theory]
    [InlineData("Cards: WOR-0100, WOR-0101")]
    [InlineData("cards: wor-0100 wor-0101")]
    [InlineData("- **Cards:** WOR-0100; WOR-0101")]
    [InlineData("**Cards**: [WOR-0100](card:WOR-0100), [WOR-0101](card:WOR-0101)")]
    [InlineData("Some words.\n\nCards: WOR-0100, WOR-0101, WOR-0300")]
    public void The_Cards_line_is_read_in_the_shapes_agents_write(string description) =>
        Assert.Equal(["WOR-0100", "WOR-0101"], StoryCards.Named(C("WOR-0300", description, "Story")));

    [Fact]
    public void A_Cards_section_adds_the_ids_its_items_start_with()
    {
        var story = C("WOR-0300", "Cards: WOR-0100", "Story",
            "## Cards\n\n- WOR-0101: themes, built on WOR-0050 (abc123)\n- **WOR-0100**: logo\n1. [WOR-0102](card:WOR-0102) fonts\n\n## Left\n\n- WOR-0400 still open");
        Assert.Equal(["WOR-0100", "WOR-0101", "WOR-0102"], StoryCards.Named(story));
    }

    [Fact]
    public void Words_that_only_mention_cards_name_none()
    {
        Assert.Empty(StoryCards.Named(C("WOR-0300", "The cards in this story were about WOR-0100.", "Story")));
        Assert.Null(StoryCards.StoryOf(C("WOR-0100", "Story: as a user I want a logo")));
    }

    [Fact]
    public void Named_cards_come_first_then_ones_that_name_the_story_and_a_missing_one_is_kept()
    {
        var story = C("WOR-0300", "Cards: WOR-0101, ZZ-0001", "Story");
        var named = C("WOR-0101", "Story: WOR-0300 Look and feel");
        var tagged = C("WOR-0102", "Done.\n\n- **Story:** [WOR-0300](card:WOR-0300) Look and feel");
        var other = C("WOR-0103", "Story: WOR-0999 Another");
        var members = StoryCards.Of(story, [On(story), On(named), On(tagged), On(other)]);
        Assert.Equal(["WOR-0101", "ZZ-0001", "WOR-0102"], members.Select(m => m.Id));
        Assert.True(members[0].Found);
        Assert.False(members[1].Found);
        Assert.Equal("ZZ-0001: no board of this project has it", members[1].Text);
        Assert.Equal("WOR-0102: t WOR-0102 (Done on Main)", members[2].Text);
    }
}
