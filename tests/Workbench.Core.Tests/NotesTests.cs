using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class CreateCardTests : IDisposable
{
    // a board inside a project's boards folder, as on disk: Create looks at the boards beside it
    readonly string _root = Directory.CreateTempSubdirectory("wb-create-").FullName;
    readonly string _dir;

    public CreateCardTests()
    {
        _dir = Directory.CreateDirectory(Path.Combine(_root, "Main")).FullName;
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

    public void Dispose() => Directory.Delete(_root, true);
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

    [Fact]
    public void Many_notes_become_tickets_oldest_first_skipping_ones_that_have_one()
    {
        var board = Directory.CreateDirectory(Path.Combine(_dir, "board")).FullName;
        File.WriteAllText(Path.Combine(board, "board.json"), """{"columns":[],"prefix":"T","nextId":1,"order":{}}""");
        var newer = NoteStore.Save(_dir, new Note("b", "newer", "Loose", "yellow", [], "2026-11-16 12:00", ""));
        var older = NoteStore.Save(_dir, new Note("a", "older", "Loose", "yellow", [], "2026-11-15 09:00", ""));
        var done = NoteStore.Save(_dir, new Note("c", "done", "Loose", "yellow", [], "2026-11-14 09:00", "T-0099"));
        var cards = NoteStore.MakeTickets(_dir, [newer, done, older], board);
        Assert.Equal(["older", "newer"], cards.Select(c => c.Title));
        Assert.Equal(["T-0001", "T-0002"], cards.Select(c => c.Id));
        var notes = NoteStore.List(_dir).ToDictionary(n => n.Id, n => n.Ticket);
        Assert.Equal("T-0001", notes["a"]);
        Assert.Equal("T-0002", notes["b"]);
        Assert.Equal("T-0099", notes["c"]);
    }

    [Fact]
    public void A_note_goes_to_the_archive_when_its_ticket_is_completed_and_back_when_it_reopens()
    {
        var boards = Directory.CreateDirectory(Path.Combine(_dir, "tasks")).FullName;
        var board = Directory.CreateDirectory(Path.Combine(boards, "Main")).FullName;
        File.WriteAllText(Path.Combine(board, "board.json"), """{"columns":[{"id":"c1","name":"In Progress"}],"prefix":"T","nextId":1,"order":{}}""");
        var notes = Directory.CreateDirectory(Path.Combine(_dir, "notes")).FullName;
        var n = NoteStore.Save(notes, new Note("n1", "ship it", "Loose", "yellow", [], "", ""));
        var (_, card) = NoteStore.MakeTicket(notes, n, board);
        NoteStore.Save(notes, new Note("n2", "lost ticket", "Loose", "yellow", [], "", "T-0099"));
        NoteStore.Save(notes, new Note("n3", "no ticket", "Loose", "yellow", [], "", ""));

        Assert.Equal((0, 0), NoteStore.SyncArchive(notes, NoteStore.TicketStatuses(boards)));
        Assert.Equal("Backlog", NoteStore.TicketStatuses(boards)[card.Id]);

        BoardStore.Move(board, card.Id, Board.Completed);
        Assert.Equal((1, 0), NoteStore.SyncArchive(notes, NoteStore.TicketStatuses(boards)));
        Assert.Equal(["n2", "n3"], NoteStore.List(notes).Select(x => x.Id).Order());
        Assert.Equal("n1", NoteStore.Archived(notes).Single().Id);

        BoardStore.Move(board, card.Id, "c1");
        Assert.Equal((0, 1), NoteStore.SyncArchive(notes, NoteStore.TicketStatuses(boards)));
        Assert.Empty(NoteStore.Archived(notes));
        Assert.Equal(3, NoteStore.List(notes).Count);
    }

    [Theory] // a board that ends in its own Done column never uses Completed
    [InlineData("Completed", true)] [InlineData("completed", true)] [InlineData("Done", true)] [InlineData(" done ", true)]
    [InlineData("In Progress", false)] [InlineData("", false)] [InlineData("Backlog", false)] [InlineData("Done-ish", false)]
    public void Completed_means_the_completed_list_or_a_done_column(string status, bool done) =>
        Assert.Equal(done, NoteStore.IsCompleted(status));

    public void Dispose() => Directory.Delete(_dir, true);
}
