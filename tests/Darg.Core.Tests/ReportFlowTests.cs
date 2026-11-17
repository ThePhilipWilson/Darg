using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0102 end to end, the way the app does it: a card is moved on a real board folder (its Status line and
/// board.json, as a drag, an agent or a QA check does), the board is read again, <see cref="ColumnWatch"/> says who
/// entered which column and <see cref="ReportFlow"/> writes or archives the reports.</summary>
public sealed class ReportFlowTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("darg-flow-").FullName;
    readonly string _board, _reports;
    readonly ColumnWatch _watch = new();
    DateTime _now = new(2026, 11, 17, 12, 0, 0);

    public ReportFlowTests()
    {
        _board = Directory.CreateDirectory(Path.Combine(_root, "Main")).FullName;
        _reports = Path.Combine(_root, "reports");
        File.WriteAllText(Path.Combine(_board, "board.json"), """
            {"columns":[{"id":"c2","name":"In Progress"},{"id":"c4","name":"QA Ready"},{"id":"c5","name":"For Human Attention"},
                        {"id":"c6","name":"needs more work"},{"id":"c3","name":"Done"}],
             "prefix":"WOR","nextId":4,"order":{"c2":["WOR-0001","WOR-0002"],"c4":["WOR-0003"]}}
            """);
        Directory.CreateDirectory(Path.Combine(_board, "items"));
        Card(1, "Make the thing", "In Progress");
        Card(2, "Make another", "In Progress");
        Card(3, "Already in QA", "QA Ready");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    void Card(int n, string title, string status) =>
        File.WriteAllText(Path.Combine(_board, "items", $"WOR-{n:0000} - {title}.md"),
            $"# WOR-{n:0000}: {title}\n\n- **Status:** {status}\n- **Priority:**\n- **Type:** Task\n- **Labels:**\n- **Created:** 2026-11-17\n- **Done:**\n\n" +
            $"## Description\n\nAbout {title}.\n\n## How to check\n\nOpen it and look.\n");

    string FileOf(Card c) => Path.Combine(_board, "items", c.File);

    /// <summary>Read the board again, as the app does when its files change, and act on what moved, with
    /// <paramref name="agent"/> the purpose of an agent at work on the card (null: none).</summary>
    ColumnWatch.Moves Look(string? agent = null)
    {
        var moves = _watch.Read(_board, BoardStore.Load(_board));
        foreach (var c in moves.Attention) ReportFlow.EnteredAttention(_reports, c, FileOf(c), "", _now);
        foreach (var c in moves.Qa) ReportFlow.EnteredQa(_reports, c, FileOf(c), "", _now, agent);
        foreach (var c in moves.Done) ReportFlow.EnteredDone(_reports, c, FileOf(c), "", _now, agent);
        return moves;
    }

    List<AgentReport> Listed(string card) => ReportStore.List(_reports).Where(r => r.Card == card).ToList();
    string[] Archived() => Directory.Exists(Path.Combine(_reports, "archive")) ? Directory.GetFiles(Path.Combine(_reports, "archive")) : [];

    [Fact]
    public void Into_qa_writes_an_unread_qa_report_and_on_to_done_archives_it_and_writes_a_done_report()
    {
        Look();
        BoardStore.Move(_board, "WOR-0001", "c4");
        Assert.Equal(["WOR-0001"], Look().Qa.Select(c => c.Id));
        var qa = Assert.Single(Listed("WOR-0001"));
        Assert.True(qa.Qa);
        Assert.False(qa.Read);
        Assert.Equal(DecisionReport.Pending, qa.Decision);

        _now = _now.AddMinutes(20);
        BoardStore.Move(_board, "WOR-0001", "c3");
        Assert.Equal(["WOR-0001"], Look().Done.Select(c => c.Id));
        var done = Assert.Single(Listed("WOR-0001"));                  // the QA report has left the list...
        Assert.False(done.Qa);
        Assert.Equal(ReportKind.Finished, done.Kind);
        Assert.False(done.Read);
        var kept = Assert.Single(Archived());                            // ...and is kept in reports/archive, settled
        Assert.Equal(Path.GetFileName(qa.File), Path.GetFileName(kept));
        Assert.Equal(DecisionReport.Approved, ReportStore.Parse(File.ReadAllText(kept), kept).Decision);

        var again = Look();                                              // reading again changes nothing
        Assert.Empty(again.Qa); Assert.Empty(again.Done); Assert.Empty(again.Attention);
        Assert.Single(Listed("WOR-0001"));
    }

    [Fact]
    public void Moves_made_while_the_app_was_closed_make_nothing()
    {
        BoardStore.Move(_board, "WOR-0001", "c3");          // moved before the app's first look
        var first = Look();
        Assert.Empty(first.Qa); Assert.Empty(first.Done); Assert.Empty(first.Attention);
        Assert.False(Directory.Exists(_reports));
        Assert.Empty(Look().Qa);                             // WOR-0003 sat in QA all along: not a move
    }

    [Fact]
    public void A_card_new_to_the_board_in_qa_or_done_is_not_a_move_but_one_put_straight_into_attention_is()
    {
        Look();
        Card(7, "Born in QA", "QA Ready");
        Card(8, "Born done", "Done");
        Card(9, "Born needing you", "For Human Attention");
        var moves = Look();
        Assert.Empty(moves.Qa); Assert.Empty(moves.Done);
        Assert.Equal(["WOR-0009"], moves.Attention.Select(c => c.Id));
        Assert.Single(Listed("WOR-0009"));
    }

    [Fact]
    public void A_card_agent_at_work_writes_its_own_reports_so_darg_writes_neither()
    {
        Look();
        BoardStore.Move(_board, "WOR-0001", "c4");
        Look(agent: AgentPurposes.Work);
        Assert.Empty(Listed("WOR-0001"));                    // its agent writes the QA report as its last step

        // an earlier QA report is still archived when it reaches done, but the done report is the agent's to write
        Directory.CreateDirectory(_reports);
        File.WriteAllText(Path.Combine(_reports, "20261117-1100-WOR-0001-qa.md"),
            "---\nstatus: qa\ncard: WOR-0001\ntitle: Make the thing\nagent: a1\ndate: 2026-11-17 11:00\nread: false\n---\n\n## Changed\n\nX.\n");
        BoardStore.Move(_board, "WOR-0001", "c3");
        Look(agent: AgentPurposes.Rework);
        Assert.Empty(Listed("WOR-0001"));
        Assert.Single(Archived());
        foreach (var purpose in new[] { AgentPurposes.Work, AgentPurposes.Flow, AgentPurposes.Improve, "" }) // "" = saved before purposes
        {
            BoardStore.Move(_board, "WOR-0002", "c3");
            Look(agent: purpose);
            Assert.Empty(Listed("WOR-0002"));
            BoardStore.Move(_board, "WOR-0002", "c2");
            Look();
        }
    }

    [Fact]
    public void A_qa_agent_writes_no_report_of_its_own_so_darg_writes_the_done_report_when_it_passes_the_card()
    {
        Look();
        BoardStore.Move(_board, "WOR-0003", "c3");           // a Run QA agent, still at work, moves its card to Done
        Look(agent: AgentPurposes.Qa);
        var done = Assert.Single(Listed("WOR-0003"));
        Assert.Equal(ReportKind.Finished, done.Kind);
    }

    [Fact]
    public void No_second_qa_report_when_the_agent_wrote_one_in_the_last_two_minutes_or_darg_did()
    {
        Look();
        // the agent's own QA report, written as its last step, just before its turn ended and the board was read
        Directory.CreateDirectory(_reports);
        File.WriteAllText(Path.Combine(_reports, $"{_now.AddMinutes(-1):yyyyMMdd-HHmm}-WOR-0001-qa.md"),
            $"---\nstatus: qa\ncard: WOR-0001\ntitle: Make the thing\nagent: a1\ndate: {_now.AddMinutes(-1):yyyy-MM-dd HH:mm}\nread: false\n---\n\n## Changed\n\nX.\n");
        BoardStore.Move(_board, "WOR-0001", "c4");
        Look();
        Assert.Single(Listed("WOR-0001"));

        // Darg's own: out of QA and straight back in
        BoardStore.Move(_board, "WOR-0002", "c4");
        Look();
        BoardStore.Move(_board, "WOR-0002", "c6");
        Look();
        BoardStore.Move(_board, "WOR-0002", "c4");
        Look();
        Assert.Single(Listed("WOR-0002"));
    }

    [Fact]
    public void Completed_counts_as_done_and_attention_gets_its_decision_report()
    {
        Look();
        BoardStore.Move(_board, "WOR-0001", "c5");
        Assert.Equal(["WOR-0001"], Look().Attention.Select(c => c.Id));
        Assert.Single(Listed("WOR-0001"));
        _now = _now.AddMinutes(20);
        BoardStore.Move(_board, "WOR-0001", Board.Completed);
        Assert.Equal(["WOR-0001"], Look().Done.Select(c => c.Id));
        Assert.Equal(ReportKind.Finished, Assert.Single(Listed("WOR-0001")).Kind);
        Assert.Single(Archived());
    }
}
