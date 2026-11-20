using Avalonia;
using Avalonia.Controls;
using Darg.Mobile.ViewModels;

namespace Darg.Mobile.Views;

public partial class AgentsListView : UserControl
{
    public AgentsListView() => InitializeComponent();

    // poll only while on screen
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as IPolled)?.StartPolling();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as IPolled)?.StopPolling();
        base.OnDetachedFromVisualTree(e);
    }
}
