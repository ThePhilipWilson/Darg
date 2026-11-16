using Darg.Core;

namespace Darg.Core.Tests;

public class ThemesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wb-settings-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    [Fact]
    public void EveryThemeColoursEveryKeyAndNothingElse()
    {
        foreach (var t in Themes.All)
        {
            Assert.Equal(Themes.Keys.Keys.Order(), t.Colours.Keys.Order());
            foreach (var (key, hex) in t.Colours)
                Assert.Matches("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", hex);
        }
        Assert.Equal(Themes.All.Count, Themes.All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void AnUnknownThemeIsDarkButFindSaysNoneForTheApi()
    {
        Assert.Same(Themes.Girlypop, Themes.Get("GirlyPop"));
        Assert.Same(Themes.Dark, Themes.Get("neon"));
        Assert.Same(Themes.Dark, Themes.Get(null));
        Assert.Same(Themes.Light, Themes.Find(" light "));
        Assert.Same(Themes.Girlypop, Themes.Find("Girlypop"));
        Assert.Null(Themes.Find("neon"));
    }

    [Fact]
    public void SettingsSaveBesideTheInstanceAndDefaultToDark()
    {
        Assert.Equal("dark", AppSettings.Load(dir).Theme); // no file yet
        Assert.Null(new AppSettings { Theme = "girlypop" }.Save(dir));
        Assert.Equal("girlypop", AppSettings.Load(dir).Theme);
        File.WriteAllText(AppSettings.FilePath(dir), "{ not json");
        Assert.Equal("dark", AppSettings.Load(dir).Theme); // a broken file doesn't stop the app opening
    }
}
