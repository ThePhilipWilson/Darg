using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Darg.Core;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0228: reading the reports folder held the window seconds at a time once it had hundreds of reports. Starting
/// work on a card with blockers opens only those blockers' reports, and the Reports list reads off the UI thread, opening
/// only the files that changed.</summary>
public class ReportReadingTests
{
    sealed class Opens : IDisposable
    {
        readonly string _folder;
        public readonly ConcurrentQueue<(string File, bool OnUi)> Seen = new();
        public Opens(string folder) { _folder = Path.GetFullPath(folder); ReportCache.Opened += Add; }
        void Add(string file)
        {
            if (Path.GetFullPath(file).StartsWith(_folder, StringComparison.OrdinalIgnoreCase))
                Seen.Enqueue((Path.GetFileName(file), Dispatcher.UIThread.CheckAccess()));
        }
        public void Clear() => Seen.Clear();
        public void Dispose() => ReportCache.Opened -= Add;
    }

    static void Write(string folder, string name, string card, DateTime at) =>
        File.WriteAllText(Path.Combine(folder, name), $"---\ncard: {card}\ntitle: {card} work\nagent: 0000000000\ncommits: \n" +
                                                     $"date: {at:yyyy-MM-dd HH:mm}\nread: true\n---\n\n## Asked\n\nIt.\n\n## Done\n\nDone.\n\n## Left\n\nNone\n");

    /// <summary>400 reports of cards no board here has, as a project that has run a while has.</summary>
    static void Fill(string folder)
    {
        Directory.CreateDirectory(folder);
        var archive = Directory.CreateDirectory(ReportArchive.Folder(folder)).FullName;
        for (var i = 0; i < 400; i++)
            Write(i % 5 == 0 ? archive : folder, $"202611{10 + i / 40:00}-{i % 24:00}{i % 60:00}-OLD-{i:0000}.md", $"OLD-{i:0000}", new DateTime(2026, 11, 10 + i / 40, i % 24, i % 60, 0));
    }

    [AvaloniaFact]
    public async Task Work_on_a_card_with_blockers_among_400_reports_opens_only_the_blockers_reports_on_the_UI_thread()
    {
        using var app = DargApp.Make();
        var scaffold = app.Card("the scaffold", "QA Ready");
        var api = app.Card("the API", "QA Ready");
        var phone = app.Card("pairing on the phone");
        var file = Path.Combine(app.BoardFolder, "items", phone.File);
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        var end = text.IndexOf('\n', text.IndexOf("- **Labels:**", StringComparison.Ordinal));
        File.WriteAllText(file, text.Insert(end, $"\n- **Blocked by:** {scaffold.Id}, {api.Id}"));
        await app.Start();

        var folder = app.Project.Reports!.Folder;
        Fill(folder);
        var older = $"{DateTime.Now.AddMinutes(-9):yyyyMMdd-HHmm}-{scaffold.Id}.md";
        Write(folder, older, scaffold.Id, DateTime.Now.AddMinutes(-9));
        var newest = Path.Combine(ReportArchive.Folder(folder), $"{DateTime.Now.AddMinutes(-5):yyyyMMdd-HHmm}-{scaffold.Id}-qa.md");
        Write(ReportArchive.Folder(folder), Path.GetFileName(newest), scaffold.Id, DateTime.Now.AddMinutes(-5));
        // the folder watcher's reload of all that has been and gone, off the UI thread
        await DargApp.Settle(600);
        await app.Project.Reports.ReloadAsync();

        using var opens = new Opens(folder);
        // a cold cache, as when the app has just started
        foreach (var f in Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(-30));
        opens.Clear();
        var watch = Stopwatch.StartNew();
        await app.Api("work-on", new { project = "proj", card = phone.Id, force = true });
        watch.Stop();
        await app.Until(() => app.Prompts().Any(p => p.Contains(phone.Id)), "the card agent's prompt");

        var prompt = app.Prompts().First(p => p.Contains(phone.Id));
        Assert.Contains($"- {scaffold.Id}: the scaffold (QA Ready, cleared). Its newest report: {Path.GetFullPath(newest).Replace('\\', '/')}", prompt);
        Assert.Contains($"- {api.Id}: the API (QA Ready, cleared). It has no report yet.", prompt);
        var onUi = opens.Seen.Where(o => o.OnUi).Select(o => o.File).Distinct().Order(StringComparer.Ordinal).ToList();
        // the blocker's two reports and nothing else: before WOR-0228 every report in the folder and its archive was read here
        Assert.Equal([Path.GetFileName(newest), older], onUi.OrderByDescending(f => f.EndsWith("-qa.md")));
        // the command runs on the UI thread, so this is how long it held the window
        Assert.True(watch.ElapsedMilliseconds < 200, $"work-on held the UI {watch.ElapsedMilliseconds} ms");
    }

    [AvaloniaFact]
    public async Task The_reports_list_reads_off_the_UI_thread_and_opens_only_what_changed()
    {
        using var app = DargApp.Make();
        await app.Start();
        var reports = app.Project.Reports!;
        Fill(reports.Folder);
        await DargApp.Settle(600);
        await reports.ReloadAsync();
        Assert.Contains("320 reports", reports.Status);

        using var opens = new Opens(reports.Folder);
        Write(reports.Folder, "20261126-1000-NEW-0001.md", "NEW-0001", new DateTime(2026, 11, 26, 10, 0, 0));
        ReportStore.SetRead(Path.Combine(reports.Folder, "20261110-0101-OLD-0001.md"), false);
        await reports.ReloadAsync();

        Assert.Contains(reports.Reports, r => r.Report.Card == "NEW-0001");
        Assert.Contains("321 reports, 1 unread", reports.Status);
        Assert.Equal(["20261110-0101-OLD-0001.md", "20261126-1000-NEW-0001.md"], opens.Seen.Select(o => o.File).Distinct().Order(StringComparer.Ordinal));
        Assert.DoesNotContain(opens.Seen, o => o.OnUi);
    }
}
