using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0173: a card blocked by one not yet in QA or done doesn't start by itself. ▶ Work All leaves it out and says
/// so, Work on it asks first (tile and panel), the API's work-on refuses it without force, and a card agent's prompt names
/// its blockers with their newest reports.</summary>
public class BlockedStartTests
{
    static ListViewModel Column(DargApp app, string id) => app.Board.Lists.Single(l => l.Id == id);

    /// <summary>Whether the window draws <paramref name="text"/> whole: some visible text block holds it with no line cut
    /// short by an ellipsis (WOR-0173 QA: the header trimmed the status at 260 px, so the left-out cards never showed).</summary>
    static bool DrawnWhole(DargApp app, string text) =>
        app.Window.GetVisualDescendants().OfType<TextBlock>().Any(t =>
            t.IsEffectivelyVisible && t.Bounds.Width > 0 && (t.Text ?? "").Contains(text, StringComparison.Ordinal)
            && !t.TextLayout.TextLines.Any(l => l.HasCollapsed));

    /// <summary>Give a card a Blocked by line among its properties, as WOR-0172's panel writes it.</summary>
    static void BlockedBy(DargApp app, Card card, params string[] ids)
    {
        var file = Path.Combine(app.BoardFolder, "items", card.File);
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        var at = text.IndexOf("- **Labels:**", StringComparison.Ordinal);
        var end = text.IndexOf('\n', at);
        File.WriteAllText(file, text.Insert(end, $"\n- **Blocked by:** {string.Join(", ", ids)}"));
    }

    static string Report(DargApp app, Card card)
    {
        var folder = app.Project.Reports!.Folder;
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{DateTime.Now.AddMinutes(-5):yyyyMMdd-HHmm}-{card.Id}.md");
        File.WriteAllText(file, $"---\ncard: {card.Id}\ntitle: {card.Title}\nagent: 0000000000\ncommits: \nrepo: {app.Repo}\n" +
                                $"date: {DateTime.Now.AddMinutes(-5):yyyy-MM-dd HH:mm}\nread: false\n---\n\n## Asked\n\nThe scaffold.\n\n## Done\n\nMade it.\n\n## Left\n\nNone\n");
        return file;
    }

