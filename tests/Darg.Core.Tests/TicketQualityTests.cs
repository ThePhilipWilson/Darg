using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0156: the ticket quality star beside a card's id.</summary>
public class TicketQualityTests
{
    const string Words = "When the board opens it should remember which columns were folded last time, so I don't have to fold " +
                         "the same ones again after every restart of the app.";

    static Card Parse(string body) => BoardStore.Parse(
        $"# WOR-0200: A card\n\n- **Status:** Backlog\n- **Type:** Task\n\n## Description\n\n{body}\n", "WOR-0200 - A card.md")!;

    [Fact]
    public void A_bare_ticket_is_red_with_nothing_said()
    {
        foreach (var bare in new[] { "", "fix logo aliasing", "From a note on the wall (20261117-144501-143c).\n\n![](images/a.png)" })
        {
            var s = Parse(bare).Quality;
            Assert.Equal(0, s.Stars);
            Assert.True(s.Zero);
            Assert.False(s.Red);
            Assert.Equal("black", s.Colour);
            Assert.Equal([TicketQuality.Wanted, TicketQuality.Where, TicketQuality.Done], s.Missing);
        }
    }

    [Fact]
    public void Each_of_wanted_where_and_done_when_is_a_star()
    {
        Assert.Equal(1, Parse(Words).Quality.Stars);
        Assert.True(Parse(Words).Quality.Red);
        Assert.Equal("red", Parse(Words).Quality.Colour);

        var two = Parse(Words + "\n\n**Where:** the Board section, `BoardView.axaml`.").Quality;
        Assert.Equal(2, two.Stars);
        Assert.True(two.Amber);
        Assert.Equal([TicketQuality.Done], two.Missing);

        var three = Parse(Words + "\n\n**Where:** the Board section.\n\n**Done when:** folded columns stay folded after a restart.").Quality;
        Assert.Equal(3, three.Stars);
        Assert.True(three.Green);
        Assert.Empty(three.Missing);
        Assert.Equal("★", three.Text);
        Assert.Equal("★", three.Long);
        Assert.Contains("Ticket quality 3/3", three.Tip);
    }

    [Theory]
    [InlineData("See src/Darg.Desktop/Views/BoardView.axaml for the column header.")]
    [InlineData("The fold state is kept by `ShownBoards`.")]
    [InlineData("It lives in BoardViewModel.cs.")]
    [InlineData("## Where\n\nThe board's column header.")]
    [InlineData("- Where it lives: the column header.")]
    public void Where_is_a_label_a_path_a_file_or_code(string where) =>
        Assert.Contains(TicketQuality.Where, TicketQuality.Score(where).Has);

    [Theory]
    [InlineData("Turn red/amber/green on and off as you like.")]
    [InlineData("Wherever possible it should be a decision.")]
    public void Prose_with_slashes_or_the_word_where_is_not_a_where(string text) =>
        Assert.Contains(TicketQuality.Where, TicketQuality.Score(text).Missing);

    [Theory]
    [InlineData("Done when: it stays folded.")]
    [InlineData("## Done when\n\n- it stays folded")]
    [InlineData("**Acceptance:** it stays folded.")]
    [InlineData("Steps to reproduce:\n1. fold a column\n2. restart")]
    [InlineData("**Expected:** it stays folded.")]
    [InlineData("- [ ] it stays folded")]
    public void Done_when_is_a_label_a_heading_repro_steps_or_a_checklist(string done) =>
        Assert.Contains(TicketQuality.Done, TicketQuality.Score(done).Has);

    [Fact]
    public void Where_and_done_when_sections_after_the_description_count_but_the_work_s_own_sections_do_not()
    {
        var improved = Parse(Words + "\n\n## Where\n\n- `src/Darg.Core/Board.cs`\n\n## Done when\n\n- it stays folded");
        Assert.Equal(Words, improved.Description); // the description itself stops at the first heading
        Assert.Equal(3, improved.Quality.Stars);

        // How to check, QA notes and a decision say what was done, not what the ticket asks
        var worked = Parse(Words + "\n\n## How to check\n\nOpen `src/Darg.Core/Board.cs`.\n\nDone when: never.\n\n" +
                           "## QA notes\n\n- [ ] checked\n\n## Decision answered 2026-11-18\n\n**Expected:** x");
        Assert.Equal(1, worked.Quality.Stars);
    }

    [Fact]
    public void A_change_below_the_description_is_a_change_to_the_card()
    {
        var before = Parse(Words);
        var after = Parse(Words + "\n\n## Done when\n\n- it stays folded");
        Assert.False(before.SameAs(after)); // so the board rebuilds the tile and its star moves
        Assert.True(after.SameAs(Parse(Words + "\n\n## Done when\n\n- it stays folded")));
    }

    [Fact]
    public void The_tip_says_what_is_there_and_what_Improve_would_add()
    {
        var tip = Parse(Words).Quality.Tip;
        Assert.StartsWith("Ticket quality 1/3", tip);
        Assert.Contains("Says what's wanted.", tip);
        Assert.Contains("Doesn't say where it lives, when it's done", tip);
        Assert.Contains("Improve", tip);
    }

    [Fact]
    public void The_Improve_prompt_carries_the_score_and_asks_for_the_labels_the_score_reads()
    {
        var bare = CardImprover.Prompt("Workbench", Parse("fix logo aliasing"), "x.md");
        Assert.Contains("ticket quality score is 0/3 (black)", bare);
        Assert.Contains("doesn't say what's wanted or where it lives or when it's done", bare);
        Assert.Contains("**Where:**", bare);
        Assert.Contains("**Done when:**", bare);

        var green = CardImprover.Prompt("Workbench", Parse(Words + "\n\n**Where:** `Board.cs`\n\nDone when: folded stays folded."), "x.md");
        Assert.Contains("ticket quality score is 3/3 (green)", green);
        Assert.Contains("change only what isn't, or nothing", green);
    }

    static double Lum(string hex)
    {
        double C(int i) { var v = Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * C(0) + 0.7152 * C(1) + 0.0722 * C(2);
    }

    static double Ratio(string a, string b) { var (x, y) = (Lum(a), Lum(b)); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }

    [Fact] // WOR-0181: the 0 star is black on the light themes and still 3:1 on every surface it sits on
    public void The_zero_star_is_black_and_visible_in_every_theme()
    {
        Assert.Equal("#000000", Themes.Light.Colours["Zero"]);
        Assert.Equal("#000000", Themes.Girlypop.Colours["Zero"]);
        foreach (var t in Themes.All)
            foreach (var surface in new[] { "Bg", "Panel", "Panel2" })
                Assert.True(Ratio(t.Colours["Zero"], t.Colours[surface]) >= 3, $"{t.Name}: Zero on {surface}");
    }
}
