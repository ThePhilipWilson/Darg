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
    }
}
