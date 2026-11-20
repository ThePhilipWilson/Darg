using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

public partial class AgentsView : UserControl
{
    readonly StickToBottom _follow;
    AgentsViewModel? _vm;

    public AgentsView()
    {
        InitializeComponent();
        _follow = new StickToBottom(Scroll);
        // Enter sends; Shift+Enter is a new line. Tunnel, so it's seen before the TextBox takes the Enter.
        // (Asks the view model each time which agent is shown, so the one open at start works too.)
        ComposeBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _vm?.Shown is { } shown)
            {
                e.Handled = true;
                shown.SendCommand.Execute(null);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // an agents tab pressed with Ctrl adds or removes its board (WOR-0150); the button's click reads the flag
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_vm is not null && (e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Button>().Any(x => x.DataContext is AgentBoardTab) == true)
                _vm.TabCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= Changed;
        _vm = DataContext as AgentsViewModel;
        if (_vm is not null) _vm.PropertyChanged += Changed;
        _follow.Reset();
    }

    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentsViewModel.Shown)) _follow.Reset(); // another agent: start at its newest line
    }
}
