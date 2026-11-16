using Avalonia.Controls;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        Readme.LinkClicked += link => (DataContext as HomeViewModel)?.OpenLink(link);
    }
}
