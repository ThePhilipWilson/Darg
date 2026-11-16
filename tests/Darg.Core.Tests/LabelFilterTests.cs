using Darg.Core;

namespace Darg.Core.Tests;

public sealed class LabelFilterTests
{
    static bool M(string labels, string any, string all) => LabelFilter.Matches(
        labels.Split(',', StringSplitOptions.RemoveEmptyEntries),
        any.Split(',', StringSplitOptions.RemoveEmptyEntries), all.Split(',', StringSplitOptions.RemoveEmptyEntries));

    [Theory]
    [InlineData("art-cards,bimbo", "", "", true)]          // nothing picked: everything
    [InlineData("art-cards", "bimbo,art-cards", "", true)] // any: one is enough
    [InlineData("engine", "bimbo,art-cards", "", false)]
    [InlineData("art-cards,bimbo", "", "bimbo,art-cards", true)] // all: both needed
    [InlineData("art-cards", "", "bimbo,art-cards", false)]
    [InlineData("bimbo,content", "art-cards,content", "bimbo", true)]  // must bimbo, and art-cards or content
    [InlineData("bimbo,engine", "art-cards,content", "bimbo", false)]
    [InlineData("Bimbo,ART-CARDS", "", "bimbo,art-cards", true)] // case doesn't matter
    public void Any_and_all(string labels, string any, string all, bool keep) => Assert.Equal(keep, M(labels, any, all));
}
