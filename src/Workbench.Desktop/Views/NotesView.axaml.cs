using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

/// <summary>Click a post-it to edit it; drag it to another column to move it.</summary>
public partial class NotesView : UserControl
{
    PointerPressedEventArgs? _press;
    NoteViewModel? _pressed;
    Point _at;
    bool _dragging;

    public NotesView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = Up(e.Source, "wallcol") is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (Up(e.Source, "wallcol")?.Tag is string column && e.DataTransfer.TryGetText() is { } id && DataContext is NotesViewModel wall)
            {
                wall.Move(id, column);
                e.Handled = true;
            }
        });
    }

    static Border? Up(object? source, string cls) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains(cls));

    void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = Up(e.Source, "postit")?.Tag as NoteViewModel;
        _press = _pressed is null ? null : e;
        _at = e.GetPosition(this);
    }

    void Released(object? sender, PointerReleasedEventArgs e)
    {
        // a press and release without a drag is a click: edit the note
        if (_pressed is not null && !_dragging) _pressed.Edit();
        _press = null; _pressed = null;
    }

    async void Moved(object? sender, PointerEventArgs e)
    {
        if (_press is null || _pressed is null || _dragging) return;
        var d = e.GetPosition(this) - _at;
        if (Math.Abs(d.X) + Math.Abs(d.Y) < 6) return;
        _dragging = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(_pressed.Id));
        try { await DragDrop.DoDragDropAsync(_press, data, DragDropEffects.Move); }
        finally { _dragging = false; _press = null; _pressed = null; }
    }
}
