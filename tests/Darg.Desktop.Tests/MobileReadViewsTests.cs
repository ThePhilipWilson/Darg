using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Core;
using ArtifactHost = Darg.Desktop.ViewModels.ArtifactHost;
using Darg.Mobile;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Desktop.Tests;

/// <summary>AND-0008: the phone's Home, Notes, Docs and Reports, run headless against the real desktop app's API:
/// each section reads and draws, a doc's [[link]] tapped opens the doc it names, a report opens from its list.</summary>
public class MobileReadViewsTests
{
    [AvaloniaFact]
    public async Task Home_notes_docs_and_reports_open_and_draw_on_the_phone()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Docs, "Index.md"),
            "# Index\n\nStart with [[PLAN]], then the [setup guide](guide/setup.md) or [the site](https://example.com).\n\n" +
            "- **Board**: cards\n- **Docs**: this\n\n| Section | What |\n|---|---|\n| Home | overview |\n\n```pwsh\npwsh -File tools/test.ps1\n```\n");
        Directory.CreateDirectory(Path.Combine(app.Docs, "guide"));
        File.WriteAllText(Path.Combine(app.Docs, "guide", "setup.md"), "# Setup\n\nInstall it.\n");
        var notes = Path.Combine(Path.GetDirectoryName(app.Boards)!, "notes");
        NoteStore.Save(notes, new Note("20261120-0101-aaaa", "A pink thought about **phones**", "Loose", "pink", ["mobile"], "2026-11-20 01:01", ""));
        var reports = Directory.CreateDirectory(ReportStore.FolderFor("proj", app.Home)).FullName;
        File.WriteAllText(Path.Combine(reports, "20261120-0100-T-0001.md"),
            "---\ncard: T-0001\ntitle: Did the thing\nagent: x\ncommits: abc1234\nrepo: r\ndate: 2026-11-20 01:00\nread: false\n---\n\n## Asked\n\nFor the thing.\n\n## Done\n\nIt.\n");
        app.Card("Phone views", column: "In Progress");
        await app.Start();

        var store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));
        var parsed = Connection.ParseAddress(ArtifactHost.Server!.ApiUrl)!.Value;
        store.Save(new Connection(parsed.Address, parsed.Token!));
        var shell = new ShellViewModel(store);
        var window = Phone(shell);
        var projects = shell.OpenProjects();
        await DargApp.Wait(() => projects.Projects.Count == 1, "the project list");
        projects.OpenCommand.Execute(projects.Projects[0]);
        var page = Assert.IsType<ProjectPageViewModel>(shell.Page);
        Assert.Equal(["Home", "Board", "Agents", "Notes", "Docs", "Reports"], page.Tabs.Select(t => t.Section.Name));

        // Home first, as the desktop opens a project: agents, cards at work, unread reports, the repo's commits
        var home = Assert.IsType<HomeViewModel>(page.Content);
        await DargApp.Wait(() => home.Loaded, "Home to read the overview");
        Assert.Equal("Main: In Progress 1", home.BoardLine);
        Assert.Contains(home.Tiles, t => t.Heading == "In progress" && t.Rows.Any(r => r.Title.Contains("Phone views")));
        var unread = Assert.Single(home.Tiles.Single(t => t.Heading == "Unread reports").Rows);
        Assert.Equal("20261120-0100-T-0001", unread.Opens);
        Assert.False(unread.HasBadge); // a plain finished report has no badge, as in Reports; a QA one says "to check"
        Shot(window, app, "home");

        Pick(page, "Notes");
        var wall = Assert.IsType<NotesViewModel>(page.Content);
        await DargApp.Wait(() => wall.Loaded, "the notes to read");
        var note = Assert.Single(Assert.Single(wall.Groups).Notes);
        Assert.Equal("pink", note.Colour);
        Assert.Contains("#mobile", note.Meta);
        Shot(window, app, "notes");

        Pick(page, "Docs");
        var docs = Assert.IsType<DocsViewModel>(page.Content);
        await DargApp.Wait(() => docs.Loaded, "the docs to list");
        // a tree: the guide folder first and closed, the top-level docs under it
        Assert.Equal(["guide", "Index", "PLAN"], docs.Visible.Select(d => d.Name));
        docs.Open(docs.Visible[0]);
        Assert.Equal(["guide", "setup", "Index", "PLAN"], docs.Visible.Select(d => d.Name));
        Shot(window, app, "docs");

        docs.Open(docs.Visible.Single(d => d.Name == "Index"));
        var index = Assert.IsType<DocPageViewModel>(shell.Page);
        await DargApp.Wait(() => index.Text.Length > 0, "the doc to read");
        Assert.Equal("Index", shell.Title);
        await DargApp.Settle();
        var body = window.GetVisualDescendants().OfType<MarkdownView>().Single();
        var links = body.Links().ToList();
        Assert.Equal(["wiki:PLAN", "guide/setup.md", "https://example.com"], links.Select(l => (string)l.Tag!));
        Shot(window, app, "doc");

        // a tap on [[PLAN]] opens PLAN, as on the desktop
        Tap(window, links[0]);
        await DargApp.Wait(() => shell.Page is DocPageViewModel { Path: "PLAN.md" }, "a tap on [[PLAN]] to open it");
        var plan = (DocPageViewModel)shell.Page!;
        await DargApp.Wait(() => plan.Text.Length > 0, "PLAN to read");
        Assert.Equal("Plan", shell.Title);
        Assert.True(shell.Back());
        Assert.Same(index, shell.Page);
        Assert.Null(index.Follow("guide/setup.md")); // a relative link opens the doc too
        Assert.Equal("guide/setup.md", Assert.IsType<DocPageViewModel>(shell.Page).Path);
        shell.Back();
        Assert.Equal(new Uri("https://example.com"), index.Follow("https://example.com")); // the web goes to the phone's browser
        Assert.Null(index.Follow("wiki:Nowhere"));
        Assert.Contains("Nowhere", index.Status);

        // ✦ Fettle on a doc tells Fettle which doc is open (AND-0007's OpenDoc); from the page itself, none
        Assert.True(index.CanTalkToFettle);
        index.FettleCommand.Execute(null);
        var chat = Assert.IsType<FettleChatViewModel>(shell.Page);
        Assert.Equal("Fettle sees: Proj › Docs, doc Index.md", chat.Sees);
        shell.Back();
        Assert.Same(index, shell.Page);
        shell.Back();
        Assert.Same(page, shell.Page);
        page.FettleCommand.Execute(null);
        Assert.Equal("Fettle sees: Proj › Docs", chat.Sees);
        shell.Back();

        Pick(page, "Reports");
        var list = Assert.IsType<ReportsViewModel>(page.Content);
        await DargApp.Wait(() => list.Loaded, "the reports to list");
        var row = Assert.Single(list.Reports);
        Assert.True(row.Strong); // unread
        Shot(window, app, "reports");
        list.OpenCommand.Execute(row);
        var report = Assert.IsType<ReportPageViewModel>(shell.Page);
        await DargApp.Wait(() => report.Text.Length > 0, "the report to read");
        Assert.Equal("T-0001: Did the thing", shell.Title);
        Assert.Contains("abc1234", report.Details);
        Assert.Contains("## Asked", report.Text);
        Shot(window, app, "report");
        Assert.Contains("read: false", File.ReadAllText(Path.Combine(reports, "20261120-0100-T-0001.md"))); // still unread on the PC

        window.Close();
        Assert.Equal("", app.Crashes());
    }

    /// <summary>An answer this build can't read (a field's type changed on a newer desktop, say) shows as an error;
    /// it used to throw out of the fire-and-forget load and leave "Loading…" up for good, as Home once did on the emulator.</summary>
    [Fact]
    public async Task A_section_given_an_answer_it_cant_read_says_so_rather_than_loading_for_ever()
    {
        var folder = Directory.CreateTempSubdirectory("darg-phone-").FullName;
        try
        {
            var store = new ConnectionStore(Path.Combine(folder, "connection.json"));
            store.Save(new Connection("http://localhost:1", "t"));
            var answers = new Dictionary<string, string>
            {
                ["projects"] = "[]",
                ["overview"] = """{"board":"Main","repos":[{"name":"r","branch":"main","uncommitted":"four","commits":[]}]}""",
                ["doc"] = """{"title":["not","a","string"],"text":"x"}""",
            };
            var shell = new ShellViewModel(store, c => new DargClient(c, new Canned(answers)));
            var home = new HomeViewModel(shell, "proj");
            await home.Refresh();
            Assert.True(home.Bad);
            Assert.StartsWith("Couldn't read it:", home.Status);
            Assert.False(home.Busy);

            var doc = new DocPageViewModel(shell, "proj", "x.md");
            await doc.Refresh();
            Assert.True(doc.Bad);
            Assert.StartsWith("Couldn't read it:", doc.Status);
        }
        finally { Directory.Delete(folder, true); }
    }

    sealed class Canned(Dictionary<string, string> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(answers.GetValueOrDefault(request.RequestUri!.Segments[^1], "{}")),
            });
    }

    [Fact]
    public void Docs_list_as_a_tree_with_folders_first()
    {
        var tree = DocsViewModel.Tree(["README.md", "guide/setup.md", "guide/deep/more.md", "PLAN.md", "api.md"]);
        Assert.Equal(["guide 0", "deep 1", "more 2", "setup 1", "api 0", "PLAN 0", "README 0"], tree.Select(e => $"{e.Name} {e.Depth}"));
        Assert.Equal("guide/deep/more.md", tree[2].Path);
    }

    [Theory]
    [InlineData(@"C:\x\reports\20261120-0100-T-0001.md", "20261120-0100-T-0001")]
    [InlineData("/home/x/reports/20261120-0100-T-0001.md", "20261120-0100-T-0001")]
    public void A_report_id_comes_from_either_kind_of_path(string file, string id) => Assert.Equal(id, ReadSection.ReportId(file));

    static void Pick(ProjectPageViewModel page, string section) => page.PickCommand.Execute(page.Tabs.Single(t => t.Section.Name == section));

    static void Tap(Window window, Control target)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
                 ?? throw new InvalidOperationException("The link isn't on screen.");
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static Window Phone(ShellViewModel shell)
    {
        var window = new Window { Width = 412, Height = 860, Content = new ShellView { DataContext = shell } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static void Shot(Window window, DargApp app, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
#pragma warning disable CS0618
        frame.Save(Path.Combine(app.Folder, "phone-read-" + name + ".png"));
#pragma warning restore CS0618
    }
}
