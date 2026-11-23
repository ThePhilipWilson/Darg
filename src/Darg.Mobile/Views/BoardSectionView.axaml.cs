using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Darg.Mobile.ViewModels;

namespace Darg.Mobile.Views;

public partial class BoardSectionView : UserControl
{
    public BoardSectionView()
    {
        AvaloniaXamlLoader.Load(this);
        // a long press on a card's tile puts up its move sheet; the tap that ends it then finds the sheet up and opens nothing
        AddHandler(HoldingEvent, OnHolding, RoutingStrategies.Bubble, handledEventsToo: true);
        var quick = this.FindControl<TextBox>("QuickBox")!;
        quick.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not BoardSectionViewModel vm) return;
            e.Handled = true;
            if (vm.QuickAddCommand.CanExecute(null)) vm.QuickAddCommand.Execute(null);
        };
    }

    BoardSectionViewModel? _vm;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as BoardSectionViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
    }

    /// <summary>A swipe to a list off the strip's edge scrolls the strip so its name shows.</summary>
    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BoardSectionViewModel.ListIndex) || _vm is not { ListIndex: >= 0 } vm) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<ItemsControl>("StripItems")?.ContainerFromIndex(vm.ListIndex)?.BringIntoView());
    }

    void OnHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || DataContext is not BoardSectionViewModel vm) return;
        if ((e.Source as StyledElement)?.DataContext is PhoneCard card && vm.Open is null)
        {
            vm.StartMove(card);
            e.Handled = true;
        }
    }

    void Scrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BoardSectionViewModel vm) vm.CancelMoveCommand.Execute(null);
    }
}
