using Avalonia.Controls;
using Avalonia.Input;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

/// <summary>Post-its are picked up and carried to another column (DragLift); a click opens one to edit,
/// Ctrl- or Shift-click picks it. Ctrl+A picks every note showing, Escape unpicks them.</summary>
public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();
        _ = new DragLift(this, Overlay, "postit", "wallcol",
            itemsOf: col => (col as NoteColumn)?.Notes.Cast<object>().ToList() ?? [],
            drop: (item, column, _) => { if (DataContext is NotesViewModel wall && item is NoteViewModel n) wall.Move(n.Id, column); },
            click: (item, keys) => { if (DataContext is NotesViewModel wall && item is NoteViewModel n) wall.Click(n, keys); },
            ordered: false); // notes sit newest first in a column; only the column matters
        BoardView.ChipDoubleClicks(this);
        KeyDown += (_, e) =>
        {
            if (DataContext is not NotesViewModel wall || e.Source is TextBox) return;
            if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { wall.PickAllCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape && wall.HasPicked) { wall.ClearPicked(); e.Handled = true; }
        };
    }
}
