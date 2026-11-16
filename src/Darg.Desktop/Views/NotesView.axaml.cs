using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

/// <summary>Post-its are picked up and carried to another column (DragLift); a click opens one to edit,
/// Ctrl- or Shift-click picks it; dragging a picked note carries every picked note with it. Ctrl+A picks every note showing, Escape unpicks them.</summary>
public partial class NotesView : UserControl
{
    static NotesView() => RenameBox.Hook();

    public NotesView()
    {
        InitializeComponent();
        AddPicture.Click += async (_, _) =>
        {
            if (DataContext is NotesViewModel { Editor: { } editor } && await PicturePicker.Pick(this) is { } path) editor.SetPicture(path);
        };
        _ = new DragLift(this, Overlay, "postit", "wallcol",
            itemsOf: col => (col as NoteColumn)?.Notes.Cast<object>().ToList() ?? [],
            drop: (items, column, _) => { if (DataContext is NotesViewModel wall) wall.Move(items.OfType<NoteViewModel>().Select(n => n.Id), column); },
            click: (item, keys) => { if (DataContext is NotesViewModel wall && item is NoteViewModel n) wall.Click(n, keys); },
            ordered: false, // notes sit newest first in a column; only the column matters
            carried: item => DataContext is NotesViewModel wall && item is NoteViewModel n ? wall.Carried(n) : [item]);
        BoardView.ChipDoubleClicks(this);
        // a column's name: double-click to rename; in the box, Enter saves and Esc leaves it as it was
        AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if (e.Source is TextBlock { Tag: NoteColumn { IsEditable: true } col } t && t.Classes.Contains("wallname")) { col.StartRenameCommand.Execute(null); e.Handled = true; }
        });
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Source is not TextBox { Tag: NoteColumn col } box || !box.Classes.Contains("rename")) return;
            if (e.Key == Key.Enter) { col.CommitRenameCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape) { col.CancelRenameCommand.Execute(null); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
        DataContextChanged += (_, _) =>
        {
            if (_wall is not null) _wall.PropertyChanged -= WallChanged;
            _wall = DataContext as NotesViewModel;
            if (_wall is not null) _wall.PropertyChanged += WallChanged;
        };
    }

    NotesViewModel? _wall;

    /// <summary>+ Post-it puts the caret in the new note's text, so typing starts at once. Posted at
    /// Loaded priority: the editor panel only becomes visible on this change, and an invisible box takes no focus.</summary>
    void WallChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotesViewModel.Editor) || _wall?.Editor is not { IsNew: true }) return;
        Dispatcher.UIThread.Post(() => PaperBox.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Puts a line break at the caret (over any selection); with <paramref name="backslash"/>, the
    /// backslash just before the caret becomes the line break instead of staying in the note.</summary>
    static void NewLine(TextBox box, bool backslash)
    {
        if (backslash) { var caret = box.CaretIndex; box.SelectionStart = caret - 1; box.SelectionEnd = caret; }
        box.SelectedText = box.NewLine;
    }
}
