using Avalonia.Controls;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

/// <summary>Post-its are picked up and carried to another column (DragLift); a click opens one to edit.</summary>
public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();
        _ = new DragLift(this, Overlay, "postit", "wallcol",
            itemsOf: col => (col as NoteColumn)?.Notes.Cast<object>().ToList() ?? [],
            drop: (item, column, _) => { if (DataContext is NotesViewModel wall && item is NoteViewModel n) wall.Move(n.Id, column); },
            click: item => (item as NoteViewModel)?.Edit(),
            ordered: false); // notes sit newest first in a column; only the column matters
        BoardView.ChipDoubleClicks(this);
    }
}
