using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0170: at most a few agents' copies of Darg at once; Philip's installed and live copies never count.</summary>
public class CopiesTests
{
    const string Qa = @"D:\repo\.shots\qa-and0006\src\Darg.Desktop\bin\Debug\net10.0-windows10.0.19041.0\Darg.Desktop.exe";
    const string Card = @"D:\repo\.claude\worktrees\wor-0161\src\Darg.Desktop\bin\Debug\net10.0-windows10.0.19041.0\Darg.Desktop.exe";
    const string Installed = @"C:\Installed\Darg\Darg.Desktop.exe";
    const string Live = @"D:\repo\.claude\worktrees\live\src\Darg.Desktop\bin\Debug\net10.0-windows10.0.19041.0\Darg.Desktop.exe";
    static bool Exempt(string exe) => exe == Installed || Copies.Exempt(exe);

    [Fact]
    public void An_agent_copy_starts_below_the_cap_and_not_at_it()
    {
        Assert.Null(Copies.Refusal(Card, [Qa, Qa, Qa], 4, Exempt));
        var refusal = Copies.Refusal(Card, [Qa, Qa, Qa, Card], 4, Exempt);
        Assert.NotNull(refusal);
        Assert.Contains("4 other debug or QA copies", refusal);
        Assert.Contains($"3 from {Qa}", refusal); // says which, so the agent can close its own
        Assert.Contains(Copies.MaxVar, refusal);
    }

    [Fact]
    public void Philips_own_copies_are_never_counted_nor_refused()
    {
        Assert.Null(Copies.Refusal(Card, [Installed, Live, Qa, Qa, Qa], 4, Exempt)); // only three count
        Assert.Null(Copies.Refusal(Installed, [Qa, Qa, Qa, Qa, Card], 4, Exempt));
        Assert.Null(Copies.Refusal(Live, [Qa, Qa, Qa, Qa, Card], 4, Exempt));
    }

    [Fact]
    public void The_cap_is_set_or_turned_off_by_its_variable()
    {
        Assert.Equal(Copies.DefaultMax, Copies.Max(null));
        Assert.Equal(Copies.DefaultMax, Copies.Max("lots"));
        Assert.Equal(2, Copies.Max("2"));
        Assert.Null(Copies.Refusal(Card, [Qa, Qa, Qa, Qa, Qa, Qa], 0, Exempt));
        Assert.NotNull(Copies.Refusal(Card, [Qa, Qa], 2, Exempt));
    }

    [Fact]
    public void The_installed_copy_is_known_by_the_source_file_beside_it()
    {
        var dir = Directory.CreateTempSubdirectory("darg-copies-").FullName;
        try
        {
            var exe = Path.Combine(dir, "Darg.Desktop.exe");
            Assert.False(Copies.Exempt(exe));
            File.WriteAllText(Path.Combine(dir, Updater.SourceFile), @"D:\repo");
            Assert.True(Copies.Exempt(exe));
        }
        finally { Directory.Delete(dir, true); }
    }
}