    [AvaloniaFact]
    public async Task Work_All_starts_the_free_cards_leaves_the_blocked_one_and_says_so()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var scaffold = app.Card("the scaffold", "In Progress");
            var free = app.Card("free to start");
            var blocked = app.Card("needs the scaffold");
            var cleared = app.Card("needs a card already in QA");
            var inQa = app.Card("already checked in", "QA Ready");
            BlockedBy(app, blocked, scaffold.Id);
            BlockedBy(app, cleared, inQa.Id);
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });
            await DargApp.Settle(300);

            var backlog = Column(app, Board.Backlog);
            Assert.Equal("▶ Work All (3, 1 blocked)", backlog.BulkText);
            backlog.BulkCommand.Execute(null); // three: no asking
            await DargApp.Settle(300);

            Assert.Single(app.Agents.Agents, a => a.CardId == free.Id);
            Assert.Single(app.Agents.Agents, a => a.CardId == scaffold.Id);
            Assert.Single(app.Agents.Agents, a => a.CardId == cleared.Id); // its blocker is in QA: cleared
            Assert.DoesNotContain(app.Agents.Agents, a => a.CardId == blocked.Id);
            Assert.Equal("Backlog", app.Read(blocked.Id).Status);
            Assert.Contains($"left out 1 blocked: {blocked.Id} (waits on {scaffold.Id})", app.Board.Status);
            Assert.Equal("▶ Work All (0, 1 blocked)", Column(app, Board.Backlog).BulkText);
            // and the screen shows it whole, the cards left out and what they wait on, not cut off in the header
            Assert.True(DrawnWhole(app, $"left out 1 blocked: {blocked.Id} (waits on {scaffold.Id})"), $"drawn whole: {app.Board.Status}");
            app.Shot("work-all-notice");
            app.Board.DismissNoticeCommand.Execute(null);
            await DargApp.Settle(100);
            Assert.False(app.Board.ShowsNotice);
            Assert.False(DrawnWhole(app, "left out"));

            // the API says the same, and starts it with force
            Assert.Contains($"Nothing to start: left out 1 blocked: {blocked.Id}", await app.Api("work-all", new { project = "proj" }));
            Assert.Contains($"Started work on {blocked.Id}", await app.Api("work-all", new { project = "proj", force = true }));
            Assert.Single(app.Agents.Agents, a => a.CardId == blocked.Id);
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task Work_on_a_blocked_card_asks_on_its_tile_and_in_its_panel_and_the_API_refuses_it_without_force()
    {
        Environment.SetEnvironmentVariable("FAKE_SECONDS", "30");
        try
        {
            using var app = DargApp.Make();
            var scaffold = app.Card("the scaffold", "In Progress");
            var blocked = app.Card("needs the scaffold");
            BlockedBy(app, blocked, scaffold.Id);
            await app.Start();
            await app.Api("show", new { project = "proj", section = "Board" });
            await DargApp.Settle(300);

            // the tile asks, and nothing starts
            CardViewModel Tile() => app.Board.Lists.SelectMany(l => l.Cards).Single(c => c.Id == blocked.Id);
            Tile().WorkOnCommand.Execute(null);
            await DargApp.Settle(100);
            Assert.Equal($"Blocked by {scaffold.Id}, In Progress. Start anyway?", Tile().BlockedAsk);
            Assert.Empty(app.Agents.Agents);
            app.Shot("tile-asks");
            Tile().CancelBlockedCommand.Execute(null);
            Assert.False(Tile().AskingBlocked);

            // so does the panel
            app.Board.Open(Tile());
            var panel = app.Board.Editor!;
            panel.WorkOnCommand.Execute(null);
            await DargApp.Settle(100);
            Assert.Equal($"Blocked by {scaffold.Id}, In Progress. Start anyway?", panel.BlockedAsk);
            Assert.Empty(app.Agents.Agents);
            app.Shot("panel-asks");
            panel.CancelBlockedCommand.Execute(null);

            // the API refuses it, naming the blocker
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("work-on", new { project = "proj", card = blocked.Id }));
            Assert.Contains($"{blocked.Id} is blocked by {scaffold.Id}, In Progress", refused.Message);
            Assert.Contains("force: true", refused.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Api("work-on", new { project = "proj", cards = blocked.Id }));
            Assert.Empty(app.Agents.Agents);

            // Start anyway on the tile starts it
            Tile().WorkOnCommand.Execute(null);
            Tile().StartAnywayCommand.Execute(null);
            await app.Until(() => app.Agents.Agents.Any(a => a.CardId == blocked.Id), "the blocked card's agent started anyway");
            Assert.Equal("In Progress", app.Read(blocked.Id).Status);

            // once its blocker is in QA it starts without asking
            var other = app.Card("also needs the scaffold");
            BlockedBy(app, other, scaffold.Id);
            BoardStore.Move(app.BoardFolder, scaffold.Id, app.ColumnId("QA Ready"));
            Assert.Contains("Started", await app.Api("work-on", new { project = "proj", card = other.Id }));
            Assert.Equal("", app.Crashes());
        }
        finally { Environment.SetEnvironmentVariable("FAKE_SECONDS", "1"); }
    }

    [AvaloniaFact]
    public async Task A_card_agents_prompt_names_its_blockers_with_their_columns_and_newest_reports()
    {
        using var app = DargApp.Make();
        var scaffold = app.Card("the scaffold", "QA Ready");
        var api = app.Card("the API", "In Progress");
        var phone = app.Card("pairing on the phone");
        BlockedBy(app, phone, scaffold.Id, api.Id);
        await app.Start();
        var report = Path.GetFullPath(Report(app, scaffold)).Replace('\\', '/');
        await app.Api("work-on", new { project = "proj", card = phone.Id, force = true });
        await app.Until(() => app.Prompts().Any(p => p.Contains(phone.Id)), "the card agent's prompt");

        var prompt = app.Prompts().First(p => p.Contains(phone.Id));
        Assert.Contains("This card is blocked by", prompt);
        Assert.Contains($"- {scaffold.Id}: the scaffold (QA Ready, cleared). Its newest report: {report}", prompt);
        Assert.Contains($"- {api.Id}: the API (In Progress, not finished yet). It has no report yet.", prompt);
        Assert.Contains("it was started anyway", prompt);

        // a card with no blockers gets none of it
        var plain = app.Card("nothing in the way");
        await app.Api("work-on", new { project = "proj", card = plain.Id });
        await app.Until(() => app.Prompts().Any(p => p.Contains(plain.Id)), "the plain card's prompt");
        Assert.DoesNotContain("This card is blocked by", app.Prompts().First(p => p.Contains(plain.Id)));
    }
}
