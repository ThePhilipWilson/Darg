using Avalonia.Controls;
using Avalonia.Input;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

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
