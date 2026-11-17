using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Darg.Desktop.Tests.TestApp))]
// One app, one DARG_HOME at a time: tests run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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
