using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class DocLinksTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("wb-doclinks").FullName;
    static readonly string[] Docs = ["Index.md", "Other.md", "My Notes.md", "sub/Deep.md", "sub/Other.md", "v1.2.md", "a/b/Plan.md"];

    public DocLinksTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "img"));
        File.WriteAllText(Path.Combine(_folder, "img", "shot.png"), "x");
    }

    public void Dispose() => Directory.Delete(_folder, true);

    DocLink Go(string link, string? from = "Index.md") => DocLinks.Resolve(link, from, Docs, _folder);

    [Theory]
    [InlineData("Other.md", "Index.md", "Other.md")]
    [InlineData("./Other.md", "Index.md", "Other.md")]
    [InlineData("Other", "Index.md", "Other.md")]                 // a doc linked without .md
    [InlineData("sub/Deep.md#part", "Index.md", "sub/Deep.md")]    // the heading is dropped
    [InlineData("My%20Notes.md", "Index.md", "My Notes.md")]
    [InlineData("<My Notes.md>", "Index.md", "My Notes.md")]
    [InlineData("Other.md \"a title\"", "Index.md", "Other.md")]
    [InlineData("../Index.md", "sub/Deep.md", "Index.md")]
    [InlineData("Other.md", "sub/Deep.md", "sub/Other.md")]       // relative to the doc's own folder
    [InlineData("/Other.md", "sub/Deep.md", "Other.md")]          // from the root of the docs folder
    [InlineData("sub\\Deep.md", "Index.md", "sub/Deep.md")]
    [InlineData("SUB/deep.MD", "Index.md", "sub/Deep.md")]
    public void Markdown_links_open_docs(string link, string from, string doc) =>
        Assert.Equal(new DocLink.Doc(doc), Go(link, from));

    [Theory]
    [InlineData("wiki:Other", "Index.md", "Other.md")]
    [InlineData("wiki:Other", "sub/Deep.md", "sub/Other.md")]      // the nearest of two with the name
    [InlineData("wiki:Other.md", "Index.md", "Other.md")]
    [InlineData("wiki:Deep#part", "Index.md", "sub/Deep.md")]
    [InlineData("wiki:Deep#^block", "Index.md", "sub/Deep.md")]
    [InlineData("wiki:sub/Deep", "Index.md", "sub/Deep.md")]
    [InlineData("wiki:b/Plan", "Index.md", "a/b/Plan.md")]         // a partial path, as Obsidian takes it
    [InlineData("wiki:v1.2", "Index.md", "v1.2.md")]
    [InlineData("wiki: My Notes ", "Index.md", "My Notes.md")]
    public void Wikilinks_open_docs(string link, string from, string doc) =>
        Assert.Equal(new DocLink.Doc(doc), Go(link, from));

    [Theory]
    [InlineData("https://example.com/a?b=c")]
    [InlineData("http://example.com")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("<https://example.com>")]
    public void Web_and_mail_links_open_outside(string link) =>
        Assert.Equal(new DocLink.External(link.Trim('<', '>')), Go(link));

    [Fact]
    public void A_file_that_isnt_a_doc_opens_outside()
    {
        var png = Path.Combine(_folder, "img", "shot.png");
        Assert.Equal(new DocLink.External(png), Go("img/shot.png"));
        Assert.Equal(new DocLink.External(png), Go("wiki:shot.png"));
        Assert.Equal(new DocLink.External(png), Go(png));
        Assert.Equal(new DocLink.External(png), Go(new Uri(png).AbsoluteUri));
        Assert.Equal(new DocLink.Doc("sub/Deep.md"), Go(Path.Combine(_folder, "sub", "Deep.md")));
    }

    [Theory]
    [InlineData("#heading")]
    [InlineData("Nope.md")]
    [InlineData("wiki:Nope")]
    [InlineData("wiki:#heading")]
    [InlineData("../../outside.md")]
    [InlineData("")]
    public void Links_to_nothing_say_why(string link) =>
        Assert.IsType<DocLink.Missing>(Go(link));

    [Fact]
    public void Links_resolve_with_no_doc_open() =>
        Assert.Equal(new DocLink.Doc("sub/Deep.md"), Go("sub/Deep.md", from: null));
}
