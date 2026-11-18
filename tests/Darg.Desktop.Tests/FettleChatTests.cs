using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

public class FettleChatTests
{
    [AvaloniaFact] // WOR-0133: a saved chat with no session can't carry on, so a message starts a new chat instead of being lost
    public async Task A_saved_chat_with_no_session_sends_into_a_new_chat()
    {
        using var app = DargApp.Make();
        await app.Start();
        var store = AgentStore.Folder("~bench");
        AgentStore.Save(store, new AgentRecord { Id = "old-chat", Name = "Old chat", Cwd = app.Repo, Project = "proj", SessionId = null, Created = DateTime.Now, Updated = DateTime.Now });
        var mascot = app.Main.Mascot;
        mascot.ShowProject(null); // lists the chats again, now that the saved one is in the store
        mascot.SelectedChat = mascot.Chats.Single(c => c.Id == "old-chat");
        Assert.Equal("old-chat", mascot.Agent?.Id);

        mascot.Compose = "hello there";
        mascot.SendCommand.Execute(null);

        Assert.NotEqual("old-chat", mascot.Agent!.Id);
        Assert.DoesNotContain(mascot.Agent.Items.OfType<NoteItem>(), n => n.Text.Contains("no session to carry on"));
        await app.Until(() => app.Prompts().Any(p => p.Contains("hello there")), "the message to reach claude");
    }

    static void Seed(DargApp app, string id, string name, int minutesAgo)
    {
        var folder = AgentStore.Folder("~bench", app.Home);
        AgentStore.Save(folder, new AgentRecord
        {
            Id = id, Name = name, Cwd = app.Home, Project = "proj",
            Created = DateTime.Now.AddMinutes(-minutesAgo), Updated = DateTime.Now.AddMinutes(-minutesAgo),
        });
    }

    static ComboBox Dropdown(DargApp app) => app.Window.GetVisualDescendants().OfType<ComboBox>()
        .Single(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Fettle chats");

    /// <summary>Pick a chat as the person does: the ComboBox's selection, which its binding hands to Fettle.</summary>
    static async Task Pick(DargApp app, string id)
    {
        var box = Dropdown(app);
        box.SelectedItem = app.Main.Mascot.Chats.Single(c => c.Id == id);
        await DargApp.Settle();
    }

    [AvaloniaFact]
    public async Task A_chat_picked_after_closing_one_opens_first_time()
    {
        using var app = DargApp.Make();
        Seed(app, "chat-a", "Newest chat", 1);
        Seed(app, "chat-b", "Middle chat", 2);
        Seed(app, "chat-c", "Oldest chat", 3);
        await app.Start();
        var fettle = app.Main.Mascot;
        fettle.Open = true;
        await DargApp.Settle();
        Assert.Equal("chat-a", fettle.Agent?.Id); // the newest opens with the project

        fettle.ForgetCommand.Execute(null); // the × beside the dropdown
        await DargApp.Settle();
        Assert.Null(fettle.Agent);
        Assert.Equal(2, fettle.Chats.Count);

        await Pick(app, "chat-b");
        Assert.Equal("chat-b", fettle.Agent?.Id);
        Assert.Equal("chat-b", Dropdown(app).SelectedItem is ChatEntry e ? e.Id : null);

        fettle.ForgetCommand.Execute(null);
        await DargApp.Settle();
        await Pick(app, "chat-c");
        Assert.Equal("chat-c", fettle.Agent?.Id);
        Assert.Equal("", app.Crashes());
    }

    /// <summary>The Fettle chats list under the Agents view: × on one, then the next picked from the list.</summary>
    [AvaloniaFact]
    public async Task A_chat_picked_in_the_agents_list_after_forgetting_one_opens_first_time()
    {
        using var app = DargApp.Make();
        Seed(app, "chat-a", "Newest chat", 1);
        Seed(app, "chat-b", "Middle chat", 2);
        Seed(app, "chat-c", "Oldest chat", 3);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Agents" });
        var list = app.Window.GetVisualDescendants().OfType<ListBox>().Single(l => l.ItemsSource == app.Main.Mascot.Chats);
        var fettle = app.Main.Mascot;

        async Task PickFromList(string id)
        {
            list.SelectedItem = fettle.Chats.Single(c => c.Id == id);
            await DargApp.Settle();
        }

        await PickFromList("chat-a");
        Assert.Equal("chat-a", app.Agents.Shown?.Id);
        app.Agents.ForgetChatCommand.Execute(fettle.Chats.Single(c => c.Id == "chat-a"));
        await DargApp.Settle();
        Assert.DoesNotContain(fettle.Chats, c => c.Id == "chat-a");

        await PickFromList("chat-b");
        Assert.Equal("chat-b", app.Agents.Shown?.Id);
        app.Agents.ForgetChatCommand.Execute(fettle.Chats.Single(c => c.Id == "chat-b"));
        await DargApp.Settle();
        await PickFromList("chat-c");
        Assert.Equal("chat-c", app.Agents.Shown?.Id);
        Assert.Equal("", app.Crashes());
    }

    /// <summary>A real pointer click on an item of the open dropdown picks it first time. The header the dropdown sits in
    /// is a drag handle, and a press on the popup's items (which aren't under the ComboBox in the visual tree) began a drag
    /// that captured the pointer and swallowed the click (WOR-0130).</summary>
    [AvaloniaFact]
    public async Task Clicking_a_chat_in_the_open_dropdown_picks_it_first_click()
    {
        using var app = DargApp.Make();
        Seed(app, "chat-a", "Newest chat", 1);
        Seed(app, "chat-b", "Middle chat", 2);
        Seed(app, "chat-c", "Oldest chat", 3);
        await app.Start();
        var fettle = app.Main.Mascot;
        fettle.Open = true;
        await DargApp.Settle();

        static void Click(Avalonia.Visual c)
        {
            var top = Avalonia.Controls.TopLevel.GetTopLevel(c)!;
            var at = c.TranslatePoint(new Avalonia.Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, at);
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, at, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, at, Avalonia.Input.MouseButton.Left);
        }

        var box = Dropdown(app);
        Click(box);
        await DargApp.Settle();
        Assert.True(box.IsDropDownOpen);
        Click(app.Window.GetVisualDescendants().OfType<ComboBoxItem>().First(i => i.DataContext is ChatEntry e && e.Id == "chat-b"));
        await DargApp.Settle();
        Assert.Equal("chat-b", fettle.Agent?.Id);
        Assert.False(box.IsDropDownOpen);
    }

