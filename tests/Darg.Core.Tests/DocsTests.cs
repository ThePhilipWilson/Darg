using System.Text.RegularExpressions;

namespace Darg.Core.Tests;

/// <summary>The repo's docs/ folder (WOR-0154): a doc that links to a file that isn't there, or isn't in the index, is a doc nobody finds.</summary>
public sealed class DocsTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Darg.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("not run from inside the Darg repo");
    }

    static string Docs() => Path.Combine(Root(), "docs");

    /// <summary>The docs that describe the app as it is; PLAN, MORNING and PHONE-PLAN are history and keep their old words.</summary>
    static IEnumerable<string> CurrentDocs() => Directory.GetFiles(Docs(), "*.md")
        .Where(f => Path.GetFileName(f) is not ("PLAN.md" or "MORNING.md" or "PHONE-PLAN.md"));

    /// <summary>The text of the repo's own files (src, tests, tools), never build output, docs, or other worktrees (paths taken
    /// from the repo's root, since the repo itself may be a worktree under .claude/worktrees).</summary>
    static string Source(string under, params string[] patterns)
    {
        var root = Root();
        string[] skip = ["bin", "obj", ".claude", ".shots", "docs", ".darg"];
        return string.Concat(patterns.SelectMany(p => Directory.GetFiles(Path.Combine(root, under), p, SearchOption.AllDirectories))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(skip.Contains))
            .Select(File.ReadAllText));
    }

    /// <summary>A button named in the docs by its icon and first word (✓ Done, 🔔 For human attention) must be a label the app shows:
    /// QA found "🔔 Needs my attention" in the docs for a button that says "🔔 For human attention".</summary>
    [Fact]
    public void Every_button_label_the_docs_name_is_in_the_app()
    {
        var src = Source("src", "*.axaml", "*.cs");  // src only: this file quotes the wrong label
        var label = new Regex(@"(?:🔔|✓|↩|🧪|▶|✦|📋|⧉|⟳|✎|✗|🎨) [A-Z][\w'-]*");
        var bad = CurrentDocs()
            .SelectMany(md => label.Matches(File.ReadAllText(md)).Select(m => (doc: Path.GetFileName(md), text: m.Value)))
            .Where(x => !src.Contains(x.text)).Select(x => $"{x.doc}: {x.text}").Distinct().ToList();
        Assert.True(bad.Count == 0, "labels the app doesn't show: " + string.Join(", ", bad));
    }

    /// <summary>An environment variable the docs name must be read somewhere: QA found the docs promising WORKBENCH_* for every DARG_* name,
    /// when only DARG_HOME and DARG_INSTANCE fall back.</summary>
    [Fact]
    public void Every_environment_variable_the_docs_name_is_read_somewhere()
    {
        var src = Source("", "*.cs", "*.ps1", "*.csproj", "*.props", "*.targets", "*.py");
        var name = new Regex(@"\b(?:DARG|WORKBENCH)_[A-Z][A-Z_]*[A-Z]\b");
        var bad = CurrentDocs()
            .SelectMany(md => name.Matches(File.ReadAllText(md)).Select(m => (doc: Path.GetFileName(md), text: m.Value)))
            // an old WORKBENCH_ name works only where InstanceConfig.Env, which falls back to it, reads the DARG_ one
            .Where(x => !(x.text.StartsWith("WORKBENCH_") ? src.Contains("Env(\"DARG_" + x.text["WORKBENCH_".Length..] + "\")") : src.Contains(x.text)))
            .Select(x => $"{x.doc}: {x.text}").Distinct().ToList();
        Assert.True(bad.Count == 0, "named in the docs but read nowhere: " + string.Join(", ", bad));
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
