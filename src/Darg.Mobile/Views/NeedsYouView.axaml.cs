using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Darg.Mobile.ViewModels;

namespace Darg.Mobile.Views;

/// <summary>The Needs you screen (AND-0010). It reads the desktop again each time it comes on screen and every
/// <see cref="Every"/> while it stays there; another screen over it (a project) stops the reading.</summary>
public partial class NeedsYouView : UserControl
{
    public static TimeSpan Every { get; set; } = TimeSpan.FromSeconds(5);

    readonly DispatcherTimer _timer;

    public NeedsYouView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Every };
        _timer.Tick += (_, _) => (DataContext as NeedsYouViewModel)?.Poll();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Interval = Every;
        _timer.Start();
        (DataContext as NeedsYouViewModel)?.Poll();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
