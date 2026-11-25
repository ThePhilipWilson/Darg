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
