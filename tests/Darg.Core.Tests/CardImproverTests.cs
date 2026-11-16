using Darg.Core;

namespace Darg.Core.Tests;

public class CardImproverTests
{
    static Card Card(string description = "") =>
        new("WOR-0073", "improve ticket button", "Backlog", "", "Task", [], "WOR-0073 - improve ticket button.md", description);

    [Fact]
    public void Finds_the_note_a_card_came_from_on_the_wall_or_in_the_archive()
    {
        var notes = Path.Combine(Path.GetTempPath(), "darg-improve-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(NoteStore.ArchiveFolder(notes));
        try
        {
            File.WriteAllText(Path.Combine(notes, "20261116-225908-6088.md"), "improve ticket button");
            File.WriteAllText(Path.Combine(NoteStore.ArchiveFolder(notes), "20261101-120000-abcd.md"), "old");

            Assert.Equal(Path.Combine(notes, "20261116-225908-6088.md"),
                CardImprover.NoteFile(notes, "From a note on the wall (20261116-225908-6088)."));
            Assert.Equal(Path.Combine(NoteStore.ArchiveFolder(notes), "20261101-120000-abcd.md"),
                CardImprover.NoteFile(notes, "Some words.\n\nFrom a note on the wall (20261101-120000-abcd).\n\n![](images/x.png)"));
            Assert.Null(CardImprover.NoteFile(notes, "From a note on the wall (20260101-000000-gone)."));
            Assert.Null(CardImprover.NoteFile(notes, "Make the button blue."));
            Assert.Null(CardImprover.NoteFile(null, "From a note on the wall (20261116-225908-6088)."));
        }
        finally { Directory.Delete(notes, recursive: true); }
    }

    [Fact]
    public void The_prompt_asks_before_guessing_and_leaves_the_work_alone()
    {
        var p = CardImprover.Prompt("Workbench", Card(), "D:/boards/Main/items/WOR-0073 - improve ticket button.md", "D:/notes/n.md");
        Assert.Contains("WOR-0073: improve ticket button", p);
        Assert.Contains("Card file: D:/boards/Main/items/WOR-0073 - improve ticket button.md", p);
        Assert.Contains("this note on the wall: D:/notes/n.md", p);
        Assert.Contains("Don't do the work it asks for", p);
        Assert.Contains("AskUserQuestion", p);
        Assert.Contains("Done when", p);
        Assert.Contains("Leave Status", p);
        Assert.Contains("Don't commit or push.", p);
    }

    [Fact]
    public void The_prompt_carries_the_commit_rule_and_skips_a_missing_note()
    {
        var p = CardImprover.Prompt("Alpha", Card(), "x.md", commits: "In D:/vault, commit your work without asking, but never push.");
        Assert.Contains("In D:/vault, commit your work without asking, but never push.", p);
        Assert.DoesNotContain("Don't commit", p);
        Assert.DoesNotContain("note on the wall:", p);
    }
}
