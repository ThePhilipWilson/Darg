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
            // a list, or one of its state sub-columns (WOR-0214), which is a drop target for the list
            itemsOf: list => (list as ListViewModel)?.Cards.Cast<object>().ToList() ?? (list as StateGroup)?.Cards.Cast<object>().ToList() ?? [],
            // each row is a board: a card dropped on another row's list goes to that board
            drop: (items, list, before) =>
            {
                if (list.StartsWith("queue:") && DataContext is BoardViewModel qb)
                    qb.Queues?.DropOnKey(list, items.OfType<CardViewModel>().Select(c => c.Id).ToList());
                else if (DataContext is BoardViewModel board && board.ListByKey(list) is { } l)
                {
                    // queued cards dragged out of a lane leave their queue (WOR-0241)
                    board.Queues?.TakeOut(items.OfType<CardViewModel>().Select(c => c.Id).ToList());
                    l.Board.Drop(items.OfType<CardViewModel>().ToList(), l, (before as CardViewModel)?.Id);
                }
            },
            click: (item, keys) => { if (item is CardViewModel card) card.Board.Click(card, keys); },
            carried: item => item is CardViewModel card ? card.Board.Carried(card) : [item]);
        // several boards share the height; a row dragged taller keeps it and the rows scroll (WOR-0160)
        RowsScroll.SizeChanged += (_, _) => Fit();
        DataContextChanged += (_, _) => Fit();
        RowDividers(this);
        // a board tab dragged along the row moves there, and the order is kept (WOR-0213)
        TabDrag.Attach(BoardTabs, (from, to) => (DataContext as BoardViewModel)?.MoveBoard(from, to), tabIsButton: true);
        // a queue's lane dragged by its grip moves among the queues, and the order is kept (WOR-0220)
        TabDrag.Attach(QueueLanes, (from, to) => (DataContext as BoardViewModel)?.Queues?.Move(from, to), vertical: true,
            grip: v => v is Border b && b.Classes.Contains("queuegrip"));
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
            // a queue's name too (WOR-0220)
            else if (e.Source is TextBlock { Tag: QueueViewModel queue } q && q.Classes.Contains("queuename")) { queue.StartRenameCommand.Execute(null); e.Handled = true; }
        });
        // a queue's name box: Esc leaves it as it was (Enter renames, through EnterSends)
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Escape || e.Source is not TextBox { Tag: QueueViewModel queue } box || !box.Classes.Contains("rename")) return;
            queue.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
        if (DataContext is BoardViewModel board && RowsScroll.Bounds.Height > 0) board.RowsSpace = RowsScroll.Bounds.Height;
    }

    /// <summary>The divider below each row (WOR-0160): dragged, the row above it follows the pointer (kept for that
    /// board when let go); double-clicked, the row goes back to sharing the height with the others.</summary>
    static void RowDividers(Control host)
    {
        BoardViewModel? row = null;
        double startY = 0, startH = 0;
        host.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("rowdivider")) is not { Tag: BoardViewModel r }
                || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (e.ClickCount == 2) { r.ResetRowHeightCommand.Execute(null); return; }
            row = r;
            startY = e.GetPosition(host).Y;
            startH = r.RowHeight;
            e.Pointer.Capture(host);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        host.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, (_, e) =>
        {
            if (row is null) return;
            row.ResizeRow(startH + e.GetPosition(host).Y - startY, save: false);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        host.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (row is null) return;
            row.ResizeRow(startH + e.GetPosition(host).Y - startY, save: true);
            row = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        host.AddHandler(Avalonia.Input.InputElement.PointerCaptureLostEvent, (_, _) =>
        {
            if (row is null) return;
            row.ResizeRow(row.RowHeight, save: true);
            row = null;
        });
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
