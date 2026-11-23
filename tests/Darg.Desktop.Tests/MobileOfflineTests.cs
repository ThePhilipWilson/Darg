using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Core;
using ArtifactHost = Darg.Desktop.ViewModels.ArtifactHost;
using Darg.Mobile;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Desktop.Tests;

/// <summary>AND-0009: with the PC off the phone shows what the desktop last answered, under a banner saying so, and sends
/// nothing; it notices the PC is back by itself. And it wears the desktop's three themes. Run headless against the
/// real desktop app's API, with a switch in the phone's connection that plays the PC going off.</summary>
public class MobileOfflineTests
{
    const string Decision =
        "Try it on the phone.\n\n## Decision\n\n**Question:** Does the offline banner read right?\n**Asked by:** agent x, 2026-11-23 00:00\n\n" +
        "- **Fine** (recommended) → done: keep it.\n- **Reword it** → back: say how.\n\n**Context:** it shows when the PC is off.\n";

    /// <summary>The phone's way to the PC: through to the real app while on; off, it fails as a switched-off PC does.</summary>
    sealed class PcSwitch : DelegatingHandler
    {
        public volatile bool Off;
        public int Sent;
        public PcSwitch() : base(new HttpClientHandler()) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Off) throw new HttpRequestException("No route to host");
            Interlocked.Increment(ref Sent);
            return base.SendAsync(request, ct);
        }
    }

    static ConnectionStore Saved(DargApp app)
    {
        var parsed = Connection.ParseAddress(ArtifactHost.Server!.ApiUrl)!.Value;
        var store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));
        store.Save(new Connection(parsed.Address, parsed.Token!));
        return store;
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
        frame.Save(Path.Combine(app.Folder, "phone-offline-" + name + ".png"));
