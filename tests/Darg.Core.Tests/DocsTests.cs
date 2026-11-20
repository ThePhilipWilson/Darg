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
    static readonly Regex WholeLink = new(@"\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled);
    static readonly Regex CodeSpan = new(@"`[^`\n]*`", RegexOptions.Compiled);
    static readonly Regex Scheme = new(@"^[a-zA-Z][a-zA-Z0-9+.-]+:", RegexOptions.Compiled);

    static IEnumerable<string> AllDocs() => Directory.GetFiles(Docs(), "*.md");

    /// <summary>A doc's lines outside fenced code blocks, with their line numbers (an example in a fence isn't the doc's own markdown).</summary>
    static IEnumerable<(int line, string text)> OutsideFences(string md)
    {
        var inFence = false;
        var lines = File.ReadAllLines(md);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```")) { inFence = !inFence; continue; }
            if (!inFence) yield return (i + 1, lines[i]);
        }
    }

    /// <summary>A doc's text outside fences and code spans: what a reader sees as prose and links.</summary>
    static string Prose(string md) => string.Join("\n", OutsideFences(md).Select(l => CodeSpan.Replace(l.text, "")));

    /// <summary>A heading's anchor as GitHub makes it: lower case, punctuation and icons dropped, spaces to hyphens, a repeat numbered.</summary>
    static HashSet<string> Anchors(string md)
    {
        var seen = new Dictionary<string, int>();
        var anchors = new HashSet<string>();
        foreach (var (_, text) in OutsideFences(md))
        {
            var h = Regex.Match(text, @"^#{1,6}\s+(.+?)\s*#*\s*$");
            if (!h.Success) continue;
            var slug = string.Concat(h.Groups[1].Value.ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')).Replace(' ', '-');
            var n = seen.GetValueOrDefault(slug);
            seen[slug] = n + 1;
            anchors.Add(n == 0 ? slug : $"{slug}-{n}");
        }
        return anchors;
    }

    /// <summary>Every link in the docs reaches a file, and every #part names a heading in it: a doc split or a heading renamed
    /// leaves links that go nowhere (WOR-0154).</summary>
    [Fact]
    public void Every_relative_link_in_the_docs_points_at_a_file_and_heading_that_exist()
    {
        var bad = new List<string>();
        foreach (var md in AllDocs())
            foreach (Match m in Link.Matches(Prose(md)))
            {
                var target = m.Groups[1].Value;
                if (Scheme.IsMatch(target)) continue; // http, mailto, card: and the like
                var parts = target.Split('#', 2);
                var file = parts[0].Length == 0 ? md : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(md)!, Uri.UnescapeDataString(parts[0])));
                if (!File.Exists(file) && !Directory.Exists(file)) { bad.Add($"{Path.GetFileName(md)} -> {target}"); continue; }
                if (parts.Length == 2 && file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !Anchors(file).Contains(parts[1]))
                    bad.Add($"{Path.GetFileName(md)} -> {target} (no such heading)");
            }
        Assert.True(bad.Count == 0, "links to nothing: " + string.Join(", ", bad));
    }

    /// <summary>A doc named in another's text is a link to it, so a reader can follow it (WOR-0154: ANDROID.md named PHONE-PLAN.md
    /// without a link). README.md is left out: other folders have READMEs too.</summary>
    [Fact]
    public void Every_doc_named_in_the_docs_is_a_link()
    {
        var names = Directory.GetFiles(Docs()).Select(Path.GetFileName)
            .Where(n => n is not null && n != "README.md" && (n.EndsWith(".md") || n.EndsWith(".html"))).ToList();
        var bad = new List<string>();
        foreach (var md in AllDocs())
        {
            // links taken out, code spans left in: a doc named in backticks still wants a link
            var text = string.Join("\n", OutsideFences(md).Select(l => WholeLink.Replace(l.text, "")));
            foreach (var name in names)
                if (Regex.IsMatch(text, $@"(?<![\w/-]){Regex.Escape(name!)}"))
                    bad.Add($"{Path.GetFileName(md)}: {name}");
        }
        Assert.True(bad.Count == 0, "named but not linked: " + string.Join(", ", bad));
    }

    /// <summary>The formatting the docs share (docs/README.md, "How these docs are written"): one title then an intro paragraph,
    /// no bold line standing in for a heading, a language on every fenced block, card ids written in full.</summary>
    [Fact]
    public void Every_doc_follows_the_shared_formatting()
    {
        var bad = new List<string>();
        foreach (var md in AllDocs())
        {
            var doc = Path.GetFileName(md);
            var lines = OutsideFences(md).ToList();
            var first = lines.FirstOrDefault(l => l.text.Trim().Length > 0);
            if (!first.text?.StartsWith("# ") ?? true) bad.Add($"{doc}: doesn't open with a # title");
            var titles = lines.Count(l => l.text.StartsWith("# "));
            if (titles != 1) bad.Add($"{doc}: {titles} # titles");
            var intro = lines.SkipWhile(l => l != first).Skip(1).FirstOrDefault(l => l.text.Trim().Length > 0);
            if (intro.text is null || intro.text.StartsWith('#')) bad.Add($"{doc}: no intro paragraph under the title");
            foreach (var (line, text) in lines)
                if (Regex.IsMatch(text, @"^\s*\*\*[^*]+\*\*:?\s*$")) bad.Add($"{doc}:{line}: a bold line used as a heading");
            var fence = false;
            foreach (var (text, i) in File.ReadAllLines(md).Select((t, i) => (t, i + 1)))
            {
                if (!text.TrimStart().StartsWith("```")) continue;
                if (!fence && text.TrimStart().TrimStart('`').Trim().Length == 0) bad.Add($"{doc}:{i}: a fenced block with no language");
                fence = !fence;
            }
            foreach (Match m in Regex.Matches(Prose(md), @"\b[A-Z]{2,5}-\d{3,5}\s*(?:to|and|or|,|/|-|–)\s*\d{3,5}\b"))
                bad.Add($"{doc}: \"{m.Value}\" shortens a card id");
        }
        Assert.True(bad.Count == 0, "formatting: " + string.Join("; ", bad));
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
