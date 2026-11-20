using Avalonia.Controls;
using Avalonia.Interactivity;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

public partial class GitHubView : UserControl
{
    public GitHubView() => InitializeComponent();

    /// <summary>A link in a description or comment opens in the browser.</summary>
    void OnMarkdownLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not MarkdownView md) return;
        md.LinkClicked -= Browse;
        md.LinkClicked += Browse;
    }

    static void Browse(string link) => GitHubViewModel.Browse(link);
}
