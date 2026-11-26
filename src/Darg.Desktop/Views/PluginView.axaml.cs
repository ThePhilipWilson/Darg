using Avalonia;
using Avalonia.Controls;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

public partial class PluginView : UserControl
{
    public PluginView() => InitializeComponent();

    // the page takes the theme's colours when it loads, so a theme switch reloads it
    void ThemeChanged() { if (DataContext is PluginSection { HasPage: true } s) s.Reload(); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += ThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= ThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }
}
