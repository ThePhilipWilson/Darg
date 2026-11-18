using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0140: the cards on a project's boards, read for the API's cards and card commands.</summary>
public sealed class CardIndexTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "darg-cardindex-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _boards;

    public CardIndexTests() => _boards = Directory.CreateDirectory(Path.Combine(_root, "tasks")).FullName;
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void Reads_every_board_and_knows_each_boards_qa_attention_and_done_lists()
    {
        var main = BoardStore.CreateBoard(_boards, "Main", ["To Do", "QA Ready", BoardStore.AttentionName, "Done"], "T");
        var story = BoardStore.CreateBoard(_boards, "Logo", ["Doing", "Review", "Shipped"], "L");
        var qa = BoardStore.Create(main, "check the header", labels: ["fettle"]);
        BoardStore.Move(main, qa.Id, BoardStore.Load(main).Columns.Single(c => c.Name == "QA Ready").Id);
        var done = BoardStore.Create(main, "all finished");
        BoardStore.Move(main, done.Id, Board.Completed);
        var review = BoardStore.Create(story, "pink or teal");
        BoardStore.Move(story, review.Id, BoardStore.Load(story).Columns.Single(c => c.Name == "Review").Id);
        var shipped = BoardStore.Create(story, "the new logo");
        BoardStore.Move(story, shipped.Id, BoardStore.Load(story).Columns.Single(c => c.Name == "Shipped").Id);
        var waiting = BoardStore.Create(main, "not yet");

        var all = CardIndex.Read(_boards);
        Assert.Equal(5, all.Count);
        Assert.Equal([review.Id, qa.Id], CardIndex.Filter(all, "qa", null, null).Select(c => c.Card.Id)); // boards in name order
        Assert.Equal([shipped.Id, done.Id], CardIndex.Filter(all, "done", null, null).Select(c => c.Card.Id));
        Assert.Equal([review.Id], CardIndex.Filter(all, "review", null, null).Select(c => c.Card.Id));
        Assert.Equal([qa.Id], CardIndex.Filter(all, null, "FETTLE", null).Select(c => c.Card.Id));
        Assert.Equal([shipped.Id], CardIndex.Filter(all, null, null, "new logo").Select(c => c.Card.Id));
        Assert.Equal("Backlog", all.Single(c => c.Card.Id == waiting.Id).Column);
        Assert.Equal("Logo", CardIndex.Find(_boards, review.Id.ToLowerInvariant())!.Board);
        Assert.True(File.Exists(CardIndex.Find(_boards, qa.Id)!.File));
    }

    [Fact]
    public void QA_notes_are_the_section_alone_and_reports_include_the_archived_ones()
    {
        var text = "# T-1: x\n\n## Description\n\nIt.\n\n## QA notes\n\n### 2026-11-18 09:00\n\ntoo loud\n\n## How to check\n\nLook.\n";
        Assert.Equal("### 2026-11-18 09:00\n\ntoo loud", CardIndex.QaNotesOf(text));
        Assert.Equal("", CardIndex.QaNotesOf("# T-1: x\n\n## Description\n\nIt.\n"));

        var reports = Directory.CreateDirectory(Path.Combine(_root, "reports")).FullName;
        File.WriteAllText(Path.Combine(reports, "20261118-0900-T-1.md"), "---\ncard: T-1\ndate: 2026-11-18 09:00\n---\n");
        File.WriteAllText(Path.Combine(reports, "20261118-0900-T-2.md"), "---\ncard: T-2\ndate: 2026-11-18 09:00\n---\n");
        Directory.CreateDirectory(Path.Combine(reports, "archive", "Logo"));
        File.WriteAllText(Path.Combine(reports, "archive", "20261117-0900-T-1-qa.md"), "---\nstatus: qa\ncard: T-1\ndate: 2026-11-17 09:00\n---\n");
        File.WriteAllText(Path.Combine(reports, "archive", "Logo", "20261116-0900-T-1.md"), "---\ncard: t-1\ndate: 2026-11-16 09:00\n---\n");
        Assert.Equal(["20261118-0900-T-1", "20261117-0900-T-1-qa", "20261116-0900-T-1"], CardIndex.ReportsFor(reports, "T-1").Select(r => r.Id));
    }
}
