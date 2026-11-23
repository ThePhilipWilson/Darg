using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
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
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        top.BackRequested += OnBackRequested;
        _pane = top.InputPane;
        if (_pane is not null)
        {
            _pane.StateChanged += OnInputPane;
            FitAbove(_pane.State == InputPaneState.Open ? _pane.OccludedRect : default);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested -= OnBackRequested;
        if (_pane is not null) _pane.StateChanged -= OnInputPane;
        _pane = null;
        base.OnDetachedFromVisualTree(e);
    }

    IInputPane? _pane;

    void OnInputPane(object? sender, InputPaneStateEventArgs e) =>
        FitAbove(e.NewState == InputPaneState.Open ? e.EndRect : default);

    /// <summary>Keeps every screen above the phone's soft keyboard (AND-0004's QA: it covered the board's quick-add box and
    /// what was typed in it). Android draws the keyboard over the app rather than shrinking it, so the shell lifts its
    /// content by however much of it the keyboard covers: the bars docked at the bottom (quick-add, Fettle's box, the
    /// sections) then sit right above the keyboard. <paramref name="keyboard"/> is where the keyboard is, in the
    /// window's coordinates; an empty rect (closed, or floating) puts everything back. The lift goes on the panel inside,
    /// not on this view's Padding: the TopLevel keeps the status and navigation bars' insets there (AutoSafeAreaPadding),
    /// and overwriting them put the header under the clock and the tabs under the gesture bar.</summary>
    public void FitAbove(Rect keyboard)
    {
        double lift = 0;
        if (keyboard.Height > 0 && TopLevel.GetTopLevel(this) is { } top
            && Body.TranslatePoint(new Point(0, Body.Bounds.Height + Body.Margin.Bottom), top) is { } bottom)
            lift = Math.Clamp(bottom.Y - keyboard.Top, 0, Body.Bounds.Height + Body.Margin.Bottom);
        Body.Margin = new Thickness(0, 0, 0, lift);
    }

    void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell && shell.Back()) e.Handled = true;
    }
}
