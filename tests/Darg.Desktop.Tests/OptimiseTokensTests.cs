using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0159: the header's Optimise tokens toggle puts agents one model down (and effort no higher than
/// medium) while it's on, and back when it's off.</summary>
public class OptimiseTokensTests
{
    /// <summary>A card labelled deep, which the starter profiles put on Deep: opus at high effort.</summary>
    static Card DeepCard(DargApp app)
    {
        var card = app.Card("redesign the board", "To Do", "The board needs a rethink.");
        var file = Directory.GetFiles(app.BoardFolder, $"{card.Id}*.md", SearchOption.AllDirectories).Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("- **Labels:**", "- **Labels:** deep"));
        return app.Read(card.Id);
    }

    static List<string> StartArgs(DargApp app) =>
        app.ClaudeCalls().First(c => (string?)c["kind"] == "start")["args"]!.AsArray().Select(a => (string)a!).ToList();

    static string? After(List<string> args, string flag) => args.IndexOf(flag) is var i and >= 0 ? args[i + 1] : null;

    static ToggleButton Toggle(DargApp app) =>
        app.Window.GetVisualDescendants().OfType<ToggleButton>().Single(t => AutomationProperties.GetName(t) == "Optimise tokens");

    [AvaloniaFact]
    public async Task Off_an_agent_starts_on_its_profile()
    {
        using var app = DargApp.Make();
        var card = DeepCard(app);
        await app.Start();
        Assert.False(Toggle(app).IsChecked);

        await app.Api("work-on", new { project = "proj", card = card.Id });
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "start"), "the agent's process to start");

        var args = StartArgs(app);
        Assert.Equal("opus", After(args, "--model"));
        Assert.Equal("high", After(args, "--effort"));
    }

    [AvaloniaFact]
    public async Task On_an_agent_starts_one_model_down_and_off_puts_it_back()
    {
        using var app = DargApp.Make();
        var card = DeepCard(app);
        await app.Start();
        try
        {
            Toggle(app).IsChecked = true; // as a click does
            await DargApp.Settle();
            Assert.True(app.Main.Usage.OptimiseTokens);
            app.Shot("on");
            Assert.Contains("ON", Toggle(app).Content?.ToString());
            var fill = (Toggle(app).GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First() as Avalonia.Controls.Presenters.ContentPresenter)?.Background as Avalonia.Media.ISolidColorBrush;
            Assert.Equal(((Avalonia.Media.ISolidColorBrush)ThemeManager.Brush("Accent")).Color, fill?.Color);
            Assert.True(TokenSaver.On);
            Assert.Contains("\"optimiseTokens\": true", File.ReadAllText(Path.Combine(app.Home, "settings.json")));

            await app.Api("work-on", new { project = "proj", card = card.Id });
            await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "start"), "the agent's process to start");
            var args = StartArgs(app);
            Assert.Equal("sonnet", After(args, "--model"));
            Assert.Equal("medium", After(args, "--effort"));
            var agent = Assert.Single(app.Agents.Agents);
            Assert.Contains(agent.Items.OfType<NoteItem>(), n => n.Text.Contains("Optimise tokens is on: starting on sonnet, effort medium"));

            Assert.Contains("off", await app.Api("optimise-tokens", new { on = false }));
            Assert.False(Toggle(app).IsChecked);
            Assert.False(TokenSaver.On);
            Assert.Contains(agent.Items.OfType<NoteItem>(), n => n.Text.Contains("Optimise tokens off: back to opus, effort high"));
        }
        finally { TokenSaver.Set(false, app.Home); }
    }

    [AvaloniaFact]
    public void A_hand_pick_survives_a_restart()
    {
        using var app = DargApp.Make();
        var dir = Directory.CreateTempSubdirectory("wb-hand-").FullName;
        try
        {
            var record = new AgentRecord { Id = "h1", Name = "x", Cwd = dir, Model = "opus", Effort = "high", ModelByHand = true };
            var agent = new AgentViewModel(record, dir);
            agent.Save();
            var saved = AgentStore.List(dir).Single();
            Assert.True(saved.ModelByHand);
            Assert.False(saved.EffortByHand);
        }
        finally { Directory.Delete(dir, true); }
    }
}
