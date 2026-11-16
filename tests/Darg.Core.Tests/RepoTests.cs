using Darg.Core;

namespace Darg.Core.Tests;

public sealed class RepoTests
{
    static string ThisRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Darg.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("not run from inside the Darg repo");
    }

    [Fact]
    public void Files_are_gits_list_without_ignored_build_output()
    {
        var files = Repo.Files(ThisRepo());
        Assert.Contains("src/Darg.Core/Repo.cs", files);
        Assert.DoesNotContain(files, f => f.Contains("/bin/") || f.Contains("/obj/"));
    }

    [Fact]
    public void Status_parses_branch_tracking_and_changes()
    {
        var s = Repo.ParseStatus("## main...origin/main [ahead 2, behind 1]\0 M src/a.cs\0?? new file.txt\0R  b.cs\0old b.cs\0A  c.cs\0");
        Assert.Equal(("main", 2, 1), (s.Branch, s.Ahead, s.Behind));
        Assert.Equal(" M", s.Changed["src/a.cs"]);
        Assert.Equal("??", s.Changed["new file.txt"]);
        Assert.Equal("R ", s.Changed["b.cs"]);
        Assert.False(s.Changed.ContainsKey("old b.cs"));
        Assert.Equal("A ", s.Changed["c.cs"]);
    }

    [Theory]
    [InlineData("## main", "main")]
    [InlineData("## No commits yet on main", "main")]
    [InlineData("## feature/x...origin/feature/x", "feature/x")]
    public void Branch_names(string head, string branch) => Assert.Equal(branch, Repo.ParseStatus(head + "\0").Branch);

    [Fact]
    public void Status_of_this_repo_has_a_branch() => Assert.False(string.IsNullOrEmpty(Repo.Status(ThisRepo()).Branch));

    [Fact]
    public void Binary_and_big_files_are_refused_with_a_reason()
    {
        var f = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(f, [1, 2, 0, 3]);
            Assert.Equal((null, "A binary file."), Repo.Read(f));
            File.WriteAllText(f, "﻿hello");
            Assert.Equal(("hello", ""), Repo.Read(f));
            Assert.Null(Repo.Read(f, maxBytes: 2).Text);
        }
        finally { File.Delete(f); }
    }
}
