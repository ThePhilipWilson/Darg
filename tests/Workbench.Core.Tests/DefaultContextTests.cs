using Workbench.Core;

namespace Workbench.Core.Tests;

public class DefaultContextTests
{
    [Fact]
    public void ClaudeFilesAreUserFirstThenRootDownToFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "wb-ctx-" + Guid.NewGuid().ToString("N")[..8]);
        var home = Path.Combine(root, "home");
        var work = Path.Combine(root, "code", "repo");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        Directory.CreateDirectory(Path.Combine(work, ".claude"));
        try
        {
            File.WriteAllText(Path.Combine(home, ".claude", "CLAUDE.md"), "user");
            File.WriteAllText(Path.Combine(root, "code", "CLAUDE.md"), "parent");
            File.WriteAllText(Path.Combine(work, "CLAUDE.local.md"), "local");
            File.WriteAllText(Path.Combine(work, ".claude", "CLAUDE.md"), "dotclaude");

            // Ancestors of the temp folder are the real machine's, so only the files under our root are compared.
            var files = DefaultContext.ClaudeFiles(work, home).Where(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToList();

            Assert.Equal(
                new[]
                {
                    Path.Combine(home, ".claude", "CLAUDE.md"),
                    Path.Combine(root, "code", "CLAUDE.md"),
                    Path.Combine(work, "CLAUDE.local.md"),
                    Path.Combine(work, ".claude", "CLAUDE.md"),
                },
                files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BuildShowsTheBriefingAndNoneWhenNoFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "wb-ctx-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var text = DefaultContext.Build(new ProjectConfig { Id = "p", Name = "Demo" }, root, userHome: root, instanceFolder: root);

            Assert.Contains("# Default Claude context: Demo", text);
            Assert.Contains("--add-dir", text);
            Assert.Contains("Artifacts in Workbench:", text);
            Assert.Contains("## CLAUDE.md files Claude Code loads", text);
        }
        finally { Directory.Delete(root, true); }
    }
}
