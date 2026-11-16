using Avalonia.Controls;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

/// <summary>Cards are picked up and carried (DragLift): drop on a list to move there, the line shows
/// where; a click opens the card.</summary>
public partial class BoardView : UserControl
{
    public BoardView()
    {
        InitializeComponent();
        _ = new DragLift(this, Overlay, "card", "list",
            itemsOf: list => (list as ListViewModel)?.Cards.Cast<object>().ToList() ?? [],
            drop: (item, list, before) =>
            {
                if (DataContext is BoardViewModel board && item is CardViewModel card)
                    board.Move(card, list, (before as CardViewModel)?.Id);
            },
            click: item => { if (DataContext is BoardViewModel board && item is CardViewModel card) board.Open(card); });
        ChipDoubleClicks(this);
    }

    /// <summary>A double-click on a filter chip makes it "must have" (AND). The button swallows the
    /// gesture, so it's caught here, handled or not.</summary>
    internal static void ChipDoubleClicks(Control host) =>
        host.AddHandler(Avalonia.Input.InputElement.DoubleTappedEvent, (_, e) =>
        {
            if ((e.Source as Avalonia.StyledElement)?.DataContext is LabelChip chip) { chip.DoubleClick(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
}
