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
        // the post-it's text: Enter posts it; Shift+Enter, or a backslash typed just before Enter, is a new line
        PaperBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not NotesViewModel { Editor: { } editor }) return;
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { NewLine(PaperBox, backslash: false); return; }
            var caret = PaperBox.CaretIndex;
            var text = PaperBox.Text ?? "";
            if (caret > 0 && caret <= text.Length && text[caret - 1] == '\\' && PaperBox.SelectionStart == PaperBox.SelectionEnd)
                NewLine(PaperBox, backslash: true);
            else editor.SaveCommand.Execute(null);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            if (DataContext is not NotesViewModel wall || e.Source is TextBox) return;
            if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { wall.PickAllCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape && wall.HasPicked) { wall.ClearPicked(); e.Handled = true; }
        };
    }

    /// <summary>Puts a line break at the caret (over any selection); with <paramref name="backslash"/>, the
    /// backslash just before the caret becomes the line break instead of staying in the note.</summary>
    static void NewLine(TextBox box, bool backslash)
    {
        if (backslash) { var caret = box.CaretIndex; box.SelectionStart = caret - 1; box.SelectionEnd = caret; }
        box.SelectedText = box.NewLine;
    }
}
