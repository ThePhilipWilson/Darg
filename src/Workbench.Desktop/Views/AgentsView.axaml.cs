using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

public partial class AgentsView : UserControl
{
    AgentViewModel? _shown;

    public AgentsView()
    {
        InitializeComponent();
        // Enter sends; Shift+Enter is a new line. Tunnel, so it's seen before the TextBox takes the Enter.
        ComposeBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _shown is not null)
            {
                e.Handled = true;
                _shown.SendCommand.Execute(null);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PropertyChanged += (_, e) => { if (e.Property == DataContextProperty) Hook(); };
    }

    void Hook()
    {
        if (DataContext is not AgentsViewModel vm) return;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AgentsViewModel.Selected)) return;
            if (_shown is not null) _shown.Items.CollectionChanged -= Grew;
            _shown = vm.Selected;
            if (_shown is not null) _shown.Items.CollectionChanged += Grew;
            ScrollSoon();
        };
    }

    /// <summary>Follow the transcript as it grows, unless the person has scrolled up to read.</summary>
    void Grew(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var nearBottom = Scroll.Offset.Y >= Scroll.Extent.Height - Scroll.Viewport.Height - 120;
        if (nearBottom) ScrollSoon();
    }

    void ScrollSoon() => Dispatcher.UIThread.Post(() => Scroll.ScrollToEnd(), DispatcherPriority.Background);
}
