using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0212: a named queue of cards, worked one at a time. Made from a board in dependency order with no model,
/// run, the next card starting once the one before is done or in QA with nothing waiting on that QA, paused when a card's
/// agent stops without finishing, and carried on past it with Resume.</summary>
public class QueueTests
{
    static void BlockedBy(DargApp app, Card card, params string[] ids)
    {
        var file = Path.Combine(app.BoardFolder, "items", card.File);
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        var at = text.IndexOf("- **Labels:**", StringComparison.Ordinal);
        var end = text.IndexOf('\n', at);
        File.WriteAllText(file, text.Insert(end, $"\n- **Blocked by:** {string.Join(", ", ids)}"));
    }

    static CardViewModel? Tile(DargApp app, string id) => app.Board.Rows.SelectMany(r => r.Lists).SelectMany(l => l.Cards).FirstOrDefault(c => c.Id == id);
    static AgentViewModel? Live(DargApp app, string id) => app.Agents.ForCard(id);
    static CardQueue Queue(DargApp app) => app.Project.Queues!.All.Single();

    [AvaloniaFact]
    public async Task A_queue_runs_its_cards_in_dependency_order_one_at_a_time_and_pauses_on_a_card_left_unfinished()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "4");
        try
        {
            using var app = DargApp.Make();
            var second = app.Card("builds on the first", "Backlog");
            var first = app.Card("the first piece", "Backlog");
            var third = app.Card("separate work", "Backlog");
            BlockedBy(app, second, first.Id);
            await app.Start();
            var asked = new List<string>();
            app.Project.Queues!.Ask = prompt => { asked.Add(prompt); return Task.FromResult<string?>("NOT BLOCKING: separate pieces"); };
            await app.Api("show", new { project = "proj", section = "Board" });

            // made from the board: the blocker first, though it's later on the board; it waits for Run
            var made = JsonNode.Parse(await app.Api("queue-new", new { project = "proj", name = "Travel" }))!;
            Assert.Equal([first.Id, second.Id, third.Id], made["cards"]!.AsArray().Select(c => (string)c!["id"]!));
            Assert.True((bool)made["paused"]!);
            await app.Until(() => Tile(app, first.Id)?.QueueChips.SequenceEqual(["⏵ Travel 1"]) == true, "the tile's queue chip");
            Assert.Null(Live(app, first.Id));

            // Run: only the first starts, moved to In Progress as Work on does
            await app.Api("queue-run", new { project = "proj", queue = "Travel" });
            await app.Until(() => Live(app, first.Id) is not null, "the queue to start the first card");
            await app.Until(() => app.Read(first.Id).Status == "In Progress", "the first card to move to In Progress");
            Assert.Null(Live(app, second.Id));
            Assert.Null(Live(app, third.Id));
            app.Shot("running");

            // it lands in QA: once its agent's turn is over the check is asked, says nothing waits on it, and the next starts
            BoardStore.Move(app.BoardFolder, first.Id, app.ColumnId("QA Ready"));
            await app.Until(() => Live(app, second.Id) is not null, "the second card to start once the first is in QA and the check says go", 40);
            Assert.Contains(asked, p => p.Contains(first.Id) && p.Contains(second.Id));
            Assert.Null(Live(app, third.Id));

            // its agent ends its turn with the card still In Progress: the queue pauses rather than carry on
            await app.Until(() => Queue(app).Paused, "the queue to pause on the unfinished card", 40);
            Assert.Contains(second.Id, Queue(app).Why);
            Assert.Null(Live(app, third.Id));
            var listed = JsonNode.Parse(await app.Api("queues", new { project = "proj" }))!.AsArray().Single()!;
            Assert.Equal("failed", (string)listed["cards"]!.AsArray().Single(c => (string)c!["id"]! == second.Id)!["state"]!);
            app.Shot("paused");

            // Resume moves on past it
            await app.Api("queue-run", new { project = "proj", queue = "Travel" });
            await app.Until(() => Live(app, third.Id) is not null, "Resume to start the third card");
            Assert.Contains(second.Id, Queue(app).Released);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task Plus_Queue_makes_one_from_the_board_showing_and_the_strip_and_card_menu_show_it()
    {
        using var app = DargApp.Make();
        var a = app.Card("one", "Backlog");
        var b = app.Card("two", "To Do");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var queues = app.Project.Queues!;
        Assert.False(queues.ShowsStrip);

        app.Board.NewQueueCommand.Execute(null);
        Assert.True(queues.IsMaking);
        Assert.Equal("Main", queues.NewName);
        queues.NewName = "Next up";
        queues.MakeNewCommand.Execute(null);
        await app.Until(() => queues.Queues.Count == 1 && queues.Queues[0].Summary.StartsWith("0 of 2 done", StringComparison.Ordinal), "the strip's line");
        Assert.Equal([a.Id, b.Id], Queue(app).Cards);
        queues.Queues[0].OpenCommand.Execute(null);
        await DargApp.Settle(200);
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Next up");
        Assert.Contains(app.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == $"2. {b.Id}  two");
        app.Shot("strip");

        // the tile's menu takes it out; a new card's menu adds it
        await app.Until(() => Tile(app, b.Id)?.MoveTargets.Any(m => m.Label == "⏵ Take out of queue Next up") == true, "the menu's take-out entry");
        Tile(app, b.Id)!.MoveTargets.First(m => m.Label == "⏵ Take out of queue Next up").Command.Execute(null);
        Assert.Equal([a.Id], Queue(app).Cards);
        await app.Until(() => Tile(app, b.Id)?.MoveTargets.Any(m => m.Label == "⏵ Add to queue Next up") == true, "the menu's add entry");
        Assert.False(Tile(app, b.Id)!.InQueue);

        // ↓ on the strip moves a card down; nothing is started by any of it
        queues.Add(Queue(app), [b.Id]);
        queues.Queues[0].Cards[0].DownCommand.Execute(null);
        Assert.Equal([b.Id, a.Id], Queue(app).Cards);
        Assert.Null(app.Agents.ForCard(a.Id));
        Assert.True(File.Exists(Path.Combine(ProjectData.Root("proj"), QueueStore.FileName)));
    }
}
