using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Darg.Core;
using Darg.Mobile.ViewModels;
using Darg.Mobile.Views;

namespace Darg.Mobile;

public partial class App : Application
{
    /// <summary>Where the connection is kept: set by the host before the app starts (Android: its files folder),
    /// else LocalApplicationData.</summary>
    public static string? ConnectionFile { get; set; }

    static ConnectionStore Store() => ConnectionFile is { } f ? new ConnectionStore(f) : ConnectionStore.Default();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyTheme(this, Themes.Dark);
    }

    /// <summary>A Core theme's colours as brushes in the app's resources (the desktop's ThemeManager does the same).</summary>
    public static void ApplyTheme(Application app, Theme theme)
    {
        foreach (var (key, hex) in theme.Colours) app.Resources[key] = new SolidColorBrush(Color.Parse(hex));
        app.RequestedThemeVariant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Android hands over its view factory (IActivityApplicationLifetime); other single-view hosts take a view.
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new ShellView { DataContext = new ShellViewModel(Store()) };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new ShellView { DataContext = new ShellViewModel(Store()) };
        base.OnFrameworkInitializationCompleted();
    }
}
