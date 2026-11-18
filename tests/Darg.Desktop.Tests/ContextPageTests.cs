using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0145: a Context page per project shows the CLAUDE.md files, memory and Darg's additions agents start with.</summary>
public class ContextPageTests
{
    [AvaloniaFact]
    public async Task The_context_page_lists_the_repos_claude_md_and_shows_its_text()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Repo, "CLAUDE.md"), "# Rules\n\nAlways say pineapple.\n");
        await app.Start();

        await app.Api("show", new { project = "proj", section = "Context" });

        Assert.Equal(Section.Context, app.Project.Section);
        var ctx = app.Project.Context;
        await app.Until(() => ctx.Items.Any(i => i.Group == ContextFiles.InstructionsGroup && i.Path.EndsWith("CLAUDE.md") && i.Path.StartsWith(app.Repo)), "the repo's CLAUDE.md listed");
        Assert.Contains(ctx.Items, i => i.Group == ContextFiles.DargGroup);
        ctx.Selected = ctx.Items.First(i => i.Path.StartsWith(app.Repo) && i.Path.EndsWith("CLAUDE.md"));
        Assert.Contains("pineapple", ctx.Text);
        app.Shot("context");
        Assert.Equal("", app.Crashes());
    }
}
