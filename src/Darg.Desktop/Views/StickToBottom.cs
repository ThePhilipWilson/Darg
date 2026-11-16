using Avalonia.Controls;

namespace Darg.Desktop.Views;

/// <summary>Keeps a chat scrolled to its newest line: whenever its content grows (a new message, a long
/// reply laid out, an image loaded) it follows, unless the person has scrolled up to read, and it starts
/// following again once they scroll back to the bottom. Works from the scroll viewer's own size changes,
/// so it doesn't matter where the growth came from or when the layout settled.</summary>
public sealed class StickToBottom
{
    readonly ScrollViewer _sv;
    bool _stick = true;

    public StickToBottom(ScrollViewer sv)
    {
        _sv = sv;
        sv.ScrollChanged += (_, e) =>
        {
            if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
            {
                if (_stick) sv.ScrollToEnd();
            }
            else if (e.OffsetDelta.Y != 0)
                _stick = sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 40;
        };
    }

    /// <summary>A different chat is shown: start at its newest line, following.</summary>
    public void Reset()
    {
        _stick = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _sv.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
