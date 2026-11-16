using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

public partial class PreviewView : UserControl
{
    PreviewViewModel? _vm;

    public PreviewView()
    {
        InitializeComponent();
        Web.NavigationCompleted += (_, _) => _vm?.Navigated(Web.Source);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= OnVm;
        _vm = DataContext as PreviewViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVm;
        if (_vm.Page is { } p) Web.Navigate(p);
    }

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewViewModel.Page) && _vm?.Page is { } p) Web.Navigate(p);
        if (e.PropertyName == nameof(PreviewViewModel.Log)) LogScroll.ScrollToEnd();
    }

    void AddressKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm?.GoToAddressCommand.Execute(null);
        e.Handled = true;
    }

    void Back(object? sender, RoutedEventArgs e) { if (Web.CanGoBack) Web.GoBack(); }
    void Forward(object? sender, RoutedEventArgs e) { if (Web.CanGoForward) Web.GoForward(); }
    void Reload(object? sender, RoutedEventArgs e) => Web.Refresh();
}
