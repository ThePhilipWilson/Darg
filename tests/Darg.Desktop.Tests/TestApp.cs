using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Darg.Desktop.Tests.TestApp))]
// One app, one DARG_HOME at a time: tests run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
// One Application and one UI dispatcher for the whole run, as the real app has for its life. The default (PerTest)
// rebuilds both before every test, and work a test left running (an agent's output, a timer, a file watcher)
// that posts to Dispatcher.UIThread from another thread can land mid-rebuild: about one run in six failed in
// HeadlessUnitTestSession.EnsureIsolatedApplication with "a different thread owns it" (WOR-0127). Darg's own
// statics (the artifact server, the usage meter) outlived each test anyway; every test makes its own instance.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace Darg.Desktop.Tests;

/// <summary>The real App (App.axaml, themes, templates) on Avalonia's headless platform, drawn with Skia so
/// a test can save what the window shows. Set up before the app reads anything from its instance folder.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Sandbox.Init();
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
    }
}
