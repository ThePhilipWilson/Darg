using Avalonia.Controls;
using Avalonia.Input;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

public partial class DocsView : UserControl
{
    public DocsView()
    {
        InitializeComponent();
        Rendered.LinkClicked += link => (DataContext as DocsViewModel)?.OpenLink(link);
        // Ctrl+S saves while editing
        Editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { (DataContext as DocsViewModel)?.SaveCommand.Execute(null); e.Handled = true; }
        };
    }
}
