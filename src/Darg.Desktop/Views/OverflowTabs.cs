using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

/// <summary>The board tabs' panel (WOR-0193): lays the tabs out in one row and shows those that fit, the lit ones
/// first, then in order; the rest are left off the row and listed in <see cref="Hidden"/>.</summary>
public sealed class OverflowTabsPanel : Panel
{
    const double Gap = 4;
    readonly HashSet<BoardTab> _hooked = [];
    HashSet<Control> _shown = [];

    /// <summary>The tabs left out of the row at the last measure, in board order.</summary>
    public List<BoardTab> Hidden { get; private set; } = [];

    static BoardTab? TabOf(Control c) => c.DataContext as BoardTab;

    void Hook()
    {
        var now = Children.OfType<Control>().Select(TabOf).OfType<BoardTab>().ToHashSet();
        foreach (var t in _hooked.Where(t => !now.Contains(t)).ToList()) { t.PropertyChanged -= OnTab; _hooked.Remove(t); }
        foreach (var t in now) if (_hooked.Add(t)) t.PropertyChanged += OnTab;
    }

    void OnTab(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoardTab.IsShown)) InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size available)
    {
        Hook();
        var kids = Children.OfType<Control>().ToList();
        foreach (var k in kids) k.Measure(new Size(double.PositiveInfinity, available.Height));
        var total = kids.Sum(k => k.DesiredSize.Width) + Gap * Math.Max(0, kids.Count - 1);
        _shown = [];
        if (double.IsInfinity(available.Width) || total <= available.Width)
        {
            _shown = kids.ToHashSet();
        }
        else
        {
            var used = 0.0;
            bool Take(Control k, bool force)
            {
                var need = k.DesiredSize.Width + (_shown.Count > 0 ? Gap : 0);
                if (!force && used + need > available.Width) return false;
                used += need; _shown.Add(k);
                return true;
            }
            // the lit tabs first (the first always, clipped if need be), then the rest in order
            var first = true;
            foreach (var k in kids.Where(k => TabOf(k)?.IsShown == true)) { if (!Take(k, first)) break; first = false; }
            foreach (var k in kids.Where(k => TabOf(k)?.IsShown != true)) if (!Take(k, false)) break;
        }
        Hidden = kids.Where(k => !_shown.Contains(k)).Select(TabOf).OfType<BoardTab>().ToList();
        var shown = kids.Where(_shown.Contains).ToList();
        return new Size(shown.Sum(k => k.DesiredSize.Width) + Gap * Math.Max(0, shown.Count - 1),
            kids.Count == 0 ? 0 : kids.Max(k => k.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var k in Children.OfType<Control>())
        {
            if (_shown.Contains(k))
            {
                var w = Math.Min(k.DesiredSize.Width, Math.Max(0, finalSize.Width - x));
                k.IsHitTestVisible = true;
                k.Arrange(new Rect(x, 0, w, finalSize.Height));
                x += w + Gap;
            }
            else { k.IsHitTestVisible = false; k.Arrange(new Rect(-100000, 0, k.DesiredSize.Width, finalSize.Height)); }
        }
        return finalSize;
    }
}

/// <summary>The board tabs and, when some don't fit, a ▾ n more button that lists them (WOR-0193). Picking one from
/// the list is a plain click on its tab, so it then shows (lit) in the row. Keeps <see cref="Reserve"/> free for
/// whatever sits after it. A button before the tabs (the All tab, WOR-0214) leads the row; once the row overflows it goes
/// into the menu with the hidden tabs, unless it is the one showing (its class "all").</summary>
public sealed class OverflowRow : Panel
{
    const double Gap = 6;
    public static readonly StyledProperty<double> ReserveProperty = AvaloniaProperty.Register<OverflowRow, double>(nameof(Reserve), 0);
    public double Reserve { get => GetValue(ReserveProperty); set => SetValue(ReserveProperty, value); }

