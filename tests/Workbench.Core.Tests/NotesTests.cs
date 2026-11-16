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

    [Fact]
    public void A_card_is_a_task_unless_made_a_bug_and_its_type_can_change()
    {
        var c = BoardStore.Create(_dir, "Plain");
        Assert.Equal(("Task", false), (c.Kind, c.IsBug));
        var b = BoardStore.Create(_dir, "Broken", type: CardTypes.Bug);
        Assert.Contains("- **Type:** Bug\n", File.ReadAllText(Path.Combine(_dir, "items", b.File)));
        Assert.True(b.IsBug);
        var u = BoardStore.Update(_dir, c.Id, type: "Bug");
        Assert.True(u.IsBug);
        Assert.Contains("- **Type:** Bug\n", File.ReadAllText(Path.Combine(_dir, "items", u.File)));
        Assert.Equal("Task", BoardStore.Update(_dir, c.Id, type: "").Kind); // blank means Task
    }

    [Fact]
    public void A_card_without_a_type_line_reads_as_a_task_and_gets_one_when_set()
    {
        var old = BoardStore.ReadCards(_dir).Single(x => x.Id == "VOT-0007");
        Assert.Equal("Task", old.Kind);
        BoardStore.Update(_dir, "VOT-0007", type: "Bug");
        Assert.Equal("# VOT-0007: Old\n\n- **Status:** Backlog\n- **Type:** Bug\n", File.ReadAllText(Path.Combine(_dir, "items", "VOT-0007 - Old.md")));
    }

    [Theory] // words people write when the thing is a bug, and ones that only mention one
    [InlineData("Bug forgetting an agent doesn't refresh the board", null, "Bug")]
    [InlineData("bug: drag drops on the wrong column", null, "Bug")]
    [InlineData("[BUG] crash on open", null, "Bug")]
    [InlineData("Bugs in the wall archive", null, "Bug")]
    [InlineData("- bug - toasts open the wrong agent", null, "Bug")]
    [InlineData("Crash on open", "bug", "Bug")]
    [InlineData("Crash on open", "type: bug", "Bug")]
    [InlineData("Crash on open", "Type/Bug", "Bug")]
    [InlineData("ticket types, task and bug for now", null, "Task")]
    [InlineData("Debugger attach button", null, "Task")]
    [InlineData("bugbear art pass", null, "Task")]
    [InlineData("Crash on open", "debugging", "Task")]
    public void Guesses_bug_from_the_words_or_labels(string text, string? label, string expected) =>
        Assert.Equal(expected, CardTypes.Guess(text, label is null ? null : [label]));

    [Fact]
    public void A_note_that_opens_with_bug_becomes_a_bug_card()
    {
        var notes = Directory.CreateDirectory(Path.Combine(_root, "notes")).FullName;
        var n = NoteStore.Save(notes, new Note(NoteStore.NewId(), "Bug: the wall forgets its scroll", "Loose", "yellow", [], "", ""));
        Assert.True(NoteStore.MakeTicket(notes, n, _dir).Card.IsBug);
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
    public void Organise_prompt_lists_the_loose_notes_columns_and_tags_but_not_ticketed_ones()
    {
        var a = NoteStore.Save(_dir, new Note("a1", "Fettle should blink\nmore often", "Loose", "yellow", ["ui"], "", ""));
        var b = NoteStore.Save(_dir, new Note("b2", "Done already", "Loose", "pink", ["ops"], "", "WOR-0001"));
        var prompt = NoteStore.OrganisePrompt(_dir, [a, b], ["Loose", "Parked"])!;
        Assert.Contains("1 post-it in", prompt);
        Assert.Contains("\"Loose\", \"Parked\"", prompt);
        Assert.Contains("Tags already in use: ops, ui.", prompt);
        Assert.Contains("- a1.md [Loose; yellow; ui]: Fettle should blink / more often", prompt);
        Assert.DoesNotContain("b2.md", prompt);
        Assert.Null(NoteStore.OrganisePrompt(_dir, [b], ["Loose"]));
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

public sealed class WallColumnTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-wallcols-").FullName;

    Note Put(string id, string column, string ticket = "") => NoteStore.Save(_dir, new Note(id, "text " + id, column, "yellow", [], "2026-11-16 10:00", ticket));

    [Fact]
    public void Adding_starts_from_the_default_columns_and_numbers_new_ones()
    {
        Assert.Equal("New column", NoteStore.AddColumn(_dir));
        Assert.Equal("New column 2", NoteStore.AddColumn(_dir));
        Assert.Equal([.. NoteStore.DefaultColumns, "New column", "New column 2"], NoteStore.Columns(_dir));
    }

    [Fact]
    public void Renaming_moves_its_notes_with_it_archived_ones_too_and_keeps_other_wall_settings()
    {
        File.WriteAllText(Path.Combine(_dir, "wall.json"), """{"columns":["Loose","Ideas"],"other":1}""");
        Put("a", "Ideas"); Put("b", "Loose");
        NoteStore.Save(NoteStore.ArchiveFolder(_dir), new Note("c", "old", "Ideas", "yellow", [], "2026-11-16 10:00", "WOR-0001"));
        NoteStore.RenameColumn(_dir, "Ideas", "  Someday   maybe ");
        Assert.Equal(["Loose", "Someday maybe"], NoteStore.Columns(_dir));
        Assert.Equal("Someday maybe", NoteStore.List(_dir).Single(n => n.Id == "a").Column);
        Assert.Equal("Loose", NoteStore.List(_dir).Single(n => n.Id == "b").Column);
        Assert.Equal("Someday maybe", NoteStore.Archived(_dir).Single().Column);
        Assert.Contains("\"other\": 1", File.ReadAllText(Path.Combine(_dir, "wall.json")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ticketed")]
    [InlineData("archive")]
    [InlineData("parked")] // another column's name, in any case
    public void Bad_names_are_refused(string name) =>
        Assert.Throws<InvalidOperationException>(() => NoteStore.RenameColumn(_dir, "Loose", name));

    [Fact]
    public void A_case_only_rename_is_allowed()
    {
        NoteStore.RenameColumn(_dir, "Loose", "loose");
        Assert.Equal("loose", NoteStore.Columns(_dir)[0]);
    }

    [Fact]
    public void A_column_only_notes_name_joins_the_wall_when_renamed()
    {
        Put("a", "Stray");
        NoteStore.RenameColumn(_dir, "Stray", "Kept");
        Assert.Equal([.. NoteStore.DefaultColumns, "Kept"], NoteStore.Columns(_dir));
        Assert.Equal("Kept", NoteStore.List(_dir).Single().Column);
    }

    [Fact]
    public void Removing_sends_its_loose_notes_to_the_first_column_and_the_last_one_stays()
    {
        Put("a", "Thinking about"); Put("b", "Thinking about", "WOR-0002"); Put("c", "Loose");
        Assert.Equal(1, NoteStore.RemoveColumn(_dir, "Thinking about")); // the ticketed one moves but isn't counted: it shows in Ticketed
        Assert.Equal(["Loose", "Parked"], NoteStore.Columns(_dir));
        Assert.All(NoteStore.List(_dir), n => Assert.Equal("Loose", n.Column));
        NoteStore.RemoveColumn(_dir, "Loose");
        Assert.Equal("Parked", NoteStore.List(_dir).Single(n => n.Id == "c").Column);
        Assert.Throws<InvalidOperationException>(() => NoteStore.RemoveColumn(_dir, "Parked"));
    }

    [Fact]
    public void Shifting_swaps_with_a_neighbour_and_stops_at_the_ends()
    {
        NoteStore.ShiftColumn(_dir, "Parked", -1);
        Assert.Equal(["Loose", "Parked", "Thinking about"], NoteStore.Columns(_dir));
        NoteStore.ShiftColumn(_dir, "Loose", -1);
        NoteStore.ShiftColumn(_dir, "Thinking about", 1);
        Assert.Equal(["Loose", "Parked", "Thinking about"], NoteStore.Columns(_dir));
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
