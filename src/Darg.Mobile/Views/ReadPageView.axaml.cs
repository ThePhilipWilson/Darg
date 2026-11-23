using Avalonia.Controls;
using Darg.Mobile.ViewModels;

namespace Darg.Mobile.Views;

/// <summary>A doc or report: a tapped link goes to the page, which opens a doc itself or hands back a web address
/// for the phone's browser.</summary>
public partial class ReadPageView : UserControl
{
    public ReadPageView()
    {
        InitializeComponent();
        Body.LinkTapped += async target =>
        {
            if (DataContext is ReadPageViewModel page && page.Follow(target) is { } uri && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(uri);
        };
    }
}
