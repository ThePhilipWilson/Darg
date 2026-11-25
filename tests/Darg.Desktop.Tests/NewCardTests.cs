using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0139: the API's new-card makes a card in one call, on the project's inbox board, and has Haiku place
/// it on a story board as a note's ticket does. Fettle wrote card files and board.json by hand before.</summary>
public class NewCardTests
{
    [AvaloniaFact]
    public async Task One_call_makes_a_well_formed_card_in_the_backlog_and_returns_its_id()
    {
        using var app = DargApp.Make();
        await app.Start();

        var reply = JsonNode.Parse(await app.Api("new-card", new
        {
            project = "proj", title = "Export the board as CSV", description = "A button on the board's menu.",
            type = "story", labels = new[] { "fettle", "board view" }, priority = "High",
        }))!.AsObject();

        var id = (string)reply["id"]!;
        Assert.Equal("T-0001", id);
        Assert.Equal("Main", (string?)reply["board"]);
        Assert.False((bool)reply["placing"]!); // one board: nowhere to move it
        var card = app.Read(id);
        Assert.Equal("Export the board as CSV", card.Title);
        Assert.Equal("Backlog", card.Status);
        Assert.Equal("Story", card.Type);
        Assert.Equal("High", card.Priority);
        Assert.Equal(["fettle", "board view"], card.Labels);
        Assert.Equal("A button on the board's menu.", card.Description.Trim());
        Assert.Equal(Path.Combine(app.BoardFolder, "items", card.File), (string)reply["file"]!, ignoreCase: true);
        Assert.True(File.Exists((string)reply["file"]!));
        Assert.Contains(id, BoardStore.Load(app.BoardFolder).Lists[Board.Backlog].Select(c => c.Id));

        // the board on screen has it without waiting for the file watcher
        await app.Api("show", new { project = "proj", section = "Board", card = id });
        Assert.Equal(id, app.Board.Editor?.Heading);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Labels_can_be_one_string_and_the_type_is_guessed_from_the_words()
    {
        using var app = DargApp.Make();
        await app.Start();

        var id = (string)JsonNode.Parse(await app.Api("new-card", new
        {
            project = "proj", title = "Fettle's box crashes when dragged", labels = "fettle, ui polish",
        }))!["id"]!;

        var card = app.Read(id);
        Assert.Equal("Bug", card.Type);
        Assert.Equal(["fettle", "ui polish"], card.Labels);
    }

    [AvaloniaFact]
    public async Task A_bad_type_or_no_title_is_refused_and_makes_nothing()
    {
        using var app = DargApp.Make();
        await app.Start();

        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("new-card", new { project = "proj", title = "x", type = "epic" }));
        Assert.Contains("No card type", bad.Message);
        var none = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("new-card", new { project = "proj" }));
        Assert.Contains("title", none.Message);
        Assert.Empty(BoardStore.ReadCards(app.BoardFolder));
    }

    [AvaloniaFact]
    public async Task With_story_boards_Haiku_is_asked_where_the_card_goes()
    {
        using var app = DargApp.Make();
        BoardStore.CreateBoard(app.Boards, "Art", DargApp.Columns, "T");
        await app.Start();

        var reply = JsonNode.Parse(await app.Api("new-card", new { project = "proj", title = "Paint the splash screen" }))!.AsObject();

        Assert.Equal("Main", (string?)reply["board"]);
        Assert.True((bool)reply["placing"]!);
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "ask" && ((string?)c["text"] ?? "").Contains("Paint the splash screen")),
            "Haiku asked which story board the new card fits");
    }

    /// <summary>WOR-0207: a Haiku question starts no MCP servers. Without --strict-mcp-config every one started the
    /// person's whole set (npx, uvx and node), about 1 GB and eight node processes for one word back.</summary>
    [AvaloniaFact]
    public async Task A_Haiku_question_starts_no_MCP_servers()
    {
        using var app = DargApp.Make();
        BoardStore.CreateBoard(app.Boards, "Art", DargApp.Columns, "T");
        await app.Start();

        await app.Api("new-card", new { project = "proj", title = "Paint the title screen" });

        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "ask"), "a Haiku question");
        var args = app.ClaudeCalls().First(c => (string?)c["kind"] == "ask")["args"]!.AsArray().Select(a => (string)a!).ToList();
        Assert.Contains("--strict-mcp-config", args);
        Assert.DoesNotContain("--mcp-config", args);
    }
}
