using Darg.Core;

namespace Darg.Core.Tests;

public class ContextFilesTests
{
    [Fact]
    public void MemoryFolderDashesEveryNonAlphanumeric()
    {
        var path = ContextFiles.MemoryFolder("D:/GitHub/ThePhilipWilson/Workbench", "H:/h");
        Assert.EndsWith("D--GitHub-ThePhilipWilson-Workbench" + Path.DirectorySeparatorChar + "memory", path);
    }

    [Fact]
    public void ListGroupsInstructionsMemoryAndDargsOwn()
    {
        var root = Path.Combine(Path.GetTempPath(), "wb-ctxf-" + Guid.NewGuid().ToString("N")[..8]);
        var home = Path.Combine(root, "home");
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        Directory.CreateDirectory(repo);
        var memory = ContextFiles.MemoryFolder(repo, home);
        Directory.CreateDirectory(memory);
        try
        {
            File.WriteAllText(Path.Combine(home, ".claude", "CLAUDE.md"), "user");
            File.WriteAllText(Path.Combine(repo, "CLAUDE.md"), "repo rules");
            File.WriteAllText(Path.Combine(memory, "b-note.md"), "b");
            File.WriteAllText(Path.Combine(memory, "MEMORY.md"), "index");
            var project = new ProjectConfig { Id = "p", Name = "P", Repos = [repo] };

            var items = ContextFiles.List(project, home, Path.Combine(root, "inst"));

            Assert.Equal("User: CLAUDE.md", items[0].Name);
            Assert.Contains(items, i => i.Group == ContextFiles.InstructionsGroup && i.Name == "repo: CLAUDE.md");
            var mem = items.Where(i => i.Group == ContextFiles.MemoryGroup).Select(i => i.Name).ToList();
            Assert.Equal(["MEMORY.md", "b-note.md"], mem);
            var darg = Assert.Single(items, i => i.Group == ContextFiles.DargGroup);
            Assert.Contains("Default Claude context", ContextFiles.Read(darg));
            Assert.Equal("repo rules", ContextFiles.Read(items.First(i => i.Name == "repo: CLAUDE.md")));
        }
        finally { Directory.Delete(root, true); }
    }
}
