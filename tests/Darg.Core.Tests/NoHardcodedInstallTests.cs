using Xunit;

namespace Darg.Core.Tests;

/// <summary>WOR-0198: the docs and scripts name no fixed install folder and no outside project; the install path is derived.</summary>
public class NoHardcodedInstallTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Darg.sln"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void Docs_and_tools_have_no_fixed_install_path_or_outside_project()
    {
        var root = Root();
        var files = new[] { Path.Combine(root, "CLAUDE.md"), Path.Combine(root, "src", "Darg.Core", "Updater.cs") }
            .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "tools"), "*.ps1"));
        var bad = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            if (text.Contains("D:" + (char)92 + "Apps", StringComparison.OrdinalIgnoreCase)) bad.Add(f + ": fixed install path");
            if (text.Contains("Tim's Tatty") || text.Contains("what Tim wants")) bad.Add(f + ": Tim's Tatty");
        }
        Assert.Empty(bad);
    }
}
