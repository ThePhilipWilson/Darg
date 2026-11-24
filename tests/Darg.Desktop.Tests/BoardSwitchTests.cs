using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0195: switching between boards was really laggy. Every card of a column was a control, so a board of
/// 150 cards cost 1.5 to 2.5 s to take down and build up again (headless, Debug); the lists now virtualize, so only
/// the cards in view are drawn.</summary>
public class BoardSwitchTests
{
    static int Drawn(DargApp app) => app.Window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("card"));

    [AvaloniaFact]
    public async Task A_big_board_draws_only_the_cards_in_view_and_switching_stays_quick()
    {
        using var app = DargApp.Make();
        var other = BoardStore.CreateBoard(app.Boards, "Other", DargApp.Columns, "O");
        foreach (var folder in new[] { app.BoardFolder, other })
        {
            var board = BoardStore.Load(folder);
            for (var i = 0; i < 150; i++)
            {
                var c = BoardStore.Create(folder, $"Card {i} on {Path.GetFileName(folder)}", "Some description text\n\n- **Where:** here\n- **Done when:** done");
                BoardStore.Move(folder, c.Id, i % 3 == 0 ? board.Columns.Last().Id : board.Columns.First().Id);
            }
        }
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        var b = app.Board;
        Assert.Equal(150, b.Lists.Sum(l => l.Cards.Count)); // every card is still in the lists, for picking and counting
        Assert.True(Drawn(app) < 60, $"{Drawn(app)} cards were drawn out of 150");

        var times = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            b.PickBoard(b.SelectedBoard == "Main" ? "Other" : "Main", false);
            await DargApp.Settle(0);
            times.Add(sw.ElapsedMilliseconds);
        }
        Assert.True(Drawn(app) < 60, $"{Drawn(app)} cards were drawn after switching");
        app.Shot("big-board");
        // 1.6 to 2.5 s each before; a generous bound, so a slow machine doesn't fail it
        Assert.True(times.Max() < 1200, "switching took " + string.Join(", ", times) + " ms");
        File.WriteAllText(Path.Combine(app.Folder, "switch.txt"), "switching ms: " + string.Join(", ", times));
    }
}