    [AvaloniaFact] // WOR-0130: the drag handle is the part above the dropdown; the dropdown's row neither shows the move cursor nor moves the box
    public async Task The_chats_dropdown_is_no_part_of_the_drag_handle()
    {
        using var app = DargApp.Make();
        Seed(app, "chat-a", "Newest chat", 1);
        Seed(app, "chat-b", "Middle chat", 2);
        await app.Start();
        var fettle = app.Main.Mascot;
        fettle.Open = true;
        await DargApp.Settle();
        var box = Dropdown(app);
        box.SelectedItem = fettle.Chats.First(); // a chat open, so + New chat and × show too
        await DargApp.Settle();

        app.Shot("fettle-open");
        var header = app.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MascotHeader");
        var move = header.Cursor;
        Assert.NotNull(move); // the header itself still says it can be dragged
        Assert.NotSame(move, box.Cursor);
        Assert.DoesNotContain(header, box.GetVisualAncestors());
        // nothing in the dropdown's row carries the move cursor: the row itself (its blank parts) or the × beside it
        var row = box.GetVisualAncestors().OfType<Border>().First(b => b.Parent == header.Parent);
        Assert.All(row.GetSelfAndVisualDescendants().OfType<Avalonia.Input.InputElement>(), e => Assert.NotSame(move, e.Cursor));
        Assert.True(row.Bounds.Top >= header.Bounds.Bottom, "the dropdown's row sits below the handle");

        // a drag that starts on the dropdown's row (its blank edge, left of the dropdown) leaves the box where it is
        var bubble = app.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MascotBubble");
        void Drag(Avalonia.Point from)
        {
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(app.Window, from);
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(app.Window, from, Avalonia.Input.MouseButton.Left);
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(app.Window, from + new Avalonia.Point(-60, -40));
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(app.Window, from + new Avalonia.Point(-60, -40), Avalonia.Input.MouseButton.Left);
        }
        var shift = (Avalonia.Media.TranslateTransform)bubble.RenderTransform!;
        Drag(row.TranslatePoint(new Avalonia.Point(-6, row.Bounds.Height / 2), app.Window)!.Value);
        await DargApp.Settle();
        Assert.Equal((0d, 0d), (shift.X, shift.Y));
        // and one that starts on the handle's blank part still moves it
        Drag(header.TranslatePoint(new Avalonia.Point(header.Bounds.Width / 2, 4), app.Window)!.Value);
        await DargApp.Settle();
        Assert.Equal((-60d, -40d), (shift.X, shift.Y));
        shift.X = shift.Y = 0;

        var buttons = header.GetVisualDescendants().OfType<Button>().ToList();
        Assert.True(buttons.Count >= 4, $"the header's buttons: {buttons.Count}");
        Assert.All(buttons, b => Assert.NotSame(move, b.Cursor));
        Assert.Null(ToolTip.GetTip(header)); // on the header it showed over the dropdown and the buttons as well

        box.IsDropDownOpen = true;
        await DargApp.Settle();
        var items = app.Window.GetVisualDescendants().OfType<ComboBoxItem>().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.NotSame(move, i.Cursor));
    }
}
