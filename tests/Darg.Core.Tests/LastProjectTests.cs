using Darg.Core;

namespace Darg.Core.Tests;

public class LastProjectTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wb-last-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    [Fact]
    public void OpensToTheProjectSavedLastElseTheFirst()
    {
        string[] projects = ["site", "alpha", "darg"];
        Assert.Equal("site", LastProject.Pick(projects, p => p, dir)); // nothing saved yet
        LastProject.Write("Darg", dir);
        Assert.Equal("darg", LastProject.Pick(projects, p => p, dir)); // ids ignore case
        LastProject.Write(null, dir); // nothing selected (the tabs being rebuilt) keeps the last one
        Assert.Equal("Darg", LastProject.Read(dir));
        LastProject.Write("gone", dir); // a project since removed falls back to the first
        Assert.Equal("site", LastProject.Pick(projects, p => p, dir));
        Assert.Null(LastProject.Pick(Array.Empty<string>(), p => p, dir));
    }
}
