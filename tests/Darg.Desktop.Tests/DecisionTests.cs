using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0124: a card in For Human Attention shows the decision it waits on, picking an option acts on it,
/// and a card that lands there without one gets one asked for.</summary>
public class DecisionTests
{
    static string Decision(string agent = "") =>
        "## Decision\n\n" +
        "**Question:** Does dictation work for you?\n" +
        (agent.Length > 0 ? $"**Asked by:** agent {agent}, 2026-11-18 00:00\n" : "") + "\n" +
        "- **Still fails** → back: say what the box showed.\n" +
        "- **Works** (recommended) → done: words land in the box.\n" +
        "- **Not tried yet** → stay: leave it here.\n\n" +
        "**Context:** a real microphone can't be tried from an agent.\n";

    static string Body(string decision = "") => "Dictate into the box.\n\n" + decision + "\n## How to check\n\nClick the mic and speak.\n";

    static string FileOf(DargApp app, Card card) => Path.Combine(app.BoardFolder, "items", card.File);

    [AvaloniaFact]
    public async Task The_panel_shows_the_decision_in_place_of_the_description_recommended_first()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Body(Decision()));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });

        var editor = app.Board.Editor!;
        Assert.True(editor.ShowDecision);
        Assert.False(editor.MissingDecision);
        Assert.False(editor.DescriptionOpen);
        Assert.False(editor.CanQaVerdict); // the options replace Approve and Send back
        Assert.Equal("Does dictation work for you?", editor.DecisionQuestion);
        Assert.Equal(["Works", "Still fails", "Not tried yet"], editor.DecisionChoices.Select(c => c.Label));
        Assert.True(editor.DecisionChoices[0].Recommended);
        Assert.Equal("a real microphone can't be tried from an agent.", editor.DecisionContext);
        app.Shot("decision");
        Assert.Equal("", app.Crashes());
    }

    /// <summary>WOR-0168: a long option label ran under "→ send back" in its corner: the label sat in a horizontal
    /// StackPanel, which lets a child be as wide as it likes, so it never wrapped. It wraps before the corner now.</summary>
    [AvaloniaFact]
    public async Task A_long_option_label_wraps_before_its_action_text()
    {
        using var app = DargApp.Make();
        var longLabel = "Send it back because the long title still runs under the corner text and needs to wrap well before it reaches the edge";
        var card = app.Card("long", BoardStore.AttentionName, Body(Decision().Replace("**Still fails**", $"**{longLabel}**")));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        app.Shot("long-label");

        var label = app.Window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == longLabel);
        var action = app.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "→ send back" && t.DataContext is DecisionChoice c && c.Label == longLabel);
        var labelRight = label.TranslatePoint(new Avalonia.Point(label.Bounds.Width, 0), app.Window)!.Value.X;
        var actionLeft = action.TranslatePoint(new Avalonia.Point(0, 0), app.Window)!.Value.X;
        Assert.True(labelRight <= actionLeft, $"label ends at {labelRight}, action text starts at {actionLeft}");
        Assert.True(label.Bounds.Height > action.Bounds.Height * 1.5, "the long label should wrap onto more than one line");
        Assert.Equal("", app.Crashes());
    }

    /// <summary>WOR-0147: "does this look okay" with nothing to look at is no use: a picture in the card file shows in the decision box.</summary>
    [AvaloniaFact]
    public async Task The_decision_box_shows_a_screenshot_from_the_card()
    {
        using var app = DargApp.Make();
        var png = Path.Combine(app.BoardFolder, "shot.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        var withShot = Body(Decision()) + $"\n![the panel]({png.Replace('\\', '/')})\n";
        var card = app.Card("looks", BoardStore.AttentionName, withShot);
        var plain = app.Card("no shot", BoardStore.AttentionName, Body(Decision()));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        Assert.True(app.Board.Editor!.HasDecisionPictures);
        Assert.Single(app.Board.Editor.DecisionPictures);
        await app.Api("show", new { project = "proj", section = "Board", card = plain.Id });
        Assert.False(app.Board.Editor!.HasDecisionPictures);
        Assert.Equal("", app.Crashes());
    }

    static string Png(DargApp app, string name, int width, int height)
    {
        var path = Path.Combine(app.BoardFolder, name);
        using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var frame = bitmap.Lock())
        {
            // a gradient with a frame round it, so a shot shows the whole picture and where it ends
            var row = new int[width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    row[x] = x < 8 || y < 8 || x >= width - 8 || y >= height - 8 ? unchecked((int)0xFFFFFFFF)
                        : unchecked((int)0xFF000000) | (x * 255 / width << 16) | (y * 255 / height << 8) | 0x60;
                System.Runtime.InteropServices.Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, width);
            }
        }
        bitmap.Save(path);
        return path.Replace('\\', '/');
    }

    static void Click(Avalonia.Visual c)
    {
        var top = Avalonia.Controls.TopLevel.GetTopLevel(c)!;
        var at = c.TranslatePoint(new Avalonia.Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, at);
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, at, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, at, Avalonia.Input.MouseButton.Left);
    }

    static void Key(DargApp app, Avalonia.Input.Key key, Avalonia.Input.PhysicalKey physical) =>
        Avalonia.Headless.HeadlessWindowExtensions.KeyPress(app.Window, key, Avalonia.Input.RawInputModifiers.None, physical, null);

    /// <summary>WOR-0167: a screenshot in the decision box is cut down to fit; a click on it shows it at full size over
    /// the board, the arrows step to the next, and a click on it or Escape closes it.</summary>
    [AvaloniaFact]
    public async Task Clicking_a_decision_picture_shows_it_full_size_until_clicked_or_escaped()
    {
        using var app = DargApp.Make();
        var wide = Png(app, "wide.png", 1600, 900);
        var other = Png(app, "other.png", 300, 200);
        var card = app.Card("looks", BoardStore.AttentionName, Body(Decision()) + $"\n![the panel]({wide})\n\n![the other]({other})\n");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        var editor = app.Board.Editor!;
        Assert.Equal(2, editor.DecisionPictures.Count);
        Assert.Equal(720, editor.DecisionPictures[0].Picture.PixelSize.Width); // the box's copy is cut down

        List<Avalonia.Controls.Button> Thumbs() => app.Window.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
            .Where(b => b.Classes.Contains("enlarge") && b.IsEffectivelyVisible).ToList();
        Avalonia.Controls.Border Lightbox() => app.Window.GetVisualDescendants().OfType<Avalonia.Controls.Border>().Single(b => b.Name == "Lightbox" && b.DataContext == editor);
        await app.Until(() => Thumbs().Count == 2, "the decision pictures drawn");
        Assert.False(Lightbox().IsVisible);
        app.Shot("decision-pictures");

        Click(Thumbs()[0]);
        Assert.True(editor.IsEnlarged);
        Assert.True(Lightbox().IsVisible);
        Assert.Equal(1600, editor.Enlarged!.PixelSize.Width); // the file at its own size
        Assert.Contains("1 of 2", editor.EnlargedCaption);
        app.Shot("enlarged");

        Key(app, Avalonia.Input.Key.Right, Avalonia.Input.PhysicalKey.ArrowRight);
        Assert.Equal(300, editor.Enlarged!.PixelSize.Width);
        Key(app, Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
        Assert.False(editor.IsEnlarged);
        Assert.Same(editor, app.Board.Editor); // Escape closed the picture, not the card

        Click(Thumbs()[1]);
        Assert.Equal(300, editor.Enlarged!.PixelSize.Width);
        Click(Lightbox());
        Assert.False(editor.IsEnlarged);
        Assert.False(Lightbox().IsVisible);
        Assert.Equal("", app.Crashes());
    }

    /// <summary>QA found a "## decision" card shown and moved to Done with its answer never written, so it came back
    /// still asking. Written by the QA agent (.shots/qa-0124); failed before the fix.</summary>
    [AvaloniaFact]
    public async Task A_lowercase_decision_heading_is_answered_like_any_other()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Body(Decision().Replace("## Decision", "## decision")));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });
        Assert.True(app.Board.Editor!.ShowDecision);

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Works" });
        var text = File.ReadAllText(FileOf(app, card));
        Assert.Equal("Done", app.Read(card.Id).Status);
        Assert.Contains("**Answer:** Works", text);
        Assert.Null(CardDecision.Read(text));
    }

    [AvaloniaFact]
    public async Task A_card_with_no_decision_shows_the_fallback()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Body());
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });

        var editor = app.Board.Editor!;
        Assert.False(editor.ShowDecision);
        Assert.True(editor.MissingDecision);
        Assert.True(editor.DescriptionOpen);
        Assert.True(editor.CanQaVerdict); // Approve and Send back are the fallback
        Assert.Equal("Click the mic and speak.", editor.WhyHere);
        app.Shot("no-decision");
    }

    [AvaloniaFact]
    public async Task Picking_a_done_option_records_the_answer_and_moves_the_card_to_done()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Body(Decision()));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });

        app.Board.Editor!.DecisionChoices.Single(c => c.Label == "Works").Command.Execute(null);
        await DargApp.Settle();

        Assert.Equal("Done", app.Read(card.Id).Status);
        var text = File.ReadAllText(FileOf(app, card));
        Assert.Null(CardDecision.Read(text));
        Assert.Contains("## Decision answered", text);
        Assert.Contains("**Answer:** Works", text);
        Assert.Empty(app.Agents.Agents);
    }

    [AvaloniaFact]
    public async Task Picking_a_back_option_sends_it_back_with_the_question_and_notes()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Body(Decision()));
        await app.Start();

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "still fails", notes = "the box said no sound" });

        Assert.Equal("In Progress", app.Read(card.Id).Status);
        var text = File.ReadAllText(FileOf(app, card));
        Assert.Contains("## QA notes", text);
        Assert.Contains("Does dictation work for you? I picked: Still fails. the box said no sound", text);
        var agent = Assert.Single(app.Agents.Agents);
        Assert.Equal(AgentPurposes.Rework, agent.Purpose);
    }

    [AvaloniaFact]
    public async Task A_stay_option_leaves_the_card_and_its_decision_open()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Body(Decision()));
        await app.Start();

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Not tried yet" });

        Assert.Equal(BoardStore.AttentionName, app.Read(card.Id).Status);
        Assert.NotNull(CardDecision.Read(File.ReadAllText(FileOf(app, card))));
        Assert.Empty(app.Agents.Agents);
    }

    [AvaloniaFact]
    public async Task An_answer_of_my_own_goes_to_a_new_card_agent_when_the_asker_is_gone()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", BoardStore.AttentionName, Body(Decision("0fa5c0cd01")));
        await app.Start();

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Use Whisper instead" });

        Assert.Equal("In Progress", app.Read(card.Id).Status);
        var agent = Assert.Single(app.Agents.Agents);
        Assert.Equal(card.Id, agent.CardId);
        await app.Until(() => app.Prompts().Count > 0, "the new agent to be sent its prompt");
        Assert.Contains("My answer: Use Whisper instead", app.Prompts()[0]);
        Assert.Contains("The question: Does dictation work for you?", app.Prompts()[0]);
        Assert.Contains("**Answer:** Use Whisper instead", File.ReadAllText(FileOf(app, card)));
    }

    [AvaloniaFact]
    public async Task An_answer_of_my_own_reaches_a_card_agent_when_the_asker_has_left_the_list()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", "In Progress", Body());
        await app.Start();
        // it writes its decision and moves the card while at work, as its rules say
        var asker = await WorkMovingTo(app, card, () =>
        {
            File.WriteAllText(FileOf(app, card), File.ReadAllText(FileOf(app, card)).Replace("## How to check", Decision(app.Agents.ForCard(card.Id)!.Id) + "\n## How to check"));
            Assert.Null(app.Board.MoveToAttention(card.Id));
        });
        // the asker goes once its turn ends, though its card waits on the person (Philip, 2026-11-22): the decision is on the card
        await app.Until(() => !app.Agents.Agents.Contains(asker), "the asker to leave the list once its turn ended");

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Use Whisper instead", notes = "the small model" });

        await app.Until(() => app.Prompts().Any(p => p.Contains("I have answered it")), "the answer to reach a fresh card agent");
        var fresh = Assert.Single(app.Agents.Agents);
        Assert.NotEqual(asker.Id, fresh.Id);
        Assert.Equal(card.Id, fresh.CardId);
        Assert.DoesNotContain(app.Prompts(), p => p.Contains("decision: you asked")); // the asker, gone, isn't resumed
        var said = app.Prompts().Single(p => p.Contains("I have answered it"));
        Assert.Contains("My answer: Use Whisper instead", said);
        Assert.Contains("My notes: the small model", said);
        Assert.Equal("In Progress", app.Read(card.Id).Status);
    }

    /// <summary>The card is moved while the agent is still at work, as a card agent's rules have it. Returns once its turn
    /// has ended (a task agent then leaves the list, WOR-0183, unless Darg holds it to ask for a missing decision).</summary>
    static async Task<AgentViewModel> WorkMovingTo(DargApp app, Card card, Action move)
    {
        using var turns = app.HoldTurns();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.ForCard(card.Id)!;
        await app.Until(() => agent.State == AgentState.Working, "the card agent to start working");
        move();
        await DargApp.Settle(1000); // the board read and the move acted on while the agent is still at work
        turns.Release();
        // idle, waiting on the person, or already off the list and saved (WOR-0183)
        await app.Until(() => agent.State is not (AgentState.Starting or AgentState.Working), "the card agent's turn to end");
        return agent;
    }

    /// <summary>WOR-0169: a card with its agent, which has written the decision and moved the card to For Human
    /// Attention, as a card agent's rules say, and is still at work: a task agent leaves the list once its turn ends
    /// (WOR-0183), so the decision shows on it only while it runs.</summary>
    static async Task<(Card Card, AgentViewModel Agent)> AgentAsks(DargApp app)
    {
        var card = app.Card("voice mode", "In Progress", Body());
        await app.Start();
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "60");
        try
        {
            await app.Api("work-on", new { project = "proj", card = card.Id });
            var agent = app.Agents.ForCard(card.Id)!;
            await app.Until(() => agent.State == AgentState.Working, "the card agent to start working");
            File.WriteAllText(FileOf(app, card), File.ReadAllText(FileOf(app, card)).Replace("## How to check", Decision(agent.Id) + "\n## How to check"));
            Assert.Null(app.Board.MoveToAttention(card.Id));
            return (card, agent);
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task The_cards_decision_shows_on_its_agent_and_picking_there_answers_the_card()
    {
        using var app = DargApp.Make();
        var (card, agent) = await AgentAsks(app);
        await app.Until(() => agent.OpenDecisions.Any(), "the card's decision to show on its agent");
        var item = agent.OpenDecisions.Single();
        Assert.Equal(card.Id, item.CardId);
        Assert.Equal("Does dictation work for you?", item.Question);
        Assert.Equal(["Works (recommended)", "Still fails", "Not tried yet"], item.Options.Select(o => o.Label));
        app.Agents.Selected = agent;
        await app.Api("show", new { project = "proj", section = "Agents" });
        await DargApp.Settle(200);
        app.Shot("decision-on-agent");

        item.Options[0].Command.Execute(null); // Works → done
        Assert.False(item.Open);
        Assert.Equal("You picked: Works", item.Outcome);
        Assert.Equal("Done", app.Read(card.Id).Status);
        var text = File.ReadAllText(FileOf(app, card));
        Assert.Contains("**Answer:** Works", text);
        Assert.Null(CardDecision.Read(text));
        await DargApp.Settle(300);
        Assert.Empty(agent.OpenDecisions); // and the board's next read doesn't put it back
        // a saved agent keeps the answered decision as a line in its transcript (still at work here, it goes when its turn ends)
        agent.Save();
        AgentStore.Flush();
        var saved = AgentStore.List(agent.StoreFolder!).Single(r => r.Id == agent.Id);
        Assert.Contains(saved.Items, i => i.Kind == "note" && i.Text == $"{card.Id} asked: Does dictation work for you? → You picked: Works");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Answering_the_cards_decision_closes_it_on_the_agent()
    {
        using var app = DargApp.Make();
        var (card, agent) = await AgentAsks(app);
        await app.Until(() => agent.OpenDecisions.Any(), "the card's decision to show on its agent");
        var item = agent.OpenDecisions.Single();

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Works" });

        // answered through the API: it closes on the agent too, saying how
        Assert.False(item.Open);
        Assert.Contains("approved", item.Outcome);
        Assert.Equal("Done", app.Read(card.Id).Status);
    }

    [AvaloniaFact]
    public async Task A_stay_picked_on_the_agent_leaves_the_decision_open_on_both()
    {
        using var app = DargApp.Make();
        var (card, agent) = await AgentAsks(app);
        await app.Until(() => agent.OpenDecisions.Any(), "the card's decision to show on its agent");
        var item = agent.OpenDecisions.Single();

        item.Pick("Not tried yet");
        Assert.True(item.Open);
        Assert.Contains("stays", item.Note);
        Assert.NotNull(CardDecision.Read(File.ReadAllText(FileOf(app, card))));
        Assert.Equal(BoardStore.AttentionName, app.Read(card.Id).Status);
    }

    [AvaloniaFact]
    public async Task A_card_moved_out_of_attention_closes_its_decision_on_the_agent()
    {
        using var app = DargApp.Make();
        var (card, agent) = await AgentAsks(app);
        await app.Until(() => agent.OpenDecisions.Any(), "the card's decision to show on its agent");
        var item = agent.OpenDecisions.Single();

        await app.Api("move-card", new { project = "proj", card = card.Id, column = "In Progress" });

        await app.Until(() => !item.Open, "the agent's decision to close once the card left the attention column");
        Assert.Contains("no open decision", item.Outcome);
    }

    /// <summary>WOR-0225: a QA agent writes the card's decision, moves the card to For Human Attention and then asks the
    /// same question in its chat (AskUserQuestion), so it sits waiting on the person. Returns once it is.</summary>
    static async Task<(Card Card, AgentViewModel Asker)> QaAsksInChatToo(DargApp app)
    {
        var card = app.Card("FAKE_ASK voice mode", "QA Ready", Body()); // the title is in the agent's prompt, so it asks
        await app.Start();
        await app.Api("qa-all", new { project = "proj" });
        var asker = app.Agents.Agents.Single(a => a.CardId == card.Id);
        Assert.Equal(AgentPurposes.Qa, asker.Purpose);
        await app.Until(() => asker.State == AgentState.NeedsYou, "the QA agent to ask in its chat");
        File.WriteAllText(FileOf(app, card), File.ReadAllText(FileOf(app, card)).Replace("## How to check", Decision(asker.Id) + "\n## How to check"));
        Assert.Null(app.Board.MoveToAttention(card.Id));
        await app.Until(() => asker.OpenDecisions.Any(), "the card's decision to show on the asker");
        return (card, asker);
    }

    static bool AskPending(AgentViewModel agent) => agent.Items.OfType<QuestionItem>().Any(q => q.Pending);

    [AvaloniaFact]
    public async Task A_card_with_a_decision_shows_it_and_not_its_askers_question_too()
    {
        using var app = DargApp.Make();
        var (card, asker) = await QaAsksInChatToo(app);
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });

        var editor = app.Board.Editor!;
        Assert.True(editor.ShowDecision);
        Assert.NotEmpty(editor.AgentAsks); // WOR-0243: the asker's question is there, but the card's decision stands for it
        Assert.False(editor.ShowAgentAsks);
    }

    [AvaloniaFact]
    public async Task Send_back_on_the_card_settles_the_askers_own_question_and_starts_a_rework_agent()
    {
        using var app = DargApp.Make();
        var (card, asker) = await QaAsksInChatToo(app);

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Still fails", notes = "the box said no sound" });

        Assert.Equal("In Progress", app.Read(card.Id).Status);
        Assert.Contains("Does dictation work for you? I picked: Still fails. the box said no sound", File.ReadAllText(FileOf(app, card)));
        // the asker's question in its chat is settled, not left waiting on Philip, and it's told Darg has acted on it
        Assert.False(AskPending(asker), "the asker still waits on the same question in its chat");
        Assert.NotEqual(AgentState.NeedsYou, asker.State);
        Assert.DoesNotContain(asker.OpenDecisions, d => d.CardId == card.Id);
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "answered"), "the asker to be answered");
        var told = app.ClaudeCalls().First(c => (string?)c["kind"] == "answered")["response"]!.ToJsonString();
        Assert.Contains("deny", told);
        Assert.Contains("started an agent to rework it", told);
        // nothing about it is left on Needs you
        var needs = System.Text.Json.Nodes.JsonNode.Parse(await app.Api("needs-you", new { project = "proj" }))!.AsArray();
        Assert.DoesNotContain(needs, n => (string?)n!["agent"] == asker.Id);
        Assert.DoesNotContain(needs, n => (string?)n!["kind"] == "decision");
        // and an agent reworks the card from the notes, as Send back does
        var rework = Assert.Single(app.Agents.Agents, a => a.Purpose == AgentPurposes.Rework);
        Assert.Equal(card.Id, rework.CardId);
        await app.Until(() => app.Prompts().Any(p => p.Contains("the box said no sound")), "the rework agent to be sent the notes");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Approving_on_the_card_settles_the_askers_own_question()
    {
        using var app = DargApp.Make();
        var (card, asker) = await QaAsksInChatToo(app);

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Works" });

        Assert.Equal("Done", app.Read(card.Id).Status);
        Assert.False(AskPending(asker), "the asker still waits on the same question in its chat");
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "answered"), "the asker to be answered");
        Assert.Contains("done", app.ClaudeCalls().First(c => (string?)c["kind"] == "answered")["response"]!.ToJsonString());
        Assert.DoesNotContain(app.Agents.Agents, a => a.Purpose == AgentPurposes.Rework);
    }

    [AvaloniaFact]
    public async Task A_stay_on_the_card_leaves_the_askers_own_question_waiting()
    {
        using var app = DargApp.Make();
        var (card, asker) = await QaAsksInChatToo(app);

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Not tried yet" });

        Assert.Equal(BoardStore.AttentionName, app.Read(card.Id).Status);
        Assert.True(AskPending(asker));
        Assert.Equal(AgentState.NeedsYou, asker.State);
    }

    [AvaloniaFact]
    public async Task A_card_landing_with_a_decision_notifies_and_asks_nobody()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", "QA Ready", Body(Decision()));
        await app.Start();

        Assert.Null(app.Board.MoveToAttention(card.Id));
        await app.Until(() => { lock (Sandbox.Notifications) return Sandbox.Notifications.Any(n => n.AgentId == MainViewModel.CardPrefix + card.Id); }, "a notification for the card's decision");
        Notifier.Notification note;
        lock (Sandbox.Notifications) note = Sandbox.Notifications.Single(n => n.AgentId == MainViewModel.CardPrefix + card.Id);
        Assert.Equal("Does dictation work for you?", note.Detail);
        Assert.Contains("needs your decision", note.Title);
        Assert.True(note.NeedsYou, "the toast for a card's decision isn't marked as waiting on you"); // WOR-0163
        Assert.Contains(app.Main.Notifications, n => n.AgentId == MainViewModel.CardPrefix + card.Id && n.IsAsk);

        // answering it takes it off Fettle's list
        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Works" });
        await DargApp.Settle(100);
        Assert.DoesNotContain(app.Main.Notifications, n => n.AgentId == MainViewModel.CardPrefix + card.Id);
    }

    [AvaloniaFact]
    public async Task A_card_landing_without_a_decision_gets_its_agent_asked_for_one()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", "In Progress", Body());
        await app.Start();
        var agent = await WorkMovingTo(app, card, () => Assert.Null(app.Board.MoveToAttention(card.Id)));

        await app.Until(() => app.Prompts().Any(p => p.Contains("has no ## Decision section")), "the agent to be asked for a decision");
        var nudge = app.Prompts().Single(p => p.Contains("has no ## Decision section"));
        Assert.Contains(card.Id, nudge);
        Assert.Contains("**Question:**", nudge);
        Assert.DoesNotContain(app.Agents.Agents, a => a != agent); // it was asked, rather than a new agent started
        // held only for that (WOR-0183): it goes once the turn it was asked in has ended
        await app.Until(() => !app.Agents.Agents.Contains(agent), "the agent to go once the turn it was asked in ended");
    }

    [AvaloniaFact]
    public async Task To_attention_writes_the_decision_and_moves_the_card()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", "QA Ready", Body());
        await app.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("to-attention", new { project = "proj", card = card.Id, question = "Ship it?", options = new[] { "Yes → done" } }));
        await app.Api("to-attention", new { project = "proj", card = card.Id, question = "Ship it?", options = new[] { "Yes (recommended) → done: it works", "No → back" }, context = "tests pass" });

        Assert.Equal(BoardStore.AttentionName, app.Read(card.Id).Status);
        var d = CardDecision.Read(File.ReadAllText(FileOf(app, card)))!;
        Assert.True(d.Valid);
        Assert.Equal("Ship it?", d.Question);
        Assert.True(d.Options[0].Recommended);
        Assert.Equal("tests pass", d.Context);
    }
}
