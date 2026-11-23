using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Darg.Desktop.ViewModels;
using Darg.Mobile;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Desktop.Tests;

/// <summary>AND-0003: the phone app's shell, run headless against the real desktop app's API over HTTP, as the
/// phone reaches it through adb reverse: connect, list the projects, open one, go back.</summary>
public class MobileShellTests
{
    [AvaloniaFact]
    public async Task The_phone_connects_to_the_desktop_and_lists_its_projects()
    {
        using var app = DargApp.Make();
        await app.Start();
        var apiUrl = ArtifactHost.Server!.ApiUrl;
        var store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));

        var shell = new ShellViewModel(store);
        var window = Phone(shell);
        var connect = Assert.IsType<ConnectViewModel>(shell.Page);
        Assert.False(shell.CanGoBack);

        // pasting the desktop's API address fills the token in from it
        connect.Address = apiUrl;
        Assert.Equal(apiUrl[(apiUrl.LastIndexOf('/') + 1)..], connect.Token);
        Assert.DoesNotContain("/api/", connect.Address);
        Shot(window, app, "1-connect");

        var connecting = connect.Connect();
        await DargApp.Wait(() => connecting.IsCompleted, "the phone to connect");
        await connecting;
        var projects = Assert.IsType<ProjectsViewModel>(shell.Page);
        var proj = Assert.Single(projects.Projects);
        Assert.Equal("Proj", proj.Name);
        Assert.Equal("proj", proj.Id);
        Assert.Equal(store.Load(), new Connection(connect.Address, connect.Token));
        await DargApp.Settle();
        Shot(window, app, "2-projects");

        shell.Show(new ProjectPageViewModel(proj));
        var page = Assert.IsType<ProjectPageViewModel>(shell.Page);
        // the phone's sections the desktop says this project has (it has a board and docs), Home, Agents and Reports always
        var tabs = page.Tabs.Select(t => t.Section.Name).ToList();
        Assert.Equal(["Home", "Board", "Agents", "Docs", "Reports"], tabs.Where(n => n != "Notes"));
        Assert.Equal(proj.Sections.Contains("Notes"), tabs.Contains("Notes"));
        Assert.Equal("Home", page.Section.Name);
        Assert.True(shell.CanGoBack);
        page.PickCommand.Execute(page.Tabs.First(t => t.Section.Name == "Agents"));
        Assert.True(page.Tabs.Single(t => t.On).Section.Name == "Agents");
        await DargApp.Settle();
        Shot(window, app, "3-project");

        Assert.True(shell.Back());
        Assert.IsType<ProjectsViewModel>(shell.Page);
        Assert.False(shell.Back()); // the first screen: Android's back leaves the app

        // opened again, the saved connection goes straight to the projects
        var again = new ShellViewModel(store);
        var list = Assert.IsType<ProjectsViewModel>(again.Page);
        await DargApp.Wait(() => list.Projects.Count == 1, "the saved connection to list the projects");
        window.Close();
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_wrong_token_is_refused_and_says_so()
    {
        using var app = DargApp.Make();
        await app.Start();
        var address = Connection.ParseAddress(ArtifactHost.Server!.ApiUrl)!.Value.Address;
        var store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));
        var shell = new ShellViewModel(store);
        var connect = (ConnectViewModel)shell.Page!;
        connect.Address = address;
        connect.Token = new string('0', 32);

        var connecting = connect.Connect();
        await DargApp.Wait(() => connecting.IsCompleted, "the refusal");
        Assert.IsType<ConnectViewModel>(shell.Page);
        Assert.True(connect.Bad);
        Assert.Contains("refused the token", connect.Status);
        Assert.Null(store.Load());
    }

    [Theory]
    [InlineData("http://localhost:47817/api/0123456789abcdef0123456789abcdef", "http://localhost:47817", "0123456789abcdef0123456789abcdef")]
    [InlineData("192.168.1.20", "http://192.168.1.20:47817", null)]
    [InlineData("pc.tailnet.ts.net:5000", "http://pc.tailnet.ts.net:5000", null)]
    [InlineData("https://pc.example:443/api/tok", "https://pc.example:443", "tok")]
    [InlineData("  http://localhost:47817/  ", "http://localhost:47817", null)]
    public void An_address_is_read_with_or_without_scheme_port_and_token(string text, string address, string? token)
    {
        var parsed = Connection.ParseAddress(text);
        Assert.NotNull(parsed);
        Assert.Equal(address, parsed.Value.Address);
        Assert.Equal(token, parsed.Value.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://pc/x")]
    public void Not_an_address_is_null(string text) => Assert.Null(Connection.ParseAddress(text));

    /// <summary>The phone's shell in a phone-sized window, so the shots look as the phone would.</summary>
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
        frame.Save(Path.Combine(app.Folder, "phone-" + name + ".png"));
#pragma warning restore CS0618
    }
}
