using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>QA scratch for WOR-0216 (not committed): real card shapes, a screenshot, and moving with the panel open.</summary>
public class QaNotesBoxTests
{
    [AvaloniaFact]
    public async Task Shot_of_the_box()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo aliasing", "QA Ready", "The logo's edges are jagged.\n\nSecond paragraph of the description.");
        await app.Start();
        await app.Api("send-back", new { project = "proj", card = card.Id, notes = "still jagged at 150% zoom" });
        await app.Api("move-card", new { project = "proj", card = card.Id, column = "needs more work" });
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        await Task.Delay(500);
        app.Shot("box-closed");
        app.Board.Editor!.ToggleFullQaNotesCommand.Execute(null);
        await Task.Delay(300);
        app.Shot("box-open");
    }

    [AvaloniaFact]
    public async Task Long_entry_folds_shot()
    {
        using var app = DargApp.Make();
        var card = app.Card("long notes", "needs more work", "Desc.");
        var lines = string.Join("\n", Enumerable.Range(1, 14).Select(i => $"- point {i}: something about the panel being wrong in this way"));
        QaNotes.Add(Path.Combine(app.BoardFolder, "items", card.File), lines, DateTime.Now);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        await Task.Delay(500);
        Assert.EndsWith(" …", app.Board.Editor!.SendBackText);
        app.Shot("box-folded");
    }

    /// <summary>The shape QA agents actually write (WOR-0212, WOR-0214 on the real board): newest round first.</summary>
    [AvaloniaFact]
    public async Task Newest_first_qa_agent_notes_show_the_newest_round()
    {
        using var app = DargApp.Make();
        var card = app.Card("agent notes newest first", "needs more work", "Desc.");
        var file = Path.Combine(app.BoardFolder, "items", card.File);
        File.AppendAllText(file, "\n\n## QA notes\n\n### 2026-11-25 round 2 (QA agent)\n\n- ROUND TWO finding\n\n### 2026-11-25 (QA agent)\n\n- round one finding\n");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        var e = app.Board.Editor!;
        Assert.True(e.ShowSendBack);
        Assert.Contains("ROUND TWO", e.SendBackText);
    }

    /// <summary>A heading QA agents have written 7 times on the real board.</summary>
    [AvaloniaFact]
    public async Task Dated_qa_notes_heading_shows_a_box()
    {
        using var app = DargApp.Make();
        var card = app.Card("dated heading", "needs more work", "Desc.");
        File.AppendAllText(Path.Combine(app.BoardFolder, "items", card.File), "\n\n## QA notes (2026-11-25)\n\n- the finding\n");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        Assert.True(app.Board.Editor!.ShowSendBack);
    }

    /// <summary>Panel open while the card moves into Needs more work (a QA agent moving it by file).</summary>
    [AvaloniaFact]
    public async Task Box_appears_when_the_card_moves_in_with_the_panel_open()
    {
        using var app = DargApp.Make();
        var card = app.Card("moves while open", "QA Ready", "Desc.");
        QaNotes.Add(Path.Combine(app.BoardFolder, "items", card.File), "the reason", DateTime.Now);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        Assert.False(app.Board.Editor!.ShowSendBack);
        await app.Api("move-card", new { project = "proj", card = card.Id, column = "needs more work" });
        await app.Until(() => app.Board.Editor?.ShowSendBack == true, "box after move with panel open");
    }
}
