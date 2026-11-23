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

    /// <summary>Reads a QR with the phone's camera and gives its text (null if cancelled): set by the Android head,
    /// null elsewhere, which hides the Scan button (paste the link instead).</summary>
    public static Func<Task<string?>>? Scanner { get; set; }

    /// <summary>What this phone calls itself when it pairs: shown in Darg's Settings next to Revoke (Android: its model).</summary>
    public static string DeviceName { get; set; } = Environment.MachineName;

    /// <summary>The running app's shell, so a pairing link opened from outside (the camera app) reaches it.</summary>
    public static ShellViewModel? Shell { get; private set; }

    static string? _pendingLink;

    /// <summary>A darg://pair link arrived from outside the app: pairs now, or as soon as the shell exists.</summary>
    public static void OpenLink(string link)
    {
        if (Shell is { } shell) _ = shell.PairWith(link);
        else _pendingLink = link;
    }

    static ShellViewModel NewShell()
    {
        var shell = Shell = new ShellViewModel(Store());
        if (_pendingLink is { } link) { _pendingLink = null; _ = shell.PairWith(link); }
        return shell;
    }

    static ConnectionStore Store() => ConnectionFile is { } f ? new ConnectionStore(f) : ConnectionStore.Default();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyTheme(this, PhoneData.Beside(Store()).LoadTheme()); // the theme picked last time
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
            activity.MainViewFactory = () => new ShellView { DataContext = NewShell() };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new ShellView { DataContext = NewShell() };
        base.OnFrameworkInitializationCompleted();
    }
}