    public ItemsControl? Items { get; set; }
    /// <summary>The button before the tabs, if any: the All tab.</summary>
    public Button? Lead { get; set; }
    /// <summary>The lead is in the row (else it is in the menu, or not there at all).</summary>
    bool _leadIn;
    bool LeadThere => Lead is { IsVisible: true };
    public OverflowTabsPanel? TabsPanel => Items?.ItemsPanelRoot as OverflowTabsPanel;
    public Button More { get; } = new() { Classes = { "chip" }, Margin = new Thickness(0), Padding = new Thickness(12, 4), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly MenuFlyout _menu = new();

    public OverflowRow()
    {
        More.Name = "MoreBoards";
        Avalonia.Automation.AutomationProperties.SetName(More, "More boards");
        More.Flyout = _menu;
        _menu.Opening += (_, _) => FillMenu();
        ToolTip.SetTip(More, "The boards that don't fit in the row. Pick one to show it.");
        Children.Add(More);
    }

    /// <summary>The menu's items are the hidden tabs. Filled as the row is measured as well as on opening: the
    /// flyout builds its list when it is shown, so items put in during Opening came too late and it opened empty.</summary>
    void FillMenu()
    {
        var hidden = TabsPanel?.Hidden ?? [];
        var lead = LeadThere && !_leadIn ? Lead : null;
        var names = (lead is null ? [] : new[] { lead.Content as string }).Concat(hidden.Select(t => t.Name));
        if (_menu.Items.OfType<MenuItem>().Select(m => m.Header as string).SequenceEqual(names)) return;
        _menu.Items.Clear();
        if (lead is not null) _menu.Items.Add(new MenuItem { Header = lead.Content as string, Command = lead.Command });
        foreach (var tab in hidden)
            _menu.Items.Add(new MenuItem { Header = tab.Name, Command = tab.PickCommand });
    }

    protected override Size MeasureOverride(Size available)
    {
        Items ??= Children.OfType<ItemsControl>().FirstOrDefault();
        if (Lead is null && Children.OfType<Button>().FirstOrDefault(b => b != More) is { } lead)
        {
            // lit or not decides whether it may go into the menu, and lighting it changes no size: measure again
            Lead = lead;
            lead.Classes.CollectionChanged += (_, _) => InvalidateMeasure();
            lead.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) InvalidateMeasure(); };
        }
        if (Items is null) return default;
        var w = double.IsInfinity(available.Width) ? available.Width : Math.Max(0, available.Width - Reserve);
        Lead?.Measure(new Size(double.PositiveInfinity, available.Height));
        _leadIn = LeadThere;
        var lw = _leadIn ? Lead!.DesiredSize.Width + Gap : 0;
        Items.Measure(new Size(Math.Max(0, w - lw), available.Height));
        var hidden = TabsPanel?.Hidden.Count ?? 0;
        if (hidden == 0) { More.IsVisible = false; return new Size(lw + Items.DesiredSize.Width, Math.Max(Items.DesiredSize.Height, _leadIn ? Lead!.DesiredSize.Height : 0)); }
        // the row overflows: the lead goes into the menu too, unless it's the board showing
        if (_leadIn && !Lead!.Classes.Contains("all")) { _leadIn = false; lw = 0; }
        var extra = LeadThere && !_leadIn ? 1 : 0;
        // the button, as wide as its widest wording, takes its place off the end of the row, then the tabs fit what's left
        var all = (Items.ItemsSource is System.Collections.ICollection c ? c.Count : hidden) + extra;
        More.IsVisible = true;
        More.Content = $"▾ {all} more";
        More.Measure(new Size(double.PositiveInfinity, available.Height));
        var mw = More.DesiredSize.Width;
        Items.Measure(new Size(Math.Max(0, w - lw - mw - Gap), available.Height));
        hidden = TabsPanel?.Hidden.Count ?? hidden;
        More.Content = $"▾ {hidden + extra} more";
        More.Measure(new Size(double.PositiveInfinity, available.Height));
        FillMenu();
        return new Size(lw + Items.DesiredSize.Width + Gap + More.DesiredSize.Width, Math.Max(Items.DesiredSize.Height, More.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Items is null) return finalSize;
        var x = 0.0;
        if (Lead is not null)
        {
            // in the menu, it is kept off the row, as the panel keeps a hidden tab
            Lead.IsHitTestVisible = _leadIn;
            if (_leadIn) { Lead.Arrange(new Rect(0, 0, Lead.DesiredSize.Width, finalSize.Height)); x = Lead.DesiredSize.Width + Gap; }
            else Lead.Arrange(new Rect(-100000, 0, Lead.DesiredSize.Width, finalSize.Height));
        }
        var iw = Items.DesiredSize.Width;
        Items.Arrange(new Rect(x, 0, iw, finalSize.Height));
        if (More.IsVisible)
        {
            // the whole height, so its own centring puts it level with the tabs (each is centred in the same height)
            More.Arrange(new Rect(x + iw + Gap, 0, More.DesiredSize.Width, finalSize.Height));
        }
        else More.Arrange(default);
        return finalSize;
    }
}
