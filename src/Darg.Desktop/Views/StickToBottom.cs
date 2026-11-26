using Avalonia.Controls;

namespace Darg.Desktop.Views;

/// <summary>Keeps a chat scrolled to its newest line: whenever its content grows (a new message, a long
/// reply laid out, an image loaded) it follows, unless the person has scrolled up to read, and it starts
/// following again once they scroll back to the bottom. Works from the scroll viewer's own size changes,
/// so it doesn't matter where the growth came from or when the layout settled.
/// The transcripts are virtualised (WOR-0230): the extent is an estimate that changes as entries are built
/// and measured, also while the person scrolls up through the history, so any move of the offset decides
/// whether it still follows before a change of extent can pull the view back down.</summary>
public sealed class StickToBottom
{
    const double Near = 40; // this close to the bottom counts as at it
    readonly ScrollViewer _sv;
    bool _stick = true;

    public StickToBottom(ScrollViewer sv)
    {
        _sv = sv;
        sv.ScrollChanged += (_, e) =>
        {
            // reaching the bottom follows again; only a move up away from it stops following. A move down that
            // isn't to the bottom is the panel keeping entries in place as it measures them (or a clamp), not the person
            if (e.OffsetDelta.Y != 0)
            {
                if (AtBottom) _stick = true;
                else if (e.OffsetDelta.Y < 0) _stick = false;
            }
            if ((e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0) && _stick) sv.ScrollToEnd();
        };
    }

    bool AtBottom => _sv.Offset.Y >= _sv.Extent.Height - _sv.Viewport.Height - Near;

    /// <summary>A different chat is shown: start at its newest line, following.</summary>
    public void Reset()
    {
        _stick = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _sv.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
