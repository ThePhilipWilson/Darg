using Darg.Core;

namespace Darg.Core.Tests;

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
    public void A_path_from_the_folders_root_is_not_taken_for_a_full_path() =>
        Assert.Equal(new DocLink.External(Path.Combine(_folder, "img", "shot.png")), Go("/img/shot.png"));

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

/// <summary>Links that leave the docs folder: into another repo, or elsewhere in the Obsidian vault it's in.</summary>
public sealed class CrossFolderDocLinksTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("wb-crosslinks").FullName;
    readonly string _vault, _docs, _repo;
    static readonly string[] Docs = ["Index.md", "sub/Deep.md"];

    public CrossFolderDocLinksTests()
    {
        _vault = Path.Combine(_root, "vault");
        _docs = Path.Combine(_vault, "Code", "Proj");
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(_vault, ".obsidian"));
        foreach (var f in new[] { "Code/Proj/Index.md", "Code/Proj/sub/Deep.md", "Meta Picks.md", "Code/Other/Plan.md",
                     "Research/Plan.md", ".trash/Gone.md", "img/shot.png" })
            Write(Path.Combine(_vault, f));
        Write(Path.Combine(_repo, "README.md"));
        Write(Path.Combine(_repo, "docs", "Setup.md"));
    }

    void Write(string file) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!); File.WriteAllText(file, "x"); }

    public void Dispose() => Directory.Delete(_root, true);

    DocLink Go(string link, string? from = "Index.md") => DocLinks.Resolve(link, from, Docs, _docs);
    DocLink.Elsewhere At(params string[] parts) => new(Path.GetFullPath(Path.Combine([_root, .. parts])));

    [Fact]
    public void A_relative_link_into_another_repo_opens_that_doc() =>
        Assert.Equal(At("repo", "README.md"), Go("../../../repo/README.md"));

    [Fact]
    public void A_link_out_of_the_folder_can_leave_off_md() =>
        Assert.Equal(At("repo", "docs", "Setup.md"), Go("../../../repo/docs/Setup"));

    [Fact]
    public void A_full_path_or_file_url_to_a_doc_outside_opens_it()
    {
        var readme = Path.Combine(_repo, "README.md");
        Assert.Equal(At("repo", "README.md"), Go(readme));
        Assert.Equal(At("repo", "README.md"), Go(readme.Replace('\\', '/')));
        Assert.Equal(At("repo", "README.md"), Go(new Uri(readme).AbsoluteUri));
    }

    [Fact]
    public void Links_in_a_doc_outside_go_from_where_it_is()
    {
        var readme = Path.Combine(_repo, "README.md").Replace('\\', '/');
        Assert.Equal(At("repo", "docs", "Setup.md"), Go("docs/Setup.md", readme));
        Assert.Equal(new DocLink.Doc("sub/Deep.md"), Go("../vault/Code/Proj/sub/Deep.md", readme)); // back into the folder
        Assert.IsType<DocLink.Missing>(Go("docs/Nope.md", readme));
    }

    [Fact]
    public void A_wikilink_finds_a_doc_elsewhere_in_the_vault()
    {
        Assert.Equal(At("vault", "Meta Picks.md"), Go("wiki:Meta Picks"));
        Assert.Equal(At("vault", "Meta Picks.md"), Go("wiki:meta picks.md#part"));
        Assert.Equal(At("vault", "Research", "Plan.md"), Go("wiki:Research/Plan"));
    }

    [Fact]
    public void A_wikilink_out_of_the_folder_takes_the_nearest_of_two() =>
        Assert.Equal(At("vault", "Code", "Other", "Plan.md"), Go("wiki:Plan")); // Code/Other is nearer Code/Proj than Research

    [Fact]
    public void A_doc_in_the_folder_still_wins() =>
        Assert.Equal(new DocLink.Doc("sub/Deep.md"), Go("wiki:Deep"));

    [Fact]
    public void A_vault_image_opens_in_its_own_app() =>
        Assert.Equal(new DocLink.External(Path.GetFullPath(Path.Combine(_vault, "img", "shot.png"))), Go("wiki:shot.png"));

    [Theory]
    [InlineData("wiki:Gone")]       // only in .trash, which Obsidian doesn't link to
    [InlineData("wiki:Nowhere")]
    public void A_wikilink_to_nothing_in_the_vault_says_why(string link) =>
        Assert.IsType<DocLink.Missing>(Go(link));

    [Fact]
    public void A_folder_outside_any_vault_looks_no_further()
    {
        var bare = Path.Combine(_repo, "docs");
        Assert.Null(DocLinks.Vault(bare));
        Assert.IsType<DocLink.Missing>(DocLinks.Resolve("wiki:README", "Setup.md", ["Setup.md"], bare));
        Assert.Equal(_vault, DocLinks.Vault(_docs));
    }
}
