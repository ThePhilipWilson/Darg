using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

public class ReportArchiveTests
{
    static string Folder()
    {
        var d = Path.Combine(Path.GetTempPath(), "darg-reports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static void Write(string folder, string name, string status, string card) =>
        File.WriteAllText(Path.Combine(folder, name + ".md"), $"---\nstatus: {status}\ncard: {card}\ntitle: t\ndate: 2026-11-17 10:00\nread: true\n---\n\n## Done\nx\n");

    [Fact]
    public void ArchivesDoneReportsOutOfTheMainListAndRestoresThem()
    {
        var f = Folder();
        try
        {
            Write(f, "20261117-1000-WOR-1", "done", "WOR-1");
            Write(f, "20261117-1001-WOR-2", "qa", "WOR-2");
            var all = ReportStore.List(f);
            Assert.Equal(1, ReportArchive.Archive(f, "Story: A/B", all)); // the QA one stays; odd characters are made safe
            Assert.Single(ReportStore.List(f));
            var archived = ReportArchive.List(f);
            Assert.Single(archived);
            Assert.Equal("Story_ A_B", archived[0].Story);
            Assert.Equal(1, ReportArchive.Restore(f, archived));
            Assert.Equal(2, ReportStore.List(f).Count);
            Assert.Empty(ReportArchive.List(f));
            Assert.False(Directory.Exists(Path.Combine(f, "archive", "Story_ A_B")));
        }
        finally { Directory.Delete(f, true); }
    }
}
