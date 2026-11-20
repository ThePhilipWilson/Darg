using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Darg.Mobile.ViewModels;

namespace Darg.Mobile.Views;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
    }

    /// <summary>Android's back button (and gesture) goes a screen back; at the first screen it's left alone so the app closes.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested += OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBackRequested;
        base.OnDetachedFromVisualTree(e);
    }

    void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell && shell.Back()) e.Handled = true;
    }
}
