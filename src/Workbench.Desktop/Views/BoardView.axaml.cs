using Avalonia.Controls;
using Avalonia.VisualTree;
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

    /// <summary>A double-click on a filter chip makes it "must have" (AND). Seen on the second press (its
    /// click count is 2), before the button turns the release into a click; the chip then ignores that click.
    /// (The DoubleTapped gesture never reaches here: the button swallows it.)</summary>
    internal static void ChipDoubleClicks(Control host) =>
        host.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            // the press lands on the chip's text, whose data is the label's string: find the chip's button
            if (e.ClickCount != 2) return;
            var chip = (e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Button>()
                .Select(b => b.DataContext).OfType<LabelChip>().FirstOrDefault();
            chip?.DoubleClick();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
}
