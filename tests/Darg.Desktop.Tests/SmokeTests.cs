using Avalonia.Headless.XUnit;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>The app opens and every section draws without a crash, with a picture of each for QA to look at.</summary>
public class SmokeTests
{
    [AvaloniaFact]
    public async Task Opens_on_the_project_home()
    {
        using var app = DargApp.Make();
        await app.Start();
        Assert.Equal(Section.Home, app.Project.Section);
        Assert.Contains("Proj › Home", await app.Api("where"));
        Assert.Equal("", app.Crashes());
    }

    // Preview and Terminal start processes (a dev server, a shell) and the Artifacts web view is a native
    // window, so those are left to their own tests.
    [AvaloniaTheory]
    [InlineData(Section.Home)]
    [InlineData(Section.Board)]
    [InlineData(Section.Notes)]
    [InlineData(Section.Docs)]
    [InlineData(Section.Agents)]
    [InlineData(Section.Flows)]
    [InlineData(Section.Reports)]
    [InlineData(Section.Mistakes)]
    [InlineData(Section.Code)]
    public async Task Every_section_draws(Section section)
    {
        using var app = DargApp.Make($"{nameof(Every_section_draws)}-{section}");
        app.Card("a card in progress", "In Progress", "Some words about it.");
        app.Card("a card waiting for QA", "QA Ready");
        app.Card("a card in the backlog");
        await app.Start();
        await app.Api("show", new { project = "proj", section = section.ToString() });
        Assert.Equal(section, app.Project.Section);
        var shot = app.Shot(section.ToString());
        Assert.True(new FileInfo(shot).Length > 0);
        Assert.Equal("", app.Crashes());
    }
}
