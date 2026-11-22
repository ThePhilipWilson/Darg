using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0187: a card that waits on the person outside For Human Attention is outlined on the board, so it shows at a glance.</summary>
public class WaitingCardTests
{
    static string Decision() =>
        "## Decision\n\n**Question:** Does it work?\n**Asked by:** agent abc, 2026-11-22 00:00\n\n" +
        "- **Works** (recommended) → done: fine.\n- **Fails** → back: say why.\n\n**Context:** needs eyes.\n";

    static Border Tile(DargApp app, string id) => app.Window.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("card") && b.DataContext is CardViewModel c && c.Id == id);

    static string Colour(IBrush? b) => b is ISolidColorBrush s ? s.Color.ToString() : "?";
    static string Outline() => Avalonia.Application.Current!.TryGetResource("Accent", null, out var b) ? Colour(b as IBrush) : "?";

    [AvaloniaFact]
    public async Task A_card_with_an_open_decision_outside_attention_is_outlined()
    {
        using var app = DargApp.Make();
        var waiting = app.Card("asks from in progress", "In Progress", "Do it.\n\n" + Decision());
        var plain = app.Card("just working", "In Progress");
        var inAttention = app.Card("already in attention", BoardStore.AttentionName, "Do it.\n\n" + Decision());
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        await DargApp.Settle();

        var tile = Tile(app, waiting.Id);
        Assert.Equal(Outline(), Colour(tile.BorderBrush));
        Assert.Equal(2, tile.BorderThickness.Left);
        Assert.NotEqual(Outline(), Colour(Tile(app, plain.Id).BorderBrush));
        Assert.NotEqual(Outline(), Colour(Tile(app, inAttention.Id).BorderBrush)); // the column already says so
        app.Shot("waiting");
    }

    static bool Outlined(DargApp app, string id) { var t = Tile(app, id); return Colour(t.BorderBrush) == Outline() && t.BorderThickness.Left == 2; }

    /// <summary>The card's agent is put in a state by hand (the real asking and failing paths are covered elsewhere); the
    /// tile must follow it, except in For Human Attention and Done.</summary>
    [AvaloniaFact]
    public async Task A_card_whose_agent_asks_or_failed_is_outlined_unless_in_attention_or_done()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "60");
        try
        {
            using var app = DargApp.Make();
            var asks = app.Card("agent asks", "In Progress");
            var failed = app.Card("agent failed", "QA Ready");
            var inAttention = app.Card("asks in attention", BoardStore.AttentionName);
            var done = app.Card("failed in done", "Done");
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });
            foreach (var c in new[] { asks, failed, inAttention, done })
            {
                await app.Api("work-on", new { project = "proj", card = c.Id });
                await app.Until(() => app.Agents.ForCard(c.Id)?.State == AgentState.Working, "the agent on " + c.Id + " to start");
            }
            app.Agents.ForCard(asks.Id)!.State = AgentState.NeedsYou;
            var f = app.Agents.ForCard(failed.Id)!; f.TurnFailed = true; f.State = AgentState.Idle;
            app.Agents.ForCard(inAttention.Id)!.State = AgentState.NeedsYou;
            var d = app.Agents.ForCard(done.Id)!; d.TurnFailed = true; d.State = AgentState.Idle;
            await DargApp.Settle();

            Assert.True(Outlined(app, asks.Id), "an agent asking");
            Assert.True(Outlined(app, failed.Id), "an agent failed");
            Assert.False(Outlined(app, inAttention.Id), "in attention");
            Assert.False(Outlined(app, done.Id), "in done");
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    /// <summary>The outline is the theme's own Accent, so it shows in all three themes; a screenshot of each is kept.</summary>
    [AvaloniaFact]
    public async Task The_outline_follows_the_theme()
    {
        using var app = DargApp.Make();
        var waiting = app.Card("asks from in progress", "In Progress", "Do it.\n\n" + Decision());
        var plain = app.Card("just working", "In Progress");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board" });
        foreach (var theme in Themes.All)
        {
            await app.Api("theme", new { theme = theme.Id });
            await DargApp.Settle();
            Assert.Equal(Color.Parse(theme.Colours["Accent"]).ToString(), Colour(Tile(app, waiting.Id).BorderBrush));
            Assert.NotEqual(Colour(Tile(app, waiting.Id).BorderBrush), Colour(Tile(app, plain.Id).BorderBrush));
            app.Shot("waiting-" + theme.Id);
        }
    }
}
