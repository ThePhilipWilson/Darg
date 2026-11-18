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
}
