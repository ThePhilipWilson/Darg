using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0158: Fettle's reply about a card it made links the card, and a click on the link opens the card's
/// panel. A [ID](card:ID) link does, and so does an ID written bare (Fettle forgot the link); new-card hands Fettle
/// the link ready-made and its briefing says to put it in the reply.</summary>
public class CardLinkTests
{
    static void Seed(DargApp app, string reply)
    {
        AgentStore.Save(AgentStore.Folder("~bench", app.Home), new AgentRecord
        {
            Id = "chat-card", Name = "Card chat", Cwd = app.Home, Project = "proj", Created = DateTime.Now, Updated = DateTime.Now,
            Items = [new ItemRecord { Kind = "user", Text = "make a card for the CSV export" }, new ItemRecord { Kind = "text", Text = reply }],
        });
    }

    /// <summary>The links drawn in Fettle's box that open a card, by the card they open.</summary>
    static List<TextBlock> CardLinks(DargApp app, string id) => app.Window.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => ToolTip.GetTip(t) as string == $"Open {id}" && t.IsEffectivelyVisible).ToList();

    static void Click(Avalonia.Visual c)
    {
        var top = TopLevel.GetTopLevel(c)!;
        var at = c.TranslatePoint(new Avalonia.Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, at);
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, at, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, at, Avalonia.Input.MouseButton.Left);
    }

    [AvaloniaTheory]
    [InlineData("Made [{0}](card:{0}): CSV export. None of it on fire.")] // as briefed
    [InlineData("Made {0}: CSV export.")] // the ID bare: still a link
    [InlineData("Made **{0}**, CSV export.")] // bold
    public async Task A_card_named_in_Fettles_reply_opens_its_panel_when_clicked(string reply)
    {
        using var app = DargApp.Make();
        var card = app.Card("Export the board as CSV");
        Seed(app, string.Format(reply, card.Id));
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Home" });
        var fettle = app.Main.Mascot;
        fettle.ShowProject(null); // lists the chats again, now that the saved one is in the store
        fettle.SelectedChat = fettle.Chats.Single(c => c.Id == "chat-card");
        fettle.Open = true;
        await DargApp.Settle(200);

        var links = CardLinks(app, card.Id);
        app.Shot("fettle-reply");
        Assert.Single(links);
        Assert.Null(app.Board.Editor);
        Click(links[0]);
        await DargApp.Settle();

        Assert.Equal(Section.Board, app.Project.Section);
        Assert.Equal(card.Id, app.Board.Editor?.Heading);
        app.Shot("card-open");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task New_card_answers_with_a_link_and_Fettle_is_told_to_reply_with_it()
    {
        using var app = DargApp.Make();
        await app.Start();
        var reply = JsonNode.Parse(await app.Api("new-card", new { project = "proj", title = "Export the board as CSV" }))!.AsObject();
        Assert.Equal("[T-0001](card:T-0001)", (string?)reply["link"]);

        var fettle = app.Main.Mascot;
        fettle.Compose = "make a card for the CSV export";
        fettle.SendCommand.Execute(null);
        await app.Until(() => app.ClaudeCalls().Any(c => (string?)c["kind"] == "start"), "Fettle's process to start");
        var args = app.ClaudeCalls().First(c => (string?)c["kind"] == "start")["args"]!.AsArray().Select(a => (string)a!).ToList();
        var briefing = File.ReadAllText(args[args.IndexOf("--append-system-prompt-file") + 1]);
        Assert.Contains("(card:", briefing);
        Assert.Contains("link field", briefing);
    }
}
