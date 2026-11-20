using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Darg.Mobile.ViewModels;

namespace Darg.Mobile.Views;

public partial class AgentPageView : UserControl
{
    AgentPageViewModel? _vm;

    public AgentPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as AgentPageViewModel);
    }

    void Watch(AgentPageViewModel? vm)
    {
        if (_vm is not null) _vm.Entries.CollectionChanged -= OnEntries;
        _vm = vm;
        if (vm is not null) vm.Entries.CollectionChanged += OnEntries;
    }

    // follow the newest entry while reading at the bottom; leave the place alone while scrolled up reading
    void OnEntries(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var atEnd = Scroll.Offset.Y >= Scroll.Extent.Height - Scroll.Viewport.Height - 80;
        if (atEnd || e.Action == NotifyCollectionChangedAction.Reset) Dispatcher.UIThread.Post(Scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm?.StartPolling();
        Dispatcher.UIThread.Post(Scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _vm?.StopPolling();
        base.OnDetachedFromVisualTree(e);
    }
}
