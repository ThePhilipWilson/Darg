using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>AND-0004: the API commands the phone's board uses: boards (every column in order, empty ones too),
/// move-card (a move menu's pick) and new-card with a board and column (the quick-add at the foot of a column).</summary>
public class PhoneBoardApiTests
{
    [AvaloniaFact]
    public async Task Boards_lists_every_column_in_order_with_its_cards_and_kind()
    {
        using var app = DargApp.Make();
        var waiting = app.Card("Triage me");
        var check = app.Card("Check the header", "QA Ready");
        await app.Start();

        var boards = JsonNode.Parse(await app.Api("boards", new { project = "proj" }))!.AsArray();
        var main = boards.Single()!.AsObject();
        Assert.Equal("Main", (string?)main["name"]);
        Assert.True((bool)main["inbox"]!);
        var lists = main["lists"]!.AsArray();
        Assert.Equal(new[] { "Backlog" }.Concat(DargApp.Columns).Append("Completed"), lists.Select(l => (string)l!["name"]!));
        var qa = lists.Single(l => (string?)l!["kind"] == "qa")!;
        Assert.Equal("QA Ready", (string?)qa["name"]);
        Assert.Equal(check.Id, (string?)qa["cards"]![0]!["id"]);
        Assert.Equal(waiting.Id, (string?)lists[0]!["cards"]![0]!["id"]);
        Assert.Empty(lists.Single(l => (string?)l!["name"] == "In Progress")!["cards"]!.AsArray()); // empty columns are there too
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Move_card_moves_it_on_disk_and_on_the_board_on_screen_at_once()
    {
        using var app = DargApp.Make();
        var a = app.Card("First");
        var b = app.Card("Second");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });

        var reply = await app.Api("move-card", new { project = "proj", card = a.Id, column = "In Progress" });
        Assert.Contains("In Progress", reply);
        await app.Api("move-card", new { project = "proj", card = b.Id, column = "in progress", before = a.Id });

        Assert.Equal("In Progress", app.Read(a.Id).Status);
        Assert.Equal([b.Id, a.Id], BoardStore.Load(app.BoardFolder).Lists[app.ColumnId("In Progress")].Select(c => c.Id));
        // the desktop board shows the move without waiting for its file watcher
        Assert.Equal([b.Id, a.Id], app.Board.Lists.Single(l => l.Name == "In Progress").Cards.Select(c => c.Id));

        await app.Api("move-card", new { project = "proj", card = a.Id, column = "qa" }); // by kind, whatever the board calls it
        Assert.Equal("QA Ready", app.Read(a.Id).Status);

        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("move-card", new { project = "proj", card = a.Id, column = "Shelved" }));
        Assert.Contains("No column", bad.Message);
        Assert.Equal("QA Ready", app.Read(a.Id).Status);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task New_card_with_a_column_lands_there_on_the_board_named_and_is_not_placed()
    {
        using var app = DargApp.Make();
        BoardStore.CreateBoard(app.Boards, "Art", ["Doing", "Shipped"], "A");
        await app.Start();

        var reply = JsonNode.Parse(await app.Api("new-card", new { project = "proj", title = "Paint the splash screen", board = "Art", column = "Doing" }))!.AsObject();
        Assert.Equal("Art", (string?)reply["board"]);
        Assert.Equal("Doing", (string?)reply["column"]);
        Assert.False((bool)reply["placing"]!);
        var art = Path.Combine(app.Boards, "Art");
        Assert.Equal("Doing", BoardStore.ReadCards(art).Single(c => c.Id == (string)reply["id"]!).Status);

        // just a column: the inbox board
        var todo = JsonNode.Parse(await app.Api("new-card", new { project = "proj", title = "Sort the backlog", column = "To Do" }))!.AsObject();
        Assert.Equal("Main", (string?)todo["board"]);
        Assert.Equal("To Do", app.Read((string)todo["id"]!).Status);

        await Task.Delay(500); // Haiku placing would have started by now; nothing should ask it
        Assert.DoesNotContain(app.ClaudeCalls(), c => ((string?)c["text"] ?? "").Contains("Paint the splash screen"));
        Assert.Equal("", app.Crashes());
    }
}
