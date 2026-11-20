using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

/// <summary>Cards are picked up and carried (DragLift): drop on a list to move there, the line shows
/// where; a click opens the card, Ctrl- or Shift-click picks it (then a plain click picks too); dragging a picked
/// card carries every picked card with it. Ctrl+A
/// picks every card showing (a column header's Pick all, just that column), Escape unpicks them; picked cards can each be given an agent in one go.</summary>
public partial class BoardView : UserControl
{
    static BoardView() => RenameBox.Hook();

    public BoardView()
    {
        InitializeComponent();
        AddCardPicture.Click += async (_, _) =>
        {
            if (DataContext is BoardViewModel { Editor: { } editor } && await PicturePicker.Pick(this) is { } path) editor.AddPicture(path);
        };
        _ = new DragLift(this, Overlay, "card", "list",
            itemsOf: list => (list as ListViewModel)?.Cards.Cast<object>().ToList() ?? [],
            // each row is a board: a card dropped on another row's list goes to that board
            drop: (items, list, before) =>
            {
                if (DataContext is BoardViewModel board && board.ListByKey(list) is { } l)
                    l.Board.Drop(items.OfType<CardViewModel>().ToList(), l, (before as CardViewModel)?.Id);
            },
            click: (item, keys) => { if (item is CardViewModel card) card.Board.Click(card, keys); },
            carried: item => item is CardViewModel card ? card.Board.Carried(card) : [item]);
        // several boards share the height, up to three on screen at once; more scroll
        RowsScroll.SizeChanged += (_, _) => Fit();
        BoardViewModel? watched = null;
        DataContextChanged += (_, _) =>
        {
            if (watched is not null) watched.RowsChanged -= Fit;
            watched = DataContext as BoardViewModel;
            if (watched is not null) watched.RowsChanged += Fit;
            Fit();
        };
        ChipDoubleClicks(this);
        // a board tab pressed with Ctrl adds or removes it (WOR-0149); the button's click reads the flag
        AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            if (DataContext is BoardViewModel b && (e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Button>().Any(x => x.DataContext is BoardTab) == true)
                b.TabCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // a column's name: double-click to rename; in the box, Enter saves and Esc leaves it as it was
        AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if (e.Source is TextBlock { Tag: ListViewModel list } t && t.Classes.Contains("listname")) { list.StartRenameCommand.Execute(null); e.Handled = true; }
        });
        // an enlarged picture (WOR-0167) takes Escape and the arrows first, wherever focus is
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is not BoardViewModel { Editor: { IsEnlarged: true } editor }) return;
            if (e.Key == Avalonia.Input.Key.Escape) editor.CloseEnlargedCommand.Execute(null);
            else if (e.Key is Avalonia.Input.Key.Right or Avalonia.Input.Key.Down) editor.StepEnlarged(1);
            else if (e.Key is Avalonia.Input.Key.Left or Avalonia.Input.Key.Up) editor.StepEnlarged(-1);
            else return;
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Source is not TextBox { Tag: ListViewModel list } box || !box.Classes.Contains("rename")) return;
            if (e.Key == Avalonia.Input.Key.Enter) { list.CommitRenameCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Avalonia.Input.Key.Escape) { list.CancelRenameCommand.Execute(null); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            if (DataContext is not BoardViewModel board || e.Source is TextBox) return;
            if (e.Key == Avalonia.Input.Key.A && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) { board.PickAllCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Avalonia.Input.Key.Escape && board.HasPicked) { board.ClearPicked(); e.Handled = true; }
        };
    }

    void LightboxReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left || DataContext is not BoardViewModel { Editor: { } editor }) return;
        editor.CloseEnlargedCommand.Execute(null);
        e.Handled = true;
    }

    void Fit()
    {
        if (DataContext is not BoardViewModel board || RowsScroll.Bounds.Height <= 0) return;
        var h = RowsScroll.Bounds.Height;
        var n = board.Rows.Count;
        var each = n <= 1 ? h : Math.Max(320, Math.Floor(h / Math.Min(n, 3)));
        foreach (var row in board.Rows) row.RowHeight = each;
    }

    /// <summary>A double-click on a filter chip makes it "must have" (AND). Seen on the second press (its
    /// click count is 2), before the button turns the release into a click; the chip then ignores that click.
    /// (The DoubleTapped gesture never reaches here: the button swallows it.)</summary>
    internal static void ChipDoubleClicks(Control host) =>
        host.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            // the press lands on the chip's text, whose data is the label's string: find the chip's button
            var chip = (e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Button>()
                .Select(b => b.DataContext).OfType<LabelChip>().FirstOrDefault();
            if (chip is null) return;
            // clicks 1, 3, 5… start a pair; 2, 4, 6… finish a double (Windows keeps counting while the pointer stays put)
            if (e.ClickCount % 2 == 1) chip.PressStarts(); else chip.DoubleClick();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
}

/// <summary>A column's name box (a TextBox with the rename class, on the board or the notes wall) takes the
/// keyboard, its text selected, the moment it shows: when it turns visible, or when it is made already
/// visible (a column just added is built with its name open).</summary>
static class RenameBox
{
    static bool _hooked;
    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        Visual.IsVisibleProperty.Changed.AddClassHandler<TextBox>((box, e) => { if (e.NewValue is true) Grab(box); });
        Control.LoadedEvent.AddClassHandler<TextBox>((box, _) => { if (box.IsVisible) Grab(box); });
    }

    static void Grab(TextBox box)
    {
        if (!box.Classes.Contains("rename")) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (box.IsEffectivelyVisible) { box.Focus(); box.SelectAll(); } }, Avalonia.Threading.DispatcherPriority.Input);
    }
}
