using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0231: the Artifacts and Notes sections read their folders off the UI thread when a file watcher
/// fires (they held the window for 35 s and 29 s over four days), and the notes wall reads only its tickets' cards,
/// and only when one of them changes. <see cref="FileReads"/> says on which thread each folder read ran.</summary>
public class OffThreadReloadTests
{
    [AvaloniaFact]
    public async Task Artifacts_read_their_folder_off_the_UI_thread_and_Home_reads_none()
    {
        using var app = DargApp.Make();
        await app.Start();
        var artifacts = app.Project.Artifacts!;
        var reads = Watch("artifacts", artifacts.Folder);
        try
        {
            // an agent writes a page: the watcher fires and the section reads the folder again
            var page = Path.Combine(Directory.CreateDirectory(Path.Combine(artifacts.Folder, "agent1")).FullName, "outfit.html");
            File.WriteAllText(page, "<html><head><title>Outfit round 3</title></head><body>hi</body></html>");
            await app.Until(() => artifacts.Groups.SelectMany(g => g.Children).Any(e => e.Title == "Outfit round 3"), "the new page listed in Artifacts");
            // Home, on screen, shows it from the section's read on its next glance
            await app.Until(() => app.Project.Home.Artifacts.Any(a => a.Title == "Outfit round 3"), "the new page on Home's tile");
            // opening the section and the page reads again, also off the UI thread
            app.Project.OpenCommand.Execute("Artifacts");
            Assert.True(await artifacts.Show(page));
            Assert.Equal("Outfit round 3", artifacts.Selected?.Title);

            Assert.NotEmpty(reads);
            Assert.DoesNotContain(reads, r => r.OnUi);
        }
        finally { FileReads.Reading -= reads.Handler; }
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task Notes_wall_reads_off_the_UI_thread_and_only_for_a_change_to_its_tickets()
    {
        using var app = DargApp.Make();
        var ticketed = app.Card("the logo is blurry");
        var other = app.Card("unrelated work");
        await app.Start();
        var notes = app.Project.Notes!;
        NoteStore.Save(notes.Folder, new Note(NoteStore.NewId(), "the logo is blurry", "Loose", "yellow", [], "", ticketed.Id, ""));
        await app.Until(() => notes.Columns.Any(c => c.Name == NoteStore.Ticketed && c.Notes.Count == 1), "the note in Ticketed");

        var reads = Watch(null, notes.Folder, NoteStore.ArchiveFolder(notes.Folder), app.Boards);
        try
        {
            // a card no note is ticketed to moves: nothing on the wall can change, so nothing is read
            BoardStore.Move(app.BoardFolder, other.Id, app.ColumnId("In Progress"));
            for (var i = 0; i < 10; i++) await DargApp.Settle(100); // past the watcher's 300 ms wait
            Assert.DoesNotContain(reads, r => r.What == "ticket statuses");

            // the note's ticket is done: the wall reads (off the UI thread) and the note goes to the archive
            BoardStore.Move(app.BoardFolder, ticketed.Id, app.ColumnId("Done"));
            await app.Until(() => notes.HasArchive && !notes.Columns.Any(c => c.Name == NoteStore.Ticketed), "the note archived");
            Assert.Contains(reads, r => r.What == "ticket statuses");
            Assert.DoesNotContain(reads, r => r.OnUi);
            Assert.Single(Directory.GetFiles(NoteStore.ArchiveFolder(notes.Folder), "*.md"));
        }
        finally { FileReads.Reading -= reads.Handler; }
        Assert.Equal("", app.Crashes());
    }

    /// <summary>Every folder read of this kind (any, when null) in these folders from now on, with whether it ran on the UI thread.</summary>
    static Reads Watch(string? what, params string[] folders)
    {
        var reads = new Reads();
        var full = folders.Select(f => Path.GetFullPath(f).TrimEnd('\\', '/')).ToList();
        reads.Handler = (w, folder) =>
        {
            if (what is not null && w != what) return;
            if (!full.Contains(Path.GetFullPath(folder).TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase)) return;
            reads.Enqueue((w, Dispatcher.UIThread.CheckAccess()));
        };
        FileReads.Reading += reads.Handler;
        return reads;
    }

    sealed class Reads : ConcurrentQueue<(string What, bool OnUi)>
    {
        public Action<string, string> Handler = (_, _) => { };
    }
}
