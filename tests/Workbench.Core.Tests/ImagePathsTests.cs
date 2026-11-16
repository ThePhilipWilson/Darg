using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class ImagePathsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("wb-img-").FullName;
    string Make(string rel) { var p = Path.Combine(_dir, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, [0x89, 0x50]); return p; }

    [Fact]
    public void Forge_tools_json_with_escaped_backslashes()
    {
        var a = Make("out/mcp-20261117-0.png");
        var b = Make("out/mcp-20261117-1.png");
        var json = "{\n  \"saved\": [\n    \"" + a.Replace("\\", "\\\\") + "\",\n    \"" + b.Replace("\\", "\\\\") + "\"\n  ],\n  \"seed\": 42\n}";
        Assert.Equal([a, b], ImagePaths.Find(json, null));
    }

    [Fact]
    public void Plain_paths_forward_slashes_and_relative_ones()
    {
        var a = Make("art/card.webp");
        var b = Make("shot.jpg");
        var text = $"Wrote {a.Replace('\\', '/')} and ./shot.jpg, then art/card.webp again.";
        var found = ImagePaths.Find(text, _dir);
        Assert.Equal(2, found.Count);
        Assert.Equal(Path.GetFullPath(a), Path.GetFullPath(found[0]));
        Assert.Equal(Path.GetFullPath(b), Path.GetFullPath(found[1]));
    }

    [Theory]
    [InlineData("no images here")]
    [InlineData("missing.png is not on disk")]
    [InlineData("image/png is a media type, not a file")]
    public void Nothing_that_isnt_an_existing_file(string text) => Assert.Empty(ImagePaths.Find(text, _dir));

    public void Dispose() => Directory.Delete(_dir, true);
}
