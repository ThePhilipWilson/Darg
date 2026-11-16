using Darg.Core;

namespace Darg.Core.Tests;

public sealed class ProjectHomeTests
{
    [Fact]
    public void A_line_of_pictures_is_a_picture_row()
    {
        var blocks = Markdown.Parse("# Title\n\n![shot](docs/shot.png)\n\nText after.");
        var row = Assert.IsType<MdImages>(blocks[1]);
        Assert.Equal(new MdImage("shot", "docs/shot.png"), Assert.Single(row.Images));
        Assert.IsType<MdParagraph>(blocks[2]);
    }

    [Fact]
    public void Badges_and_html_pictures_keep_their_links_and_order()
    {
        var md = """
            <p align="center">
              <a href="https://example.com"><img src="logo.png" alt="Logo" width="200"></a>
            </p>
            [![CI](https://ci.example/badge.svg)](https://ci.example) ![Stars](stars.svg "title")
            """;
        var blocks = Markdown.Parse(md);
        var row = Assert.IsType<MdImages>(Assert.Single(blocks));
        Assert.True(row.Centred);
        Assert.Equal(
            [new MdImage("Logo", "logo.png", "https://example.com", 200),new MdImage("CI", "https://ci.example/badge.svg", "https://ci.example"), new MdImage("Stars", "stars.svg")],
            row.Images);
    }

    [Theory]
    [InlineData("Look at ![this](a.png) here")]
    [InlineData("<https://example.com>")]
    [InlineData("List<string> is a type")]
    [InlineData("<T>")]
    public void Lines_with_text_stay_paragraphs(string line) =>
        Assert.IsType<MdParagraph>(Assert.Single(Markdown.Parse(line)));

    [Fact]
    public void A_picture_in_a_sentence_is_a_link_named_by_its_alt_text()
    {
        var spans = Markdown.Inline("Look at ![the board](shots/board.png) and [![CI](b.svg)](https://ci) now");
        Assert.Contains(spans, s => s.Text == "the board" && s.Link == "shots/board.png");
        Assert.Contains(spans, s => s.Text == "CI" && s.Link == "https://ci");
        Assert.DoesNotContain(spans, s => s.Text.Contains('!'));
    }

    [Fact]
    public void Readme_is_the_first_repos_then_the_docs_folders()
    {
        var root = Directory.CreateTempSubdirectory("wb-home-").FullName;
        try
        {
            var a = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
            var b = Directory.CreateDirectory(Path.Combine(root, "b")).FullName;
            var docs = Directory.CreateDirectory(Path.Combine(root, "docs")).FullName;
            var project = new ProjectConfig { Repos = [a, b, Path.Combine(root, "missing")], Docs = docs };
            Assert.Null(ProjectHome.Readme(project));
            File.WriteAllText(Path.Combine(docs, "index.md"), "# Docs");
            Assert.Equal(Path.Combine(docs, "index.md"), ProjectHome.Readme(project));
            File.WriteAllText(Path.Combine(b, "readme.md"), "# B");
            Assert.Equal(Path.Combine(b, "readme.md"), ProjectHome.Readme(project));
            File.WriteAllText(Path.Combine(a, "README.md"), "# A");
            Assert.Equal(Path.Combine(a, "README.md"), ProjectHome.Readme(project));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Log_is_parsed_and_read_from_a_real_repo()
    {
        var log = Repo.ParseLog("abc1234\u001fFirst line\u001fSomeone\u001f2026-11-16T21:00:00+00:00\u001e\ndef5678\u001fSecond\u001fOther\u001fnot a date\u001e");
        Assert.Equal(new Commit("abc1234", "First line", "Someone", new DateTimeOffset(2026, 11, 16, 21, 0, 0, TimeSpan.Zero)), Assert.Single(log));

        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Darg.sln"))) d = d.Parent;
        Assert.Equal(3, Repo.Log(d!.FullName, 3).Count);
    }

    [Fact]
    public void Ago_reads_as_words()
    {
        var now = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 16)));
        Assert.Equal("just now", ProjectHome.Ago(now.AddSeconds(-20), now));
        Assert.Equal("5 min ago", ProjectHome.Ago(now.AddMinutes(-5), now));
        Assert.Equal("3 h ago", ProjectHome.Ago(now.AddHours(-3), now));
        Assert.Equal("yesterday", ProjectHome.Ago(now.AddHours(-20), now));
        Assert.Equal("4 days ago", ProjectHome.Ago(now.AddDays(-4), now));
        Assert.Equal("2 Nov", ProjectHome.Ago(now.AddDays(-14), now));
    }
}
