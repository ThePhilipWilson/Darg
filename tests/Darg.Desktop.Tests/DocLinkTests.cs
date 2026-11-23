using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0192: a link to a .md file in a reply (agent transcripts and Fettle have no link handler of their own)
/// opens in the Docs viewer, relative to the project's repo or docs folder or absolute, instead of Windows' default app.</summary>
public class DocLinkTests
{
    static void Seed(DargApp app, string reply) =>
        AgentStore.Save(AgentStore.Folder("~bench", app.Home), new AgentRecord
        {
            Id = "chat-docs", Name = "Doc chat", Cwd = app.Home, Project = "proj", Created = DateTime.Now, Updated = DateTime.Now,
            Items = [new ItemRecord { Kind = "user", Text = "where is the proposal" }, new ItemRecord { Kind = "text", Text = reply }],
        });

    static async Task Show(DargApp app, string reply)
    {
        Seed(app, reply);
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Agents" });
        var fettle = app.Main.Mascot;
        fettle.ShowProject(null);
        fettle.SelectedChat = fettle.Chats.Single(c => c.Id == "chat-docs");
        fettle.Open = true;
        await DargApp.Settle(200);
    }

    static void Click(DargApp app, string target)
    {
        var link = app.Window.GetVisualDescendants().OfType<TextBlock>().Single(t => ToolTip.GetTip(t) as string == target && t.IsEffectivelyVisible);
        var top = TopLevel.GetTopLevel(link)!;
        var at = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), top)!.Value;
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, at);
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, at, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, at, Avalonia.Input.MouseButton.Left);
    }

    [AvaloniaFact]
    public async Task A_relative_md_link_in_a_reply_opens_the_file_in_Docs_from_the_repo()
    {
        using var app = DargApp.Make();
        Directory.CreateDirectory(Path.Combine(app.Repo, "docs", "Work"));
        File.WriteAllText(Path.Combine(app.Repo, "docs", "Work", "PROPOSAL.md"), "# The proposal\n\nSplit the backlog.\n");
        await Show(app, "See [the proposal](docs/Work/PROPOSAL.md) for it.");
        Click(app, "docs/Work/PROPOSAL.md");
        await DargApp.Settle();

        Assert.Equal(Section.Docs, app.Project.Section);
        var docs = app.Project.Docs!;
        Assert.EndsWith("docs/Work/PROPOSAL.md", docs.Selected!.Path.Replace('\\', '/'));
        Assert.StartsWith("Outside the docs folder", docs.Status);
        app.Shot("doc-open");
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task An_absolute_md_link_and_one_in_the_docs_folder_open_in_Docs()
    {
        using var app = DargApp.Make();
        var abs = Path.Combine(app.Repo, "README.md").Replace('\\', '/');
        await Show(app, $"The [readme]({abs}) and [the plan](PLAN.md).");
        Click(app, abs);
        await DargApp.Settle();
        Assert.Equal(Section.Docs, app.Project.Section);
        Assert.Equal(abs, app.Project.Docs!.Selected!.Path, ignoreCase: true);

        await app.Api("show", new { project = "proj", section = "Agents" });
        Click(app, "PLAN.md");
        await DargApp.Settle();
        Assert.Equal(Section.Docs, app.Project.Section);
        Assert.Equal("PLAN.md", app.Project.Docs!.Selected!.Path);
    }

    [AvaloniaFact]
    public async Task A_missing_md_file_says_so_in_the_Docs_status_line()
    {
        using var app = DargApp.Make();
        await Show(app, "Gone: [x](docs/Nope.md).");
        Click(app, "docs/Nope.md");
        await DargApp.Settle();
        Assert.Equal(Section.Docs, app.Project.Section);
        Assert.StartsWith("No doc at", app.Project.Docs!.Status);
        Assert.Contains("Nope.md", app.Project.Docs.Status);
    }

    [Theory]
    [InlineData("docs/Work/X.md", true)]
    [InlineData("D:/repo/X.md#part", true)]
    [InlineData("file:///D:/repo/X.md", true)]
    [InlineData("https://example.com/X.md", false)]
    [InlineData("mailto:a@b.c", false)]
    [InlineData("src/Program.cs", false)]
    [InlineData("card:WOR-0001", false)]
    [InlineData("wiki:Note", false)]
    public void Only_markdown_files_are_doc_links(string link, bool expected) =>
        Assert.Equal(expected, Views.MarkdownView.IsDocLink(link));
}
