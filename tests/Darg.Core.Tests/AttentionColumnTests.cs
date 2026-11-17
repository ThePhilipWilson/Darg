using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

public sealed class AttentionColumnTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-attn-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void NewBoardsGetTheColumn()
    {
        var f = BoardStore.CreateBoard(_dir, "B");
        Assert.Equal(["To Do", "In Progress", "For Human Attention", "Done"], BoardStore.Load(f).Columns.Select(c => c.Name));
    }

    [Fact]
    public void OldBoardGainsItBeforeTheLastColumnOnce()
    {
        var f = BoardStore.CreateBoard(_dir, "B", ["To Do", "Done"]);
        Assert.True(BoardStore.EnsureAttentionColumn(f));
        Assert.Equal(["To Do", "For Human Attention", "Done"], BoardStore.Load(f).Columns.Select(c => c.Name));
        var id = BoardStore.Load(f).Columns[1].Id;
        BoardStore.RemoveColumn(f, id);
        BoardStore.EnsureAttentionColumn(f);
        Assert.Equal(["To Do", "Done"], BoardStore.Load(f).Columns.Select(c => c.Name));
    }

    [Fact]
    public void LegacyNameIsRenamedWithItsCards()
    {
        var f = BoardStore.CreateBoard(_dir, "B", ["To Do", "Needs Phil Attention", "Done"]);
        var card = BoardStore.Create(f, "A card");
        var file = Path.Combine(f, "items", card.File);
        File.WriteAllText(file, File.ReadAllText(file).Replace("Backlog", "Needs Phil Attention"));
        BoardStore.EnsureAttentionColumn(f);
        var b = BoardStore.Load(f);
        Assert.Equal(["To Do", "For Human Attention", "Done"], b.Columns.Select(c => c.Name));
        Assert.Contains("- **Status:** For Human Attention", File.ReadAllText(file));
    }
}