#pragma warning restore CS0618
    }

    static Control? Named(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => Avalonia.Automation.AutomationProperties.GetName(c) == name);
    }

    [AvaloniaFact]
    public async Task With_the_PC_off_the_phone_shows_what_it_last_read_under_a_banner_and_sends_nothing()
    {
        using var app = DargApp.Make();
        var card = app.Card("offline banner", BoardStore.AttentionName, Decision);
        await app.Start();
        var store = Saved(app);

        // online: Needs you, the projects and a project's docs are read once
        var pc = new PcSwitch();
        var shell = new ShellViewModel(store, c => new DargClient(c, pc));
        var needs = Assert.IsType<NeedsYouViewModel>(shell.Page);
        await needs.Refresh();
        Assert.True(needs.Items.Count == 1, $"needs you: {needs.Items.Count}, {needs.Status}");
        var projects = shell.OpenProjects();
        await DargApp.Wait(() => !projects.Busy && projects.Projects.Count == 1, "the projects to be read");
        projects.OpenCommand.Execute(Assert.Single(projects.Projects));
        var page = Assert.IsType<ProjectPageViewModel>(shell.Page);
        page.Section = ProjectPageViewModel.All.Single(s => s.Name == "Docs");
        var docs = Assert.IsType<DocsViewModel>(page.Content);
        await DargApp.Wait(() => !docs.Busy && docs.Visible.Count > 0, "the docs to be read");
        var docNames = docs.Visible.Select(d => d.Name).ToList();
        Assert.False(shell.Offline);

        // the PC goes off and the app is started again: everything read before shows, marked offline
        pc.Off = true;
        var off = new ShellViewModel(store, c => new DargClient(c, pc));
        var window = Phone(off);
        var needsOff = Assert.IsType<NeedsYouViewModel>(off.Page);
        await needsOff.Refresh();
        Assert.Equal(card.Id, Assert.Single(needsOff.Items).Item.Card);
        Assert.False(needsOff.Bad);
        Assert.True(off.Offline);
        Assert.Contains("Offline: the PC isn't answering. What shows is from", off.OfflineText);
        Assert.True(Named(window, "Offline")!.IsEffectivelyVisible);
        Shot(window, app, "1-needs-you");

        // a tap sends nothing, and says so; the card is as it was
        var row = needsOff.Items.Single();
        row.Questions.Single().Options.First(o => o.Label == "Fine").TapCommand.Execute(null);
        await DargApp.Wait(() => row.Error.Length > 0, "the decision to fail");
        Assert.StartsWith("Nothing was sent:", row.Error);
        Assert.Equal(BoardStore.AttentionName, app.Read(card.Id).Status);

        // the projects and the docs still open from what was kept, without asking the PC
        var projectsOff = off.OpenProjects();
        await DargApp.Wait(() => !projectsOff.Busy && projectsOff.Projects.Count == 1, "the kept projects to show");
        Assert.False(projectsOff.Bad);
        projectsOff.OpenCommand.Execute(Assert.Single(projectsOff.Projects));
        var pageOff = Assert.IsType<ProjectPageViewModel>(off.Page);
        pageOff.Section = ProjectPageViewModel.All.Single(s => s.Name == "Docs");
        var docsOff = Assert.IsType<DocsViewModel>(pageOff.Content);
        await DargApp.Wait(() => !docsOff.Busy, "the docs to show");
        Assert.Equal(docNames, docsOff.Visible.Select(d => d.Name));
        Assert.False(docsOff.Bad);
        Shot(window, app, "2-docs");

        // a section never read has nothing kept: it says why, and the banner says nothing is kept
        pageOff.Section = ProjectPageViewModel.All.Single(s => s.Name == "Reports");
        var reports = Assert.IsType<ReportsViewModel>(pageOff.Content);
        await DargApp.Wait(() => !reports.Busy, "the reports to fail");
        Assert.True(reports.Bad);
        Assert.Contains("Can't reach", reports.Status);

        // the PC comes back: Try again clears the banner and reads the screen again
        pc.Off = false;
        var before = pc.Sent;
        await off.TryAgainCommand.ExecuteAsync(null);
        Assert.False(off.Offline);
        Assert.False(Named(window, "Offline")!.IsEffectivelyVisible);
        await DargApp.Wait(() => !reports.Busy && !reports.Bad, "the reports to be read again");
        Assert.True(pc.Sent > before + 1); // the try, and the section's read
        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_a_poll_answers_from_the_phone_and_notices_the_PC_coming_back()
    {
        using var app = DargApp.Make();
        app.Card("first", BoardStore.AttentionName, Decision);
        await app.Start();
        var store = Saved(app);
        var pc = new PcSwitch();
        var shell = new ShellViewModel(store, c => new DargClient(c, pc));
        var needs = Assert.IsType<NeedsYouViewModel>(shell.Page);
        await needs.Refresh();

        var was = DargClient.ProbeEvery;
        DargClient.ProbeEvery = TimeSpan.FromMilliseconds(300);
        try
        {
            pc.Off = true;
            await needs.Poll();
            Assert.True(shell.Offline);
            // while off, polls answer from the phone at once rather than waiting on the PC each time
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 5; i++) await needs.Poll();
            Assert.True(sw.ElapsedMilliseconds < 2000, $"5 offline polls took {sw.ElapsedMilliseconds} ms");
            Assert.Single(needs.Items);

            // the PC comes back with something new: a later poll's background try notices, and the screen is read again
            app.Card("second", BoardStore.AttentionName, Decision);
            pc.Off = false;
            await DargApp.Wait(() => { _ = needs.Poll(); return !shell.Offline; }, "the phone to notice the PC is back");
            await DargApp.Wait(() => needs.Items.Count == 2, "the new card to show");
            Assert.Equal("", shell.OfflineText);
        }
        finally { DargClient.ProbeEvery = was; }
    }

    [AvaloniaFact]
    public async Task A_refusal_is_not_offline_and_forgetting_the_PC_forgets_what_it_said()
    {
        using var app = DargApp.Make();
        await app.Start();
        var store = Saved(app);
        var shell = new ShellViewModel(store, c => new DargClient(c, new PcSwitch()));
        var projects = shell.OpenProjects();
        await DargApp.Wait(() => !projects.Busy && projects.Projects.Count == 1, "the projects to be read");
        Assert.True(Directory.EnumerateFiles(shell.Data.Answers.Folder).Any());

        // a wrong token is answered (refused): an error, never the offline banner or a kept answer
        var bad = new DargClient(new Connection(store.Load()!.Address, "ffffffffffffffffffffffffffffffff"), new PcSwitch()) { Cache = shell.Data.Answers };
        var e = await Assert.ThrowsAsync<DargClientException>(() => bad.Projects());
        Assert.False(e.Unreachable);
        Assert.False(bad.Offline);

        shell.OpenSettingsCommand.Execute(null);
        Assert.Equal("Settings", shell.Title);
        Assert.IsType<ConnectViewModel>(shell.Page).ForgetCommand.Execute(null);
        Assert.False(Directory.Exists(shell.Data.Answers.Folder));
        Assert.Equal("Connect to Darg", shell.Title);
    }

    [AvaloniaFact]
    public async Task The_theme_picked_restyles_the_app_and_is_there_next_run()
    {
        using var app = DargApp.Make();
        await app.Start();
        var store = Saved(app);
        var shell = new ShellViewModel(store, c => new DargClient(c, new PcSwitch()));
        Assert.Equal(Themes.Dark, shell.Theme);
        shell.OpenSettingsCommand.Execute(null);
        var settings = Assert.IsType<ConnectViewModel>(shell.Page);
        Assert.Equal(["Dark", "Light", "Girlypop"], settings.Themes.Select(t => t.Name));
        var window = Phone(shell);
        try
        {
            // not Dark first: the phone starts on it, so picking it first changes nothing and applies nothing, and the test
            // app is shared, so its colours may be whatever an earlier test left
            foreach (var theme in new[] { Themes.Light, Themes.Dark, Themes.Girlypop })
            {
                // picked on the settings screen, as a tap does
                var picker = Assert.IsType<ListBox>(Named(window, "Theme"));
                picker.SelectedItem = theme;
                Assert.Equal(theme, shell.Theme);
                var bg = (SolidColorBrush)Application.Current!.Resources["Bg"]!;
                Assert.Equal(Color.Parse(theme.Colours["Bg"]), bg.Color);
                Assert.Equal(theme.IsLight ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
                Shot(window, app, "theme-" + theme.Id);
            }
            Assert.Equal(Themes.Girlypop, new ShellViewModel(store, c => new DargClient(c, new PcSwitch())).Theme);
            Assert.Equal(Themes.Girlypop, PhoneData.Beside(store).LoadTheme());
        }
        finally
        {
            shell.Theme = Themes.Dark; // the shared app goes back as it was
            window.Close();
        }
    }
}
