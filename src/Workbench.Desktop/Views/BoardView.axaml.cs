using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

/// <summary>Cards are picked up and carried (DragLift): drop on a list to move there, the line shows
/// where; a click opens the card, Ctrl- or Shift-click picks it (then a plain click picks too). Ctrl+A
/// picks every card showing (a column header's Pick all, just that column), Escape unpicks them; picked cards can each be given an agent in one go.</summary>
public partial class BoardView : UserControl
{
    static BoardView() => RenameBox.Hook();

    public BoardView()
    {
        InitializeComponent();
        _ = new DragLift(this, Overlay, "card", "list",
            itemsOf: list => (list as ListViewModel)?.Cards.Cast<object>().ToList() ?? [],
            drop: (item, list, before) =>
            {
                if (DataContext is BoardViewModel board && item is CardViewModel card)
                    board.Move(card, list, (before as CardViewModel)?.Id);
            },
            click: (item, keys) => { if (DataContext is BoardViewModel board && item is CardViewModel card) board.Click(card, keys); });
        ChipDoubleClicks(this);
        // a column's name: double-click to rename; in the box, Enter saves and Esc leaves it as it was
        AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if (e.Source is TextBlock { Tag: ListViewModel list } t && t.Classes.Contains("listname")) { list.StartRenameCommand.Execute(null); e.Handled = true; }
        });
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
