using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0146: a column's sort, and the Moved line it relies on.</summary>
public sealed class ColumnSortTests : IDisposable
{
    readonly string _dir = Directory.CreateDirectory(Path.Combine(Directory.CreateTempSubdirectory("wb-sort-").FullName, "board")).FullName;

    public ColumnSortTests()
    {
        File.WriteAllText(Path.Combine(_dir, "board.json"), """{"columns":[{"id":"c1","name":"To Do"},{"id":"c2","name":"Doing"}],"prefix":"T","nextId":5,"order":{}}""");
        Directory.CreateDirectory(Path.Combine(_dir, "items"));
    }

    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch (IOException) { } }

    void Make(int n, string title, string created, string? moved = null) =>
        File.WriteAllText(Path.Combine(_dir, "items", $"T-{n:0000} - {title}.md"),
            $"# T-{n:0000}: {title}\n\n- **Status:** To Do\n- **Priority:**\n- **Type:** Task\n- **Labels:**\n- **Created:** {created}\n" +
            (moved is null ? "" : $"- **Moved:** {moved}\n") + "- **Done:**\n\n## Description\n\nx\n");

    static string[] Ids(IEnumerable<Card> cards) => cards.Select(c => c.Id).ToArray();

    List<Card> Todo() => BoardStore.Load(_dir).Lists["c1"];

    [Fact]
    public void Each_sort_orders_a_column_and_reverses()
    {
        Make(1, "banana", "2026-11-10", "2026-11-12 09:00:00");
        Make(2, "apple", "2026-11-11", "2026-11-11 08:00:00");
        Make(3, "cherry", "2026-11-12");           // no Moved: falls back to Created (midnight 11-12)
        Make(4, "Date", "2026-11-12", "2026-11-12 15:30:00");
        var cards = Todo();

        Assert.Equal("apple banana cherry Date", string.Join(" ", new ColumnSort(SortBy.Title).Apply(cards).Select(c => c.Title)));
        Assert.Equal("Date cherry banana apple", string.Join(" ", new ColumnSort(SortBy.Title, true).Apply(cards).Select(c => c.Title)));
        // newest: by Created, number breaking ties (cherry 3 and Date 4 are the same day)
        Assert.Equal(["T-0004", "T-0003", "T-0002", "T-0001"], Ids(new ColumnSort(SortBy.Newest).Apply(cards)));
        Assert.Equal(["T-0001", "T-0002", "T-0003", "T-0004"], Ids(new ColumnSort(SortBy.Newest, true).Apply(cards)));
        // longest in column: earliest entered first: apple 11-11 08:00, cherry 11-12 00:00 (Created), banana 11-12 09:00, Date 11-12 15:30
        Assert.Equal(["T-0002", "T-0003", "T-0001", "T-0004"], Ids(new ColumnSort(SortBy.Longest).Apply(cards)));
        Assert.Equal(["T-0004", "T-0001", "T-0003", "T-0002"], Ids(new ColumnSort(SortBy.Longest, true).Apply(cards)));
        // manual leaves board.json's order (here: by number) alone
        Assert.Equal(Ids(cards), Ids(ColumnSort.Manual.Apply(cards)));
    }

    [Fact]
    public void Moving_a_card_to_another_column_stamps_Moved_and_reordering_does_not()
    {
        Make(1, "one", "2026-11-10", "2020-01-01 00:00:00");
        Make(2, "two", "2026-11-10");
        BoardStore.Move(_dir, "T-0001", "c1", "T-0002"); // same column: a reorder
        Assert.Equal(new DateTime(2020, 1, 1), BoardStore.ReadCards(_dir).Single(c => c.Id == "T-0001").Moved);

        var before = DateTime.Now.AddSeconds(-2);
        BoardStore.Move(_dir, "T-0001", "c2");
        BoardStore.Move(_dir, "T-0002", "c2");
        Assert.All(BoardStore.ReadCards(_dir), c => Assert.True(c.Moved > before, $"{c.Id} Moved {c.Moved}"));
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "items"), "T-0001*")[0]);
        Assert.Equal(1, text.Split("**Moved:**").Length - 1);
    }

    [Fact]
    public void A_card_with_no_Moved_line_gets_one_with_the_other_fields()
    {
        Make(1, "one", "2026-11-10");
        BoardStore.Move(_dir, "T-0001", "c2");
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "items"))[0]);
        Assert.Matches(@"\*\*Created:\*\* 2026-11-10\s+- \*\*Done:\*\*\s+- \*\*Moved:\*\* \d{4}-\d\d-\d\d \d\d:\d\d:\d\d", text);
        Assert.True(text.IndexOf("**Moved:**", StringComparison.Ordinal) < text.IndexOf("## Description", StringComparison.Ordinal));
    }

    [Fact]
    public void A_status_changed_by_hand_is_stamped_on_the_next_read()
    {
        Make(1, "one", "2026-11-10");
        var watch = new MovedWatch();
        Assert.Empty(watch.Read(_dir, BoardStore.Load(_dir))); // the first read only learns
        var file = Directory.GetFiles(Path.Combine(_dir, "items"))[0];
        File.WriteAllText(file, File.ReadAllText(file).Replace("**Status:** To Do", "**Status:** Doing"));
        Assert.Equal(["T-0001"], watch.Read(_dir, BoardStore.Load(_dir)));
        Assert.NotNull(BoardStore.ReadCards(_dir)[0].Moved);
        Assert.Empty(watch.Read(_dir, BoardStore.Load(_dir))); // and only once
    }

    [Fact]
    public void Two_files_with_one_id_read_and_are_named_not_stamped()
    {
        // DAR-0003: a renamed card's old file left beside the new one made every read of the board throw
        Make(1, "one", "2026-11-10");
        Make(2, "two", "2026-11-10");
        var file = Directory.GetFiles(Path.Combine(_dir, "items")).Single(f => Path.GetFileName(f).StartsWith("T-0001"));
        var copy = Path.Combine(_dir, "items", "T-0001 - one, old name.md");
        File.WriteAllText(copy, File.ReadAllText(file).Replace("**Status:** To Do", "**Status:** Doing"));
        var board = BoardStore.Load(_dir);
        Assert.Equal(2, board.AllCards.Count(c => c.Id == "T-0001"));
        var dup = Assert.Single(board.Duplicates);
        Assert.Equal("T-0001", dup.Id);
        Assert.Equal(2, dup.Files.Count);
        Assert.Contains("T-0001 is in 2 files", board.DuplicatesText);
        var watch = new MovedWatch();
        Assert.Empty(watch.Read(_dir, board));
        File.WriteAllText(file, File.ReadAllText(file).Replace("**Status:** To Do", "**Status:** Doing"));
        Assert.Empty(watch.Read(_dir, BoardStore.Load(_dir))); // ambiguous: neither file is stamped
        var other = Directory.GetFiles(Path.Combine(_dir, "items")).Single(f => Path.GetFileName(f).StartsWith("T-0002"));
        File.WriteAllText(other, File.ReadAllText(other).Replace("**Status:** To Do", "**Status:** Doing"));
        Assert.Equal(["T-0002"], watch.Read(_dir, BoardStore.Load(_dir))); // the rest of the board still is
    }

    [Fact]
    public void Sorts_are_kept_per_column_and_manual_is_not_kept()
    {
        var instance = Directory.CreateTempSubdirectory("wb-sorts-").FullName;
        try
        {
            ColumnSorts.Save("p", new Dictionary<string, ColumnSort> { ["Main/c1"] = new(SortBy.Longest, true), ["Main/c2"] = ColumnSort.Manual }, instance);
            var loaded = ColumnSorts.Load("p", instance);
            Assert.Equal(new ColumnSort(SortBy.Longest, true), loaded["Main/c1"]);
            Assert.False(loaded.ContainsKey("Main/c2"));
        }
        finally { Directory.Delete(instance, true); }
    }
}
