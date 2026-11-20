using Darg.Core;
using Xunit;

public class FettleRulesTests
{
    [Fact]
    public void ADescribedChangeIsATicketUnlessToldToDoItNow()
    {
        var r = FettleRules.ChangeRequests;
        Assert.Contains("make a card", r);
        Assert.Contains("do not edit code", r);
        Assert.Contains("just do it", r);
        Assert.Contains("AskUserQuestion", r);
        Assert.Contains("New ticket", r);
        Assert.Contains("Do it now", r);
    }

    [Fact]
    public void IsSafeOnACommandLine()
    {
        Assert.DoesNotContain(FettleRules.ChangeRequests, c => c is '<' or '>' or '"' or '&' or '|');
        Assert.DoesNotContain(FettleRules.SayWhatYouDo, c => c is '<' or '>' or '"' or '&' or '|');
    }

    [Fact] // WOR-0157: a line first saying what was understood, a line last saying what was made, never silence
    public void A_turn_that_does_something_says_so_first_and_last()
    {
        var r = FettleRules.SayWhatYouDo;
        Assert.Contains("before the first tool call", r);
        Assert.Contains("naming any card you made by its ID", r);
        Assert.Contains("If you could not do it, say so", r);
        Assert.Contains("without a reply", r);
    }
}

/// <summary>Real Haiku turns with the change-request rule in the briefing (WOR-0132). Opt in with DARG_LIVE=1.
/// Each runs in a throwaway folder holding a board and a source file, so a wrong edit touches nothing real.</summary>
public sealed class LiveFettleRulesTests
{
    const string Intro =
        "You are Fettle, the assistant inside Darg, Philip's app for his projects. Keep replies short. " +
        "Each message starts with [Philip is looking at: ...]. Never commit or push. ";
    const string Boards =
        "Boards: each board is a folder with board.json (columns, prefix, nextId, order {list id: [card ids]}) and items/ID - title.md. " +
        "A card file starts with a heading line of the ID and title, then a Status line, a Type line, then a Description section. " +
        "To add a card: next ID is past nextId, bump nextId, add the ID to order.backlog, name the file ID - title.md. ";

    const string SourceText = """
        class SendBack
        {
            public string Note = "";
            public void OnSendBackClick() { /* sends the card back with Note */ }
            public void OnNoteKeyDown(string key) { }
        }
        """;

    /// <summary>Runs one turn; returns what was written or edited (tool, path) and whether a question was asked.</summary>
    static async Task<(string Dir, List<string> Writes, bool Asked)> Turn(string message)
    {
        var dir = Directory.CreateTempSubdirectory("wb-fettle-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "board", "items"));
        File.WriteAllText(Path.Combine(dir, "board", "board.json"),
            """{"prefix":"T","nextId":2,"columns":[{"id":"backlog","name":"Backlog"},{"id":"done","name":"Completed"}],"order":{"backlog":["T-0001"],"done":[]}}""");
        File.WriteAllText(Path.Combine(dir, "board", "items", "T-0001 - Existing.md"),
            "# T-0001: Existing\n\n- **Status:** Backlog\n- **Type:** Task\n\n## Description\n\nx\n");
        File.WriteAllText(Path.Combine(dir, "SendBack.cs"), SourceText);
        var writes = new List<string>();
        var asked = false;
        await using var s = AgentSession.Start(dir, "haiku",
            ["--append-system-prompt", Intro + FettleRules.ChangeRequests + " " + Decisions.Briefing + " " + Boards +
                $"The project Demo has its board in {Path.Combine(dir, "board")} and its repo in {dir}."]);
        var done = new TaskCompletionSource<TurnFinished>();
        s.Event += e =>
        {
            switch (e)
            {
                case PermissionAsked { ToolName: "AskUserQuestion" } q:
                    asked = true;
                    var qn = (string)q.Input["questions"]![0]!["question"]!; var first = (string)q.Input["questions"]![0]!["options"]![0]!["label"]!;
                    s.Answer(q.RequestId, q.Input, new Dictionary<string, string> { [qn] = first });
                    break;
                case PermissionAsked p:
                    if (p.ToolName is "Edit" or "Write" or "MultiEdit")
                        writes.Add(p.ToolName + " " + (p.Input["file_path"]?.ToString() ?? ""));
                    s.Allow(p.RequestId, p.Input);
                    break;
                case TurnFinished f: Console.Error.WriteLine("REPLY: " + f.Text); done.TrySetResult(f); break;
                case SessionExited x: done.TrySetException(new Exception("exited: " + x.Stderr)); break;
            }
        };
        s.Send("[Philip is looking at: project Demo, Board section, board Main]\n" + message);
        await done.Task.WaitAsync(TimeSpan.FromMinutes(3));
        return (dir, writes, asked);
    }

    static bool SourceEdited(string dir, List<string> writes) =>
        writes.Any(w => !w.Contains("board", StringComparison.OrdinalIgnoreCase)) ||
        File.ReadAllText(Path.Combine(dir, "SendBack.cs")).Trim() != SourceText.Trim();

    [Fact]
    public async Task A_described_change_becomes_a_card_and_no_code_edit()
    {
        if (Environment.GetEnvironmentVariable("DARG_LIVE") != "1") return;
        var (dir, writes, _) = await Turn("pressing enter in the send back text box should send the ticket back with the note");
        try
        {
            Assert.False(SourceEdited(dir, writes), "edited code: " + string.Join("; ", writes));
            Assert.True(Directory.GetFiles(Path.Combine(dir, "board", "items")).Length >= 2, "no card was made: " + string.Join("; ", writes));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Do_it_now_still_edits_code()
    {
        if (Environment.GetEnvironmentVariable("DARG_LIVE") != "1") return;
        var (dir, writes, asked) = await Turn("pressing enter in the send back text box should send the ticket back with the note. Just fix it, do it now, in SendBack.cs.");
        try { Assert.True(SourceEdited(dir, writes), "no code edit (asked=" + asked + "): " + string.Join("; ", writes)); }
        finally { Directory.Delete(dir, true); }
    }
}
