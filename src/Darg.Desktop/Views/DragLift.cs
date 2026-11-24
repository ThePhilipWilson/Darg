using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Darg.Desktop.Views;

/// <summary>Drag and drop that looks like picking something up: the item lifts (a picture of it follows
/// the pointer, tilted, with a shadow), its place dims, the list under the pointer lights up, and a line
/// shows where it will land. Done inside the app with pointer capture, not the OS's drag.
///
/// Items are Borders with <paramref name="itemClass"/> whose Tag is the item; lists are Borders with
/// <paramref name="listClass"/> whose Tag is the list's id and whose DataContext gives its items in
/// order (<paramref name="itemsOf"/>). A press and release with no drag is a click, passed with the keys
/// held at the press (Ctrl or Shift for picking more than one).
///
/// An item can carry others with it (<paramref name="carried"/>: the picked items, when the one pressed
/// is picked): they all dim, the picture says how many, and the drop is passed the lot.</summary>
public sealed class DragLift
{
    readonly Control _host;
    readonly Canvas _overlay;
    readonly string _itemClass, _listClass;
    readonly Func<object?, IReadOnlyList<object>> _itemsOf;
    readonly Action<IReadOnlyList<object>, string, object?> _drop;
    readonly Func<object, IReadOnlyList<object>>? _carried;
    readonly Action<object, KeyModifiers> _click;
    readonly bool _ordered, _horizontal;

    Border? _pressed;
    Point _pressAt, _grab;
    KeyModifiers _pressKeys;
    bool _dragging;
    Control? _ghost;
    Rectangle? _line;
    Border? _target;
    string? _targetList;
    object? _before;
    /// <summary>What is being carried: the item pressed and any others going with it, in their order.</summary>
    List<object> _carry = [];
    readonly List<Border> _dimmed = [];

