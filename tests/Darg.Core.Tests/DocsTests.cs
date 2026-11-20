using System.Text.RegularExpressions;

namespace Darg.Core.Tests;

/// <summary>The repo's docs/ folder (WOR-0154): a doc that links to a file that isn't there, or isn't in the index, is a doc nobody finds.</summary>
public sealed class DocsTests
{
    static string Docs()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Darg.sln"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new InvalidOperationException("not run from inside the Darg repo"), "docs");
    }

    static readonly Regex Link = new(@"\]\(([^)\s]+)\)", RegexOptions.Compiled);

    [Fact]
    public void Every_relative_link_in_the_docs_points_at_a_file_that_exists()
    {
        var bad = new List<string>();
        foreach (var md in Directory.GetFiles(Docs(), "*.md"))
            foreach (Match m in Link.Matches(File.ReadAllText(md)))
            {
                var target = m.Groups[1].Value;
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith('#') || target.StartsWith("mailto:")) continue;
                var path = Uri.UnescapeDataString(target.Split('#')[0]);
                if (!File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(md)!, path))) &&
                    !Directory.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(md)!, path))))
                    bad.Add($"{Path.GetFileName(md)} -> {target}");
            }
        Assert.True(bad.Count == 0, "links to nothing: " + string.Join(", ", bad));
    }

    [Fact]
    public void The_index_lists_every_doc()
    {
        var index = File.ReadAllText(Path.Combine(Docs(), "README.md"));
        var missing = Directory.GetFiles(Docs(), "*.*")
            .Select(Path.GetFileName).Where(n => n is not null && n != "README.md" && (n.EndsWith(".md") || n.EndsWith(".html")))
            .Where(n => !index.Contains($"]({n})")).ToList();
        Assert.True(missing.Count == 0, "not in docs/README.md: " + string.Join(", ", missing));
    }
}
