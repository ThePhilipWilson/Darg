using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0242: the installed copy's header said only ⟳, as like the live copy's ⟳ live as made no difference, so a
/// finished card that never reached it (the installed copy doesn't hot reload) was a surprise. The header now names the copy,
/// and the installed one looks at its repo again on its own, so work that lands there shows as • without opening the menu.</summary>
public class RestartHeaderTests
{
    /// <summary>An install folder published from a repo: source.txt naming it, and the stamp of the source it was built from.</summary>
    static (string install, string repo, string src) Installed()
    {
        var root = Path.Combine(Path.GetTempPath(), "darg-restart-" + Guid.NewGuid().ToString("N")[..8]);
        var repo = Path.Combine(root, "repo");
        var src = Path.Combine(repo, "src", "Darg.Desktop");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Darg.Desktop.csproj"), "<Project/>");
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, Updater.SourceFile), repo);
        File.WriteAllText(Path.Combine(install, Updater.StampFile), Updater.SourceStamp(repo).ToString());
        return (install, repo, src);
    }

    [AvaloniaFact]
    public async Task The_installed_copy_says_so_and_shows_changes_landing_in_its_repo_without_the_menu()
    {
        var (install, _, src) = Installed();
        var vm = new RestartViewModel(() => [], () => { }, install);

        Assert.Equal("installed", vm.CopyName);
        Assert.Equal("⟳ installed", vm.HeaderLabel);
        Assert.Contains("doesn't hot reload", vm.HeaderTip);
        Assert.Contains("doesn't hot reload", vm.SourceNote);

        // a card agent merges into the repo: the next look marks the header, with no menu opened
        var file = Path.Combine(src, "New.cs");
        File.WriteAllText(file, "class New {}");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        await vm.CheckSource();

        Assert.True(vm.HasChanges);
        Assert.Equal("⟳ installed •", vm.HeaderLabel);
        Assert.Contains("has changes this copy doesn't", vm.HeaderTip);
    }

    [AvaloniaFact]
    public void A_live_copy_and_a_debug_copy_are_named_too()
    {
        var (install, _, _) = Installed();
        var live = new RestartViewModel(() => [], () => { }, install, new LiveCopy(install, "main", "live.ps1", null));
        Assert.Equal("live", live.CopyName);
        Assert.StartsWith("⟳ live", live.HeaderLabel);

        var bare = Path.Combine(Path.GetTempPath(), "darg-restart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(bare);
        var debug = new RestartViewModel(() => [], () => { }, bare);
        Assert.Equal("debug", debug.CopyName);
        Assert.Equal("⟳ debug", debug.HeaderLabel);
        Assert.Contains("doesn't hot reload", debug.HeaderTip);
    }

    [AvaloniaFact]
    public void A_restart_with_agents_mid_turn_says_work_agents_carry_on()
    {
        var (install, _, _) = Installed();
        var vm = new RestartViewModel(() => ["\"RS-0001 work\""], () => { }, install);
        vm.Refresh();
        Assert.Contains("carry on by themselves", vm.Warning);
    }

    [AvaloniaFact]
    public async Task The_app_under_test_names_itself_a_debug_copy_in_the_header()
    {
        using var app = DargApp.Make();
        await app.Start();
        var button = app.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RestartButton");
        Assert.Equal("⟳ debug", button.Content);
        Assert.Contains("debug copy", ToolTip.GetTip(button) as string);
        app.Shot("header");
    }
}
