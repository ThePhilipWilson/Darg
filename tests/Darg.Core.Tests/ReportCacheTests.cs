using System.Collections.Concurrent;
using System.Diagnostics;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0228: a card's reports are found from the file names, not by reading every report in the folder, and a
/// folder listed again opens only the reports that changed.</summary>
public sealed class ReportCacheTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "darg-reportcache-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _reports;
    readonly ConcurrentBag<string> _opened = [];

    public ReportCacheTests()
    {
        _reports = Directory.CreateDirectory(Path.Combine(_root, "reports")).FullName;
        ReportCache.Opened += Seen;
    }

    // other tests list their own folders meanwhile: keep only this one's
    void Seen(string file) { if (file.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) _opened.Add(Path.GetFullPath(file)); }

    public void Dispose()
    {
        ReportCache.Opened -= Seen;
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    static void Write(string file, string card, string date, string extra = "") =>
        File.WriteAllText(file, $"---\n{extra}card: {card}\ntitle: {card} work\ndate: {date}\nread: false\n---\n\n## Done\n\nIt.\n");

    /// <summary>What ReportsFor gave before WOR-0228: every report in the folder, its archive and each story's, parsed, then filtered.</summary>
    static List<string> Before(string reports, string card)
    {
        IEnumerable<AgentReport> In(string dir, string story) => !Directory.Exists(dir) ? []
            : Directory.GetFiles(dir, "*.md").Select(f => ReportStore.Parse(File.ReadAllText(f), f) with { Story = story });
        var archive = ReportArchive.Folder(reports);
        var all = In(reports, "").Concat(In(archive, ""));
        if (Directory.Exists(archive)) all = all.Concat(Directory.GetDirectories(archive).SelectMany(d => In(d, Path.GetFileName(d))));
        return all.Where(r => r.Card.Equals(card, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date, StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .Select(r => $"{r.Id}|{r.Story}|{r.Qa}").ToList();
    }

    /// <summary>A folder like this project's: 400 reports across many cards, some archived, some in stories.</summary>
    void Fill()
    {
        var archive = Directory.CreateDirectory(ReportArchive.Folder(_reports)).FullName;
        var story = Directory.CreateDirectory(Path.Combine(archive, "Logo")).FullName;
        for (var i = 0; i < 400; i++)
        {
            var card = $"WOR-{i % 80:0000}";
            var stamp = $"202611{10 + i / 40:00}-{i % 24:00}{i % 60:00}";
            var dir = (i % 7) switch { 0 => archive, 1 => story, _ => _reports };
            var qa = i % 3 == 0;
            Write(Path.Combine(dir, $"{stamp}-{card}{(qa ? "-qa" : "")}.md"), card, $"2026-11-{10 + i / 40:00} {i % 24:00}:{i % 60:00}", qa ? "status: qa\n" : "");
        }
        // the card's own reports, in each place, with a suffix and a card id it only starts like (T-10 isn't T-1)
        Write(Path.Combine(_reports, "20261126-0900-T-1.md"), "T-1", "2026-11-26 09:00");
        Write(Path.Combine(_reports, "20261126-0901-T-1-gitignore.md"), "T-1", "2026-11-26 09:01");
        Write(Path.Combine(archive, "20261125-0900-T-1-qa.md"), "T-1", "2026-11-25 09:00", "status: qa\n");
        Write(Path.Combine(story, "20261124-0900-T-1.md"), "t-1", "2026-11-24 09:00");
        Write(Path.Combine(_reports, "20261126-0900-T-10.md"), "T-10", "2026-11-26 09:00");
        Write(Path.Combine(_reports, "20261126-0900-AT-1.md"), "AT-1", "2026-11-26 09:00");
        // named by hand with no card in the name: opened to see whose it is
        Write(Path.Combine(_reports, "summary.md"), "T-1", "2026-11-20 09:00");
        Write(Path.Combine(_reports, "notes for later.md"), "WOR-0001", "2026-11-20 09:00");
    }

    [Fact]
    public void ReportsFor_gives_what_it_gave_before_archived_ones_included_without_opening_other_cards_files()
    {
        Fill();
        var found = CardIndex.ReportsFor(_reports, "T-1");

        Assert.Equal(Before(_reports, "T-1"), found.Select(r => $"{r.Id}|{r.Story}|{r.Qa}"));
        Assert.Equal(["20261126-0901-T-1-gitignore", "20261126-0900-T-1", "20261125-0900-T-1-qa", "20261124-0900-T-1", "summary"], found.Select(r => r.Id));
        Assert.Equal("Logo", found.Single(r => r.Id == "20261124-0900-T-1").Story);
        // opened: the card's five and the two named by hand, none of the 400 or T-10's or AT-1's
        Assert.Equal(["20261124-0900-T-1.md", "20261125-0900-T-1-qa.md", "20261126-0900-T-1.md", "20261126-0901-T-1-gitignore.md", "notes for later.md", "summary.md"],
            _opened.Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // asked again, nothing changed: nothing is opened
        _opened.Clear();
        Assert.Equal(found.Select(r => r.Id), CardIndex.ReportsFor(_reports, "T-1").Select(r => r.Id));
        Assert.Empty(_opened);

        // every other card comes out as it did too
        foreach (var card in new[] { "WOR-0000", "WOR-0007", "WOR-0079", "T-10", "AT-1", "WOR-0001" })
            Assert.Equal(Before(_reports, card), CardIndex.ReportsFor(_reports, card).Select(r => $"{r.Id}|{r.Story}|{r.Qa}"));
    }

    [Fact]
    public void A_cards_blockers_reports_in_a_folder_of_400_take_well_under_200_ms()
    {
        Fill();
        var watch = Stopwatch.StartNew();
        // what a card agent's prompt asks for, one blocker at a time (BlockedStart.PromptLines' newestReport)
        foreach (var card in new[] { "WOR-0003", "WOR-0004", "WOR-0005", "T-1" }) Assert.NotEmpty(CardIndex.ReportsFor(_reports, card));
        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 200, $"took {watch.ElapsedMilliseconds} ms");
        Assert.True(_opened.Count < 40, $"opened {_opened.Count} files");
    }

    [Fact]
    public void A_folder_listed_again_opens_only_the_reports_that_changed()
    {
        Fill();
        var first = ReportStore.List(_reports);
        Assert.Equal(Directory.GetFiles(_reports, "*.md").Length, first.Count);
        _opened.Clear();

        var changed = Path.Combine(_reports, "20261126-0900-T-1.md");
        ReportStore.SetRead(changed, true);
        var added = Path.Combine(_reports, "20261126-1000-T-2.md");
        Write(added, "T-2", "2026-11-26 10:00");
        var gone = Path.Combine(_reports, "20261126-0900-AT-1.md");
        File.Delete(gone);

        var second = ReportStore.List(_reports);
        Assert.Equal([Path.GetFullPath(changed), Path.GetFullPath(added)], _opened.Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(second.Single(r => r.File == changed).Read);
        Assert.Contains(second, r => r.Card == "T-2");
        Assert.DoesNotContain(second, r => r.File == gone);
        Assert.Equal(first.Count, second.Count);
    }

    [Theory]
    [InlineData("20261126-0900-WOR-0228.md", "WOR-0228", true)]
    [InlineData("20261126-0900-WOR-0228-qa.md", "wor-0228", true)]
    [InlineData("20261126-0900-WOR-0228-qa-after.md", "WOR-0228", true)]
    [InlineData("20261126-0900-WOR-02281.md", "WOR-0228", false)]
    [InlineData("20261126-0900-XWOR-0228.md", "WOR-0228", false)]
    [InlineData("20261126-0900-WOR-0229.md", "WOR-0228", false)]
    [InlineData("summary.md", "WOR-0228", true)]
    [InlineData("20261126-0900.md", "WOR-0228", true)]
    public void A_report_name_may_be_the_cards_when_it_names_it_or_names_no_card(string name, string card, bool may) =>
        Assert.Equal(may, ReportCache.MayBeFor(name, card));
}
