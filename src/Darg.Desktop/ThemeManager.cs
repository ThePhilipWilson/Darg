using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Darg.Core;

namespace Darg.Desktop;

/// <summary>Puts a <see cref="Theme"/>'s colours into the app's resources as brushes (views take them
/// with DynamicResource, so a switch shows at once), tells Fluent whether to draw light or dark and with
/// which accent, and says so to the code-built views (markdown, the code editor) that pick colours themselves.</summary>
public static class ThemeManager
{
    public static Theme Current { get; private set; } = Themes.Dark;

    /// <summary>Raised on the UI thread after a theme is applied.</summary>
    public static event Action? Changed;

    public static void Apply(Theme theme)
    {
        if (Application.Current is not { } app) return;
        foreach (var (key, hex) in theme.Colours)
            app.Resources[key] = new SolidColorBrush(Color.Parse(hex));

        // Fluent's palettes take only Light and Dark as keys (a custom variant throws), so a theme's own accent
        // goes in its base variant's palette. In Avalonia 12.1.6 only a change of a palette's Accent reaches the
        // controls (ColorPaletteResources.OnPropertyChanged raises it); taking a palette out is silent, and other
        // palette colours (BaseHigh) are fixed once Fluent's brushes are first drawn. So the palette stays once
        // made, and an accent of default hands back to the system accent.
        var variant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent
            && (theme.FluentAccent is not null || fluent.Palettes.ContainsKey(variant)))
        {
            if (!fluent.Palettes.TryGetValue(variant, out var palette)) fluent.Palettes[variant] = palette = new ColorPaletteResources();
            palette.Accent = theme.FluentAccent is { } hex ? Color.Parse(hex) : default;
        }
        app.RequestedThemeVariant = variant;
        Current = theme;
        Changed?.Invoke();
    }

    /// <summary>The current theme's brush for a key, for views built in code.</summary>
    public static IBrush Brush(string key) =>
        Application.Current?.Resources.TryGetResource(key, null, out var v) == true && v is IBrush b ? b : Brushes.Magenta;
}
