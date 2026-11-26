using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0243: a card whose agent asks the person something (an AskUserQuestion, a permission) while the card is
/// In Progress shows it on the board, as a card in For Human Attention shows its decision: the question on the tile, and
/// the question with its options at the top of the card's panel, where a pick answers the agent.</summary>
public class AgentAskTests
{
    static void Click(Avalonia.Visual c)
    {
        var top = TopLevel.GetTopLevel(c)!;
        var at = c.TranslatePoint(new Avalonia.Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, at);
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, at, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, at, Avalonia.Input.MouseButton.Left);
    }

    /// <summary>The option buttons drawn in the card panel's asks box (not the agent's transcript).</summary>
    static List<Button> PanelOptions(DargApp app) => app.Window.GetVisualDescendants().OfType<Button>()
        .Where(b => b.IsEffectivelyVisible && b.DataContext is QuestionOption
            && b.GetVisualAncestors().OfType<ItemsControl>().Any(i => i.DataContext is CardEditor && Avalonia.Automation.AutomationProperties.GetName(i) == "Its agent asks"))
        .ToList();

    [AvaloniaFact]
    public async Task An_in_progress_cards_agent_question_shows_on_its_tile_and_panel_and_a_pick_there_answers_it()
    {
        using var app = DargApp.Make();
        var card = app.Card("FAKE_ASK pick a board", "To Do"); // the title is in the agent's prompt, so it asks
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        var editor = app.Board.Editor!;
        Assert.False(editor.ShowAgentAsks);

        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.Agents.Single(a => a.CardId == card.Id);
        await app.Until(() => agent.State == AgentState.NeedsYou, "the card's agent to ask");
        Assert.Equal("In Progress", app.Read(card.Id).Status);

        // the open panel picks it up as it comes, at the top, with the agent's own options
        await app.Until(() => editor.ShowAgentAsks, "the panel to show what the agent asks");
        var ask = Assert.Single(editor.AgentAsks);
        Assert.Same(agent, ask.Agent);
        var question = Assert.IsType<QuestionItem>(ask.Item);
        Assert.Equal("Which board?", question.Questions.Single().Question);
        await app.Until(() => PanelOptions(app).Count == 2, "the question's options drawn in the panel");

        // and the tile says what it asks
        await app.Until(() => app.Window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Which board?"
            && t.GetVisualAncestors().OfType<Button>().Any(b => b.Classes.Contains("agentline"))), "the tile to show the question");
        app.Shot("asks-on-card");

        Click(PanelOptions(app).Single(b => ((QuestionOption)b.DataContext!).Label == "Main"));

        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "answered"), "the agent to be answered");
        Assert.Contains("Main", app.ClaudeCalls().First(c => (string?)c["kind"] == "answered")["response"]!.ToJsonString());
        await app.Until(() => !editor.ShowAgentAsks, "the panel's box to go once answered");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_permission_ask_shows_in_the_panel_and_Allow_there_answers_it()
    {
        using var app = DargApp.Make();
        var card = app.Card("FAKE_PERMIT run it", "To Do");
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.Agents.Single(a => a.CardId == card.Id);
        await app.Until(() => agent.State == AgentState.NeedsYou, "the card's agent to ask to use Bash");

        // opened after it asked, the panel shows it too
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        var editor = app.Board.Editor!;
        Assert.True(editor.ShowAgentAsks);
        var permission = Assert.IsType<PermissionItem>(Assert.Single(editor.AgentAsks).Item);
        Assert.Equal("Bash", permission.Tool);

        permission.AllowCommand.Execute(null);
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "permitted"), "the agent to be answered");
        Assert.Contains("allow", app.ClaudeCalls().First(c => (string?)c["kind"] == "permitted")["response"]!.ToJsonString());
        await app.Until(() => !editor.ShowAgentAsks, "the panel's box to go once allowed");
    }
}
