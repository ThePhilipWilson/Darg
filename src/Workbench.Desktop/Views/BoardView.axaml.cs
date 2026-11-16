using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

/// <summary>Cards drag between lists: drop on a card to go before it, on a list's empty space to go
/// to its end. The move is written to the card's file and board.json, then the board reloads.</summary>
public partial class BoardView : UserControl
{
    PointerPressedEventArgs? _press;
    CardViewModel? _pressed;
    Point _at;
    bool _dragging;

    public BoardView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => { _press = null; _pressed = null; }, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, Over);
        AddHandler(DragDrop.DropEvent, Dropped);
    }

    static T? Up<T>(object? source, Func<T, bool>? test = null) where T : Visual =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<T>().FirstOrDefault(v => test?.Invoke(v) ?? true);

    static Border? CardAt(object? source) => Up<Border>(source, b => b.Classes.Contains("card"));
    static Border? ListAt(object? source) => Up<Border>(source, b => b.Classes.Contains("list"));

    void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = CardAt(e.Source)?.Tag as CardViewModel;
        _press = _pressed is null ? null : e;
        _at = e.GetPosition(this);
    }

    async void Moved(object? sender, PointerEventArgs e)
    {
        if (_press is null || _pressed is null || _dragging) return;
        var d = e.GetPosition(this) - _at;
        if (Math.Abs(d.X) + Math.Abs(d.Y) < 6) return;
        _dragging = true;
        var card = _pressed;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(card.Id));
        try { await DragDrop.DoDragDropAsync(_press, data, DragDropEffects.Move); }
        finally { _dragging = false; _press = null; _pressed = null; }
    }

    void Over(object? sender, DragEventArgs e)
    {
        e.DragEffects = ListAt(e.Source) is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    void Dropped(object? sender, DragEventArgs e)
    {
        var id = e.DataTransfer.TryGetText();
        if (ListAt(e.Source)?.Tag is not string list || DataContext is not BoardViewModel board || id is null) return;
        var card = board.Lists.SelectMany(l => l.Cards).FirstOrDefault(c => c.Id == id);
        if (card is null) return;
        var before = (CardAt(e.Source)?.Tag as CardViewModel)?.Id;
        if (before == id) return;
        board.Move(card, list, before);
        e.Handled = true;
    }
}
