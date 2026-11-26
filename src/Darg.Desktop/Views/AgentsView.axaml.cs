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
        if (_vm is not null) { _vm.PropertyChanged -= Changed; _vm.NewAgentStarted -= CloseNewAgent; }
        _vm = DataContext as AgentsViewModel;
        if (_vm is not null) { _vm.PropertyChanged += Changed; _vm.NewAgentStarted += CloseNewAgent; }
        _follow.Reset();
    }

    void CloseNewAgent() => NewAgentButton.Flyout?.Hide();

    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentsViewModel.Shown)) _follow.Reset(); // another agent: start at its newest line
    }
}
