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
}