    /// <param name="ordered">Whether the drop place within a list matters (a line is shown and "before" passed).</param>
    public DragLift(Control host, Canvas overlay, string itemClass, string listClass, Func<object?, IReadOnlyList<object>> itemsOf,
        Action<IReadOnlyList<object>, string, object?> drop, Action<object, KeyModifiers> click, bool ordered = true,
        Func<object, IReadOnlyList<object>>? carried = null, bool horizontal = false)
    {
        _horizontal = horizontal;
        _host = host; _overlay = overlay; _itemClass = itemClass; _listClass = listClass;
        _itemsOf = itemsOf; _drop = drop; _click = click; _ordered = ordered; _carried = carried;
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
        // the carried items in their own order (so they land in it), the pressed one always among them
        _carry = [.. _carried?.Invoke(card.Tag!) ?? []];
        if (!_carry.Contains(card.Tag!)) _carry.Insert(0, card.Tag!);
        var others = _carry.Count - 1;
        _grab = _pressAt - (card.TranslatePoint(new Point(0, 0), _host) ?? default);
        // a picture of the item, drawn at the screen's scale so it stays sharp
        var scale = TopLevel.GetTopLevel(_host)?.RenderScaling ?? 1;
        var size = card.Bounds.Size;
        var bmp = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale))), new Vector(96 * scale, 96 * scale));
        bmp.Render(card);
        var picture = new Border
        {
            Width = size.Width, Height = size.Height, CornerRadius = new CornerRadius(6), Opacity = 0.95,
            BoxShadow = BoxShadows.Parse("0 12 28 0 #A0000000"),
            Child = new Image { Source = bmp, Width = size.Width, Height = size.Height },
        };
        if (others == 0) { picture.RenderTransform = new RotateTransform(2.5); picture.IsHitTestVisible = false; _ghost = picture; }
        else
        {
            // a stack: a plain sheet behind the picture for the others, and a badge with how many
            var grid = new Panel { Width = size.Width + 8, Height = size.Height + 8, IsHitTestVisible = false, RenderTransform = new RotateTransform(2.5) };
            grid.Children.Add(new Border
            {
                Width = size.Width, Height = size.Height, Margin = new Thickness(8, 8, 0, 0), CornerRadius = new CornerRadius(6),
                Background = ThemeManager.Brush("Lift"), BorderBrush = ThemeManager.Brush("LiftLine"), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                BoxShadow = BoxShadows.Parse("0 12 28 0 #A0000000"),
            });
            picture.HorizontalAlignment = HorizontalAlignment.Left; picture.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(picture);
            grid.Children.Add(new Border
            {
                Background = ThemeManager.Brush("Accent"), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -8, -8, 0),
                Child = new TextBlock { Text = _carry.Count.ToString(), Foreground = ThemeManager.Brush("OnAccent"), FontWeight = FontWeight.Bold, FontSize = 12 },
            });
            _ghost = grid;
        }
        _overlay.Children.Add(_ghost);
        _line = new Rectangle { Height = 3, Fill = ThemeManager.Brush("Accent"), RadiusX = 2, RadiusY = 2, IsVisible = false, IsHitTestVisible = false };
        _overlay.Children.Add(_line);
        _dimmed.Clear();
        _dimmed.AddRange(_host.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains(_itemClass) && b.Tag is not null && _carry.Contains(b.Tag)));
        if (!_dimmed.Contains(card)) _dimmed.Add(card);
        foreach (var b in _dimmed) b.Opacity = 0.3;
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
        // (never next to something being carried: it is leaving its place too)
        var borders = list.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains(_itemClass) && b.Tag is not null && !_carry.Contains(b.Tag)).ToList();
        Border? lineAt = null; var above = true;
        foreach (var b in borders)
        {
            var top = b.TranslatePoint(new Point(0, 0), _host) ?? default;
            if (_horizontal ? at.X < top.X + b.Bounds.Width / 2 : at.Y < top.Y + b.Bounds.Height / 2) { _before = b.Tag; lineAt = b; break; }
            lineAt = b; above = false;
        }
        if (_before is null && lineAt is not null)
        {
            // past the last card drawn: a virtualized list draws only what's in view, so the next card
            // down has no control yet; "before" is the next carried-free item after it in list order
            var items = _itemsOf(list.DataContext);
            var at0 = -1;
            for (var i = 0; i < items.Count; i++) if (Equals(items[i], lineAt.Tag)) { at0 = i; break; }
            if (at0 >= 0)
                for (var i = at0 + 1; i < items.Count; i++)
                    if (!_carry.Contains(items[i])) { _before = items[i]; break; }
        }
        if (lineAt is null)
        {
            _line!.IsVisible = false; // an empty list: it lights up, that's enough
            return;
        }
        if (_horizontal)
        {
            var q = lineAt.TranslatePoint(new Point(above ? -10 : lineAt.Bounds.Width + 7, 0), _host) ?? default;
            Canvas.SetLeft(_line!, q.X);
            Canvas.SetTop(_line!, q.Y);
            _line!.Width = 3;
            _line.Height = Math.Max(lineAt.Bounds.Height, list.Bounds.Height);
            _line.IsVisible = true;
            return;
        }
        var p = lineAt.TranslatePoint(new Point(0, above ? -5 : lineAt.Bounds.Height + 3), _host) ?? default;
        Canvas.SetLeft(_line!, p.X);
        Canvas.SetTop(_line!, p.Y);
        _line!.Height = 3;
        _line!.Width = lineAt.Bounds.Width;
        _line.IsVisible = true;
    }

    void Finish(bool drop)
    {
        var carry = _carry;
        var list = _targetList;
        var before = _before;
        foreach (var b in _dimmed) b.Opacity = 1;
        _dimmed.Clear();
        _carry = [];
        if (_ghost is not null) _overlay.Children.Remove(_ghost);
        if (_line is not null) _overlay.Children.Remove(_line);
        _target?.Classes.Remove("droptarget");
        _ghost = null; _line = null; _target = null; _targetList = null; _before = null;
        _dragging = false;
        _pressed = null;
        if (drop && carry.Count > 0 && list is not null) _drop(carry, list, before);
    }
}
