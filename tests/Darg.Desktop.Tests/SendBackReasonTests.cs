using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0216: a card in Needs more work shows why it came back, the newest QA notes entry, at the top of its panel.</summary>
public class SendBackReasonTests
{
    [AvaloniaFact]
    public async Task A_card_in_needs_more_work_shows_the_newest_send_back_notes_above_its_description()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "QA Ready", "The logo's edges are jagged.");
        await app.Start();
        await app.Api("send-back", new { project = "proj", card = card.Id, notes = "still jagged at 150% zoom" });
        await app.Api("move-card", new { project = "proj", card = card.Id, column = "needs more work" });

        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        var editor = app.Board.Editor!;
        Assert.True(editor.ShowSendBack);
        Assert.Contains("still jagged at 150% zoom", editor.SendBackText);
        Assert.StartsWith("Sent back 20", editor.SendBackWhen);
        Assert.False(editor.SendBackFullOpen);
        editor.ToggleFullQaNotesCommand.Execute(null);
        Assert.True(editor.SendBackFullOpen);
        Assert.Contains("still jagged at 150% zoom", editor.QaNotesFull);

        // a second send-back: the box shows the new entry, the old one only under Full QA notes
        QaNotes.Add(Path.Combine(app.BoardFolder, "items", app.Read(card.Id).File), "now the edges are fine but the shadow is wrong", DateTime.Now.AddMinutes(5));
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        editor = app.Board.Editor!;
        Assert.Contains("shadow is wrong", editor.SendBackText);
        Assert.DoesNotContain("still jagged", editor.SendBackText);
        Assert.Contains("still jagged", editor.QaNotesFull);
    }

    /// <summary>QA agents write their rounds newest first (the class: order of entries differs by writer).</summary>
    [AvaloniaFact]
    public async Task Newest_first_agent_notes_show_the_newest_round()
    {
        using var app = DargApp.Make();
        var card = app.Card("agent notes newest first", "needs more work", "Desc.");
        File.AppendAllText(Path.Combine(app.BoardFolder, "items", card.File),
            "\n\n## QA notes\n\n### 2026-11-25 round 2 (QA agent)\n\n- ROUND TWO finding\n\n### 2026-11-25 (QA agent)\n\n- round one finding\n");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        Assert.Contains("ROUND TWO", app.Board.Editor!.SendBackText);
        Assert.DoesNotContain("round one", app.Board.Editor!.SendBackText);
    }

    [Theory]
    [InlineData("## QA notes (2026-11-18)")]
    [InlineData("## QA Notes")]
    [InlineData("## QA notes:")]
    [InlineData("## QA notes   ")]
    public void Heading_shapes_agents_write_are_all_read(string heading)
    {
        var text = $"# T\n\n## Description\n\nd\n\n{heading}\n\n### 2026-11-25 10:00\n\nthe finding\n\n## Other\n\nelsewhere\n";
        Assert.Equal("### 2026-11-25 10:00\n\nthe finding", CardIndex.QaNotesOf(text));
        Assert.Equal("the finding", QaNotes.Latest(text)!.Value.Text);
        Assert.Equal("", CardIndex.QaNotesOf("## QA notes for later\n\nnot it\n"));
    }

    [AvaloniaFact]
    public async Task The_box_appears_when_the_card_moves_in_with_its_panel_open()
    {
        using var app = DargApp.Make();
        var card = app.Card("moves while open", "QA Ready", "Desc.");
        QaNotes.Add(Path.Combine(app.BoardFolder, "items", card.File), "the reason", DateTime.Now);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        Assert.False(app.Board.Editor!.ShowSendBack);
        await app.Api("move-card", new { project = "proj", card = card.Id, column = "needs more work" });
        await app.Until(() => app.Board.Editor?.ShowSendBack == true, "box after the move, panel open");
    }

    [AvaloniaFact]
    public async Task No_box_in_another_column_or_with_no_qa_notes()
    {
        using var app = DargApp.Make();
        var noted = app.Card("has notes, in progress", "In Progress", "Something.");
        QaNotes.Add(Path.Combine(app.BoardFolder, "items", noted.File), "was wrong", DateTime.Now);
        var bare = app.Card("needs more work, no notes", "needs more work", "Something.");
        await app.Start();

        await app.Api("show", new { project = "proj", section = "Board", card = noted.Id });
        Assert.False(app.Board.Editor!.ShowSendBack);
        await app.Api("show", new { project = "proj", section = "Board", card = bare.Id });
        Assert.False(app.Board.Editor!.ShowSendBack);
    }
}
