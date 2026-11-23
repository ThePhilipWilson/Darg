using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Darg.Mobile.ViewModels;

namespace Darg.Mobile.Views;

public partial class FettleChatView : UserControl
{
    FettleChatViewModel? _vm;

    public FettleChatView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.Rows.CollectionChanged -= ToEnd;
        _vm = DataContext as FettleChatViewModel;
        if (_vm is not null) { _vm.Rows.CollectionChanged += ToEnd; _vm.Shown(); }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm?.Shown();
        ToEnd(null, null);
    }

    /// <summary>The newest entry in view, as a chat reads.</summary>
    void ToEnd(object? sender, NotifyCollectionChangedEventArgs? e) =>
        Dispatcher.UIThread.Post(() => this.FindControl<ScrollViewer>("Scroll")?.ScrollToEnd(), DispatcherPriority.Background);
}
