using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Workbench.Desktop.Views;

/// <summary>Drag and drop that looks like picking something up: the item lifts (a picture of it follows
/// the pointer, tilted, with a shadow), its place dims, the list under the pointer lights up, and a line
/// shows where it will land. Done inside the app with pointer capture, not the OS's drag.
///
/// Items are Borders with <paramref name="itemClass"/> whose Tag is the item; lists are Borders with
/// <paramref name="listClass"/> whose Tag is the list's id and whose DataContext gives its items in
/// order (<paramref name="itemsOf"/>). A press and release with no drag is a click, passed with the keys
/// held at the press (Ctrl or Shift for picking more than one).</summary>
public sealed class DragLift
{
    readonly Control _host;
    readonly Canvas _overlay;
    readonly string _itemClass, _listClass;
    readonly Func<object?, IReadOnlyList<object>> _itemsOf;
    readonly Action<object, string, object?> _drop;
    readonly Action<object, KeyModifiers> _click;
    readonly bool _ordered;

    Border? _pressed;
    Point _pressAt, _grab;
    KeyModifiers _pressKeys;
    bool _dragging;
    Control? _ghost;
    Rectangle? _line;
    Border? _target;
    string? _targetList;
    object? _before;

    /// <param name="ordered">Whether the drop place within a list matters (a line is shown and "before" passed).</param>
    public DragLift(Control host, Canvas overlay, string itemClass, string listClass, Func<object?, IReadOnlyList<object>> itemsOf,
        Action<object, string, object?> drop, Action<object, KeyModifiers> click, bool ordered = true)
    {
        _host = host; _overlay = overlay; _itemClass = itemClass; _listClass = listClass;
        _itemsOf = itemsOf; _drop = drop; _click = click; _ordered = ordered;
        host.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => { if (_dragging) Finish(drop: false); }, RoutingStrategies.Bubble);
    }

    static T? Up<T>(object? v, Func<T, bool> test) where T : Visual =>
        (v as Visual)?.GetSelfAndVisualAncestors().OfType<T>().FirstOrDefault(test);

    void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed) return;
        // presses on a button or a text box belong to them
        if (Up<Control>(e.Source, c => c is Button or TextBox) is not null) { _pressed = null; return; }
        _pressed = Up<Border>(e.Source, b => b.Classes.Contains(_itemClass));
        _pressAt = e.GetPosition(_host);
        _pressKeys = e.KeyModifiers;
    }

    void Moved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null) return;
        var at = e.GetPosition(_host);
        if (!_dragging)
        {
            if (Math.Abs(at.X - _pressAt.X) + Math.Abs(at.Y - _pressAt.Y) < 6) return;
            Lift(e);
        }
        Follow(at);
        e.Handled = true;
    }

    void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressed is null) return;
        if (_dragging) { Finish(drop: true); e.Handled = true; }
        else _click(_pressed.Tag!, _pressKeys);
        _pressed = null;
    }

    void Lift(PointerEventArgs e)
    {
        _dragging = true;
        var card = _pressed!;
        _grab = _pressAt - (card.TranslatePoint(new Point(0, 0), _host) ?? default);
        // a picture of the item, drawn at the screen's scale so it stays sharp
        var scale = TopLevel.GetTopLevel(_host)?.RenderScaling ?? 1;
        var size = card.Bounds.Size;
        var bmp = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale))), new Vector(96 * scale, 96 * scale));
        bmp.Render(card);
        _ghost = new Border
        {
            Width = size.Width, Height = size.Height, CornerRadius = new CornerRadius(6), Opacity = 0.95,
            BoxShadow = BoxShadows.Parse("0 12 28 0 #A0000000"),
            Child = new Image { Source = bmp, Width = size.Width, Height = size.Height },
            RenderTransform = new RotateTransform(2.5), IsHitTestVisible = false,
        };
        _overlay.Children.Add(_ghost);
        _line = new Rectangle { Height = 3, Fill = new SolidColorBrush(Color.Parse("#e0a84a")), RadiusX = 2, RadiusY = 2, IsVisible = false, IsHitTestVisible = false };
        _overlay.Children.Add(_line);
        card.Opacity = 0.3;
        e.Pointer.Capture(_host);
    }

    void Follow(Point at)
    {
        Canvas.SetLeft(_ghost!, at.X - _grab.X);
        Canvas.SetTop(_ghost!, at.Y - _grab.Y);

        // what's under the pointer (the ghost and line take no hits)
        var hit = _host.InputHitTest(at) as Visual;
        var list = Up<Border>(hit, b => b.Classes.Contains(_listClass));
        if (list != _target)
        {
            _target?.Classes.Remove("droptarget");
            _target = list;
            _target?.Classes.Add("droptarget");
        }
        _targetList = list?.Tag as string;
        _before = null;
        if (list is null || !_ordered) { _line!.IsVisible = false; return; }

        // where in the list: before the item under the pointer (upper half) or the one after it
        var items = _itemsOf(list.DataContext).Where(i => i != _pressed!.Tag).ToList();
        var borders = list.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains(_itemClass) && b.Tag is not null && b.Tag != _pressed!.Tag).ToList();
        Border? lineAt = null; var above = true;
        foreach (var b in borders)
        {
            var top = b.TranslatePoint(new Point(0, 0), _host) ?? default;
            if (at.Y < top.Y + b.Bounds.Height / 2) { _before = b.Tag; lineAt = b; break; }
            lineAt = b; above = false;
        }
        if (lineAt is null)
        {
            _line!.IsVisible = false; // an empty list: it lights up, that's enough
            return;
        }
        var p = lineAt.TranslatePoint(new Point(0, above ? -5 : lineAt.Bounds.Height + 3), _host) ?? default;
        Canvas.SetLeft(_line!, p.X);
        Canvas.SetTop(_line!, p.Y);
        _line!.Width = lineAt.Bounds.Width;
        _line.IsVisible = true;
    }

    void Finish(bool drop)
    {
        var item = _pressed?.Tag;
        var list = _targetList;
        var before = _before;
        if (_pressed is not null) _pressed.Opacity = 1;
        if (_ghost is not null) _overlay.Children.Remove(_ghost);
        if (_line is not null) _overlay.Children.Remove(_line);
        _target?.Classes.Remove("droptarget");
        _ghost = null; _line = null; _target = null; _targetList = null; _before = null;
        _dragging = false;
        _pressed = null;
        if (drop && item is not null && list is not null) _drop(item, list, before);
    }
}
