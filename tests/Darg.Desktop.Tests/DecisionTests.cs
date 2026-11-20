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
    public async Task An_answer_of_my_own_goes_to_the_agent_that_asked_when_it_is_listed()
    {
        using var app = DargApp.Make();
        var card = app.Card("voice mode", "In Progress", Body());
        await app.Start();
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var asker = app.Agents.ForCard(card.Id)!;
        await app.Until(() => asker.State == AgentState.Idle, "the card agent's turn to end");
        // it writes its decision and moves the card, as its rules say
        File.WriteAllText(FileOf(app, card), File.ReadAllText(FileOf(app, card)).Replace("## How to check", Decision(asker.Id) + "\n## How to check"));
        Assert.Null(app.Board.MoveToAttention(card.Id));
        await DargApp.Settle(300);

        await app.Api("decide", new { project = "proj", card = card.Id, answer = "Use Whisper instead", notes = "the small model" });

        await app.Until(() => app.Prompts().Any(p => p.Contains("decision: you asked")), "the answer to reach the agent that asked");
        Assert.Single(app.Agents.Agents); // no second agent
        var said = app.Prompts().Single(p => p.Contains("decision: you asked"));
        Assert.Contains("I picked: Use Whisper instead", said);
        Assert.Contains("Notes: the small model", said);
        Assert.Equal("In Progress", app.Read(card.Id).Status);
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
        await app.Api("work-on", new { project = "proj", card = card.Id });
        var agent = app.Agents.ForCard(card.Id)!;
        await app.Until(() => agent.State == AgentState.Idle, "the card agent's turn to end");

        Assert.Null(app.Board.MoveToAttention(card.Id));

        await app.Until(() => app.Prompts().Any(p => p.Contains("has no ## Decision section")), "the agent to be asked for a decision");
        var nudge = app.Prompts().Single(p => p.Contains("has no ## Decision section"));
        Assert.Contains(card.Id, nudge);
        Assert.Contains("**Question:**", nudge);
        Assert.Single(app.Agents.Agents);
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
