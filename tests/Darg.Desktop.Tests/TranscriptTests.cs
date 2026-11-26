using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0230: an agent's transcript is virtualised, so showing an agent that has run a long time builds
/// only the entries on screen (it built all of them, 1.6 to 2.7 s a switch at 500 entries), and it still opens
/// at the newest entry, follows new ones, lets the person scroll up through the history, and folds tool steps
/// in Fettle.</summary>
public class TranscriptTests
{
    const int Long = 500;
    const int Screenful = 60; // a 900 px window shows a dozen or so entries; the panel builds a little either side

    static List<ItemRecord> Transcript(string who) => Enumerable.Range(0, Long).Select(i => (i % 4) switch
    {
        0 => new ItemRecord { Kind = "user", Text = $"{who}: message {i}" },
        1 => new ItemRecord { Kind = "text", Text = $"Reply {i} from {who}.\n\n- one point\n- another point\n\nAnd a closing line." },
        2 => new ItemRecord { Kind = "tool", Name = "Read", Summary = $"src/File{i}.cs", Result = string.Join("\n", Enumerable.Range(0, 8).Select(n => $"line {n} of the file")) },
        _ => new ItemRecord { Kind = "note", Text = $"note {i}" },
    }).ToList();

    static void Seed(string folder, string id, string name, string cwd) => AgentStore.Save(folder, new AgentRecord
    {
        Id = id, Name = name, Cwd = cwd, Project = "proj", Purpose = AgentPurposes.Chat, SessionId = $"session-{id}",
        Created = DateTime.Now.AddMinutes(-30), Updated = DateTime.Now.AddMinutes(-30), Items = Transcript(name),
    });

    static ItemsControl Named(DargApp app, string name) => app.Window.GetVisualDescendants().OfType<ItemsControl>()
        .Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);

    static ScrollViewer ScrollOf(ItemsControl list) => list.GetVisualAncestors().OfType<ScrollViewer>().First();

    static int Built(ItemsControl list) => list.GetRealizedContainers().Count();

    static bool AtBottom(ScrollViewer sv) => sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 2;

    static bool Shows(ItemsControl list, int index) => list.ContainerFromIndex(index) is Control c && c.IsEffectivelyVisible
        && c.TranslatePoint(new Point(0, 0), ScrollOf(list)) is { } p && p.Y < ScrollOf(list).Viewport.Height && p.Y + c.Bounds.Height > 0;

    [AvaloniaFact]
    public async Task Switching_agents_builds_only_a_screenful_and_opens_at_the_newest_entry()
    {
        using var app = DargApp.Make();
        var store = AgentStore.Folder("proj", app.Home);
        Seed(store, "longone", "First long chat", app.Repo);
        Seed(store, "longtwo", "Second long chat", app.Repo);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Agents" });

        foreach (var id in new[] { "longone", "longtwo", "longone" })
        {
            await app.Api("show", new { project = "proj", agent = id });
            await DargApp.Settle(200);
            var list = Named(app, "Transcript");
            await app.Until(() => AtBottom(ScrollOf(list)) && Shows(list, Long - 1), $"{id}'s newest entry to show");
            Assert.Equal(Long, list.ItemCount);
            Assert.InRange(Built(list), 1, Screenful);
        }
        app.Shot("long-transcript");

        // working: a new entry is followed
        var agent = app.Agents.Find("longone")!;
        var transcript = Named(app, "Transcript");
        var sv = ScrollOf(transcript);
        agent.Items.Add(new NoteItem("a new line while it works", false));
        await app.Until(() => AtBottom(sv) && Shows(transcript, Long), "the new entry to be followed");

        // scrolled up to read: the top of the history builds and shows, and a new entry doesn't pull the view down
        sv.Offset = new Vector(0, 0);
        await DargApp.Settle(200);
        await app.Until(() => Shows(transcript, 0), "the first entry to show at the top");
        Assert.InRange(Built(transcript), 1, Screenful);
        agent.Items.Add(new NoteItem("another line", false));
        await DargApp.Settle(200);
        Assert.True(Shows(transcript, 0), "still reading the top after a new entry");
        Assert.False(AtBottom(sv));

        // halfway down, scrolling up a step at a time keeps going up (measuring entries doesn't pull it back)
        sv.Offset = new Vector(0, sv.Extent.Height / 2);
        await DargApp.Settle(200);
        for (var step = 0; step < 10; step++)
        {
            var before = sv.Offset.Y;
            sv.Offset = new Vector(0, Math.Max(0, before - 120));
            await DargApp.Settle(50);
            Assert.True(sv.Offset.Y < before, $"step {step}: {before} went to {sv.Offset.Y}");
        }

        // back at the bottom it follows again
        sv.ScrollToEnd();
        await DargApp.Settle(200);
        agent.Items.Add(new NoteItem("and another", false));
        await app.Until(() => AtBottom(sv) && Shows(transcript, transcript.ItemCount - 1), "following again from the bottom");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Fettle_shows_a_long_chat_a_screenful_at_a_time_and_its_steps_still_fold()
    {
        using var app = DargApp.Make();
        Seed(AgentStore.Folder("~bench", app.Home), "fettlelong", "A long Fettle chat", app.Home);
        await app.Start();
        var fettle = app.Main.Mascot;
        fettle.ShowProject(null);
        fettle.Open = true;
        fettle.SelectedChat = fettle.Chats.Single(c => c.Id == "fettlelong");
        await DargApp.Settle(200);

        var list = Named(app, "Fettle transcript");
        var sv = ScrollOf(list);
        await app.Until(() => AtBottom(sv) && Shows(list, Long - 1), "Fettle's newest entry to show");
        Assert.Equal(Long, list.ItemCount);
        Assert.InRange(Built(list), 1, Screenful);

        // the newest tool step (entry 498) is one folded line; a click opens it and it follows the growth
        var step = (ToolItem)fettle.Items[Long - 2];
        var box = list.ContainerFromIndex(Long - 2)!;
        var folded = box.Bounds.Height;
        step.ToggleExpandedCommand.Execute(null);
        await DargApp.Settle(200);
        Assert.True(list.ContainerFromIndex(Long - 2)!.Bounds.Height > folded + 40, "the step opened");
        step.ToggleExpandedCommand.Execute(null);
        await DargApp.Settle(200);
        Assert.Equal(folded, list.ContainerFromIndex(Long - 2)!.Bounds.Height, 1);
        app.Shot("fettle-long-chat");
        Assert.Equal("", app.Crashes());
    }
}
