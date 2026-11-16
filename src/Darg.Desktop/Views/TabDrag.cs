using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Darg.Desktop.Views;

/// <summary>Tabs in a row (a ListBox laid out horizontally) that move by dragging, as in a browser:
/// the tab under the pointer follows it along the row, the others slide aside to show where it will
/// land, and letting go puts it there (<c>move(from, to)</c>, which moves the item in the list's
/// collection). A press and release without dragging is still a plain click; presses on a button inside
/// a tab (its ×) belong to the button.</summary>
public sealed class TabDrag
{
    readonly ListBox _list;
    readonly Action<int, int> _move;

    ListBoxItem? _pressed;
    Point _pressAt;
    bool _dragging;
    int _from, _to;
    List<ListBoxItem> _tabs = [];
    double[] _centres = [];
    double _slot, _minDx, _maxDx;

    public static TabDrag Attach(ListBox list, Action<int, int> move) => new(list, move);

    TabDrag(ListBox list, Action<int, int> move)
    {
        _list = list; _move = move;
        list.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => { if (_dragging) Finish(drop: false); }, RoutingStrategies.Bubble);
    }

    void Pressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
        var source = e.Source as Visual;
        if (source?.GetSelfAndVisualAncestors().OfType<Button>().Any() == true) return;
        var item = source?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (item is null || _list.IndexFromContainer(item) < 0) return;
        _pressed = item;
        _pressAt = e.GetPosition(_list);
        // the press goes on to the list, so the tab is picked as usual
    }

    void Moved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null) return;
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) { if (_dragging) Finish(drop: false); _pressed = null; return; }
        var dx = e.GetPosition(_list).X - _pressAt.X;
        if (!_dragging)
        {
            if (Math.Abs(dx) < 6) return;
            if (!Lift(e)) { _pressed = null; return; }
        }
        Follow(Math.Clamp(dx, _minDx, _maxDx));
        e.Handled = true;
    }

    void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressed is null) return;
        if (_dragging) { Finish(drop: true); e.Handled = true; }
        _pressed = null;
    }

    bool Lift(PointerEventArgs e)
    {
        _from = _list.IndexFromContainer(_pressed!);
        _tabs = Enumerable.Range(0, _list.ItemCount).Select(i => _list.ContainerFromIndex(i) as ListBoxItem).OfType<ListBoxItem>().ToList();
        if (_from < 0 || _tabs.Count != _list.ItemCount || _tabs.Count < 2) return false;
        _dragging = true;
        _to = _from;
        _centres = _tabs.Select(t => t.Bounds.X + t.Bounds.Width / 2).ToArray();
        var me = _tabs[_from];
        _slot = me.Bounds.Width + me.Margin.Left + me.Margin.Right;
        // the tab can go as far as either end of the row, no further
        _minDx = _tabs[0].Bounds.X - me.Bounds.X;
        _maxDx = _tabs[^1].Bounds.Right - me.Bounds.Right;
        var slide = new Transitions { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(120) } };
        foreach (var t in _tabs) if (t != me) t.Transitions = slide;
        me.ZIndex = 1;
        me.Opacity = 0.9;
        e.Pointer.Capture(_list);
        return true;
    }

    void Follow(double dx)
    {
        var centre = _centres[_from] + dx;
        // where it lands: past the middle of a neighbour is past that neighbour
        _to = _from;
        for (var j = _from + 1; j < _tabs.Count && centre > _centres[j]; j++) _to = j;
        for (var j = _from - 1; j >= 0 && centre < _centres[j]; j--) _to = j;

        _tabs[_from].RenderTransform = Shift(dx);
        for (var j = 0; j < _tabs.Count; j++)
        {
            if (j == _from) continue;
            var by = _from < _to && j > _from && j <= _to ? -_slot
                   : _to < _from && j >= _to && j < _from ? _slot
                   : 0;
            _tabs[j].RenderTransform = Shift(by);
        }
    }

    static ITransform Shift(double x) => TransformOperations.Parse($"translateX({x.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)");

    void Finish(bool drop)
    {
        var (from, to) = (_from, _to);
        _dragging = false;
        foreach (var t in _tabs)
        {
            t.Transitions = null;
            t.RenderTransform = null;
            t.ZIndex = 0;
            t.Opacity = 1;
        }
        _tabs = [];
        _pressed = null;
        if (!drop || from == to) return;
        var selected = _list.SelectedItem;
        _move(from, to);
        // a moved item can come back unpicked; keep whatever was picked before
        if (selected is not null && _list.SelectedItem != selected) _list.SelectedItem = selected;
    }
}
