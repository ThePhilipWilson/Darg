using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class CreateCardTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-create-").FullName;

    public CreateCardTests()
    {
        File.WriteAllText(Path.Combine(_dir, "board.json"), """{"columns":[{"id":"c1","name":"To Do"}],"prefix":"VOT","nextId":3,"order":{"backlog":["VOT-0001"]}}""");
        Directory.CreateDirectory(Path.Combine(_dir, "items"));
        File.WriteAllText(Path.Combine(_dir, "items", "VOT-0007 - Old.md"), "# VOT-0007: Old\n\n- **Status:** Backlog\n");
    }

    [Fact]
    public void Numbers_past_every_id_in_use_and_lands_last_in_the_backlog()
    {
        var c = BoardStore.Create(_dir, "Pause: menu?", "Esc opens it.", ["ui", "UI ", "input"]);
        Assert.Equal("VOT-0008", c.Id);
        Assert.Equal("VOT-0008 - Pause menu.md", c.File);
        Assert.Equal(["ui", "input"], c.Labels);
        var b = BoardStore.Load(_dir);
        Assert.Equal("Esc opens it.", b.Lists[Board.Backlog].Single(x => x.Id == "VOT-0008").Description);
        var json = File.ReadAllText(Path.Combine(_dir, "board.json"));
        Assert.Contains("\"nextId\": 9", json);
        Assert.Matches("\"VOT-0001\",\\s*\"VOT-0008\"", json); // appended to the backlog's order
    }

    [Theory] // as Tatty names files (real examples from the Alpha board)
    [InlineData("Outpaint Cat uncommons/rares; holo masks for apex, cream-dream", "Outpaint Cat uncommons rares; holo masks for apex, cream-dre")]
    [InlineData("Pause: menu?", "Pause menu")]
    [InlineData("Draw Bimbo's final figure (decide flatter, inked style)", "Draw Bimbo's final figure (decide flatter, inked style)")]
    public void File_titles_follow_tattys_rule(string title, string expected) => Assert.Equal(expected, BoardStore.FileTitle(title));

    [Fact]
    public void A_blank_title_is_refused() => Assert.Throws<ArgumentException>(() => BoardStore.Create(_dir, "  \n "));

    [Fact]
    public void Update_changes_only_what_it_is_given_and_keeps_other_sections()
    {
        var c = BoardStore.Create(_dir, "First go", "Old text.", ["a"]);
        var path = Path.Combine(_dir, "items", c.File);
        File.AppendAllText(path, "\n## Notes\n\nKeep me.\n");
        var u = BoardStore.Update(_dir, c.Id, title: "Second go", description: "New text.\n\nTwo paragraphs.", priority: "High");
        Assert.Equal("VOT-0008 - Second go.md", u.File);
        Assert.False(File.Exists(path));
        var text = File.ReadAllText(Path.Combine(_dir, "items", u.File));
        Assert.Contains("# VOT-0008: Second go\n", text);
        Assert.Contains("- **Priority:** High\n", text);
        Assert.Contains("- **Labels:** a\n", text);
        Assert.Contains("## Description\n\nNew text.\n\nTwo paragraphs.\n\n## Notes\n\nKeep me.\n", text);
        Assert.Equal("New text.\n\nTwo paragraphs.", u.Description);
    }

    public void Dispose() => Directory.Delete(_dir, true);
}

public sealed class NoteStoreTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-notes-").FullName;

    [Fact]
    public void Saves_and_reads_back_with_obsidian_properties()
    {
        var n = NoteStore.Save(_dir, new Note(NoteStore.NewId(), "Homewrecker as its own deity\nmirror of Rivals", "Thinking about", "pink", ["ideas", "design"], "", ""));
        var text = File.ReadAllText(Path.Combine(_dir, n.Id + ".md"));
        Assert.StartsWith("---\ncolumn: Thinking about\ncolour: pink\ntags: [ideas, design]\n", text);
        var back = Assert.Single(NoteStore.List(_dir));
        Assert.Equal((n.Id, n.Text, n.Column, n.Colour, n.Created), (back.Id, back.Text, back.Column, back.Colour, back.Created));
        Assert.Equal(["ideas", "design"], back.Tags);
        Assert.Equal("Homewrecker as its own deity", back.FirstLine);
    }

    [Fact]
    public void A_note_without_properties_is_a_loose_yellow_note()
    {
        File.WriteAllText(Path.Combine(_dir, "hand.md"), "Written in Obsidian\n");
        var n = Assert.Single(NoteStore.List(_dir));
        Assert.Equal(("Loose", "yellow", "Written in Obsidian"), (n.Column, n.Colour, n.Text));
    }

    [Fact]
    public void Delete_moves_the_note_aside()
    {
        var n = NoteStore.Save(_dir, new Note("x1", "gone", "Loose", "yellow", [], "", ""));
        NoteStore.Delete(_dir, n.Id);
        Assert.Empty(NoteStore.List(_dir));
        Assert.True(File.Exists(Path.Combine(_dir, ".deleted", "x1.md")));
    }

    [Fact]
    public void Make_it_a_ticket_creates_the_card_and_marks_the_note()
    {
        var board = Directory.CreateDirectory(Path.Combine(_dir, "board")).FullName;
        File.WriteAllText(Path.Combine(board, "board.json"), """{"columns":[],"prefix":"T","nextId":1,"order":{}}""");
        var n = NoteStore.Save(_dir, new Note("n1", "Cow: the pail\nfills across the run", "Loose", "green", ["design"], "", ""));
        var (note, card) = NoteStore.MakeTicket(_dir, n, board);
        Assert.Equal("T-0001", card.Id);
        Assert.Equal("Cow: the pail", card.Title);
        Assert.StartsWith("fills across the run", card.Description);
        Assert.Equal(["design"], card.Labels);
        Assert.Equal("T-0001", note.Ticket);
        Assert.Equal("T-0001", NoteStore.List(_dir).Single(x => x.Id == "n1").Ticket);
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
