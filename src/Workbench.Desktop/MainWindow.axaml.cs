using Avalonia.Controls;
using Avalonia;
using System;
using Avalonia.VisualTree;

namespace Workbench.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new ViewModels.MainViewModel();
        DataContext = vm;
        Views.TabDrag.Attach(ProjectTabs, vm.MoveProject);
        Activated += (_, _) => vm.WindowActive = true;
        Deactivated += (_, _) => vm.WindowActive = false;
        vm.BringToFront += () =>
        {
            if (WindowState == Avalonia.Controls.WindowState.Minimized) WindowState = Avalonia.Controls.WindowState.Normal;
            Activate();
            Topmost = true; Topmost = false; // Windows won't always let a background app take focus; this at least raises it
        };
        Closing += (_, _) => vm.SaveAll();
        Closed += (_, _) => Notifier.Clear();

        // Bench's box: Enter sends, Shift+Enter is a new line; the chat follows its newest line.
        MascotBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) return;
            e.Handled = true;
            vm.Mascot.SendCommand.Execute(null);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // the chat keeps to its newest line as it grows (Views.StickToBottom), whichever chat it is and
        // however it got there; opening the bubble or switching chats starts at the bottom
        var follow = new Views.StickToBottom(MascotScroll);
        vm.Mascot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModels.MascotViewModel.Open) && vm.Mascot.Open)
            {
                follow.Reset();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => MascotBox.Focus());
            }
            if (e.PropertyName == nameof(ViewModels.MascotViewModel.Items)) follow.Reset();
        };
        MoveByHeader(MascotBubble, MascotHeader);
    }

    /// <summary>Drag <paramref name="handle"/> to move <paramref name="box"/> anywhere in the window (its
    /// buttons and lists still work as before); a double-click on the handle puts it back where it started.
    /// The box stays wholly inside the window, also when it or the window changes size.</summary>
    void MoveByHeader(Control box, Control handle)
    {
        var shift = new Avalonia.Media.TranslateTransform();
        box.RenderTransform = shift;
        Avalonia.Point? grabbed = null; // pointer position in the window when the drag began, less the shift then
        var root = (Control)Content!;

        // keep the box inside the window: its laid-out place (without the shift) plus the shift
        void Clamp()
        {
            if (!box.IsVisible || box.Bounds.Width <= 0 || (shift.X == 0 && shift.Y == 0)) return; // home is always in view
            var home = box.TranslatePoint(default, root) is { } p ? p - new Avalonia.Point(shift.X, shift.Y) : default;
            double w = root.Bounds.Width, h = root.Bounds.Height;
            shift.X = Math.Clamp(shift.X, -home.X, Math.Max(-home.X, w - box.Bounds.Width - home.X));
            shift.Y = Math.Clamp(shift.Y, -home.Y, Math.Max(-home.Y, h - box.Bounds.Height - home.Y));
        }

        bool OnControl(object? source) => source is Avalonia.Visual v &&
            (v as Control ?? v.FindAncestorOfType<Control>()) is { } c &&
            (c is Button or ComboBox || c.FindAncestorOfType<Button>() is not null || c.FindAncestorOfType<ComboBox>() is not null);

        handle.PointerPressed += (_, e) =>
        {
            if (OnControl(e.Source) || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { shift.X = shift.Y = 0; grabbed = null; e.Handled = true; return; }
            grabbed = e.GetPosition(root) - new Avalonia.Point(shift.X, shift.Y);
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (grabbed is not { } g) return;
            var at = e.GetPosition(root) - g;
            shift.X = at.X; shift.Y = at.Y;
            Clamp();
        };
        void Drop() => grabbed = null;
        handle.PointerReleased += (_, e) => { if (grabbed is null) return; Drop(); e.Pointer.Capture(null); };
        handle.PointerCaptureLost += (_, _) => Drop();
        // after a resize, wait for layout to settle: mid-layout the box's place is still the old one
        // (opening the box widens its panel, which moved it 300px left before this waited)
        void ClampLater() => Avalonia.Threading.Dispatcher.UIThread.Post(Clamp, Avalonia.Threading.DispatcherPriority.Background);
        box.SizeChanged += (_, _) => ClampLater();
        root.SizeChanged += (_, _) => ClampLater();
    }
}