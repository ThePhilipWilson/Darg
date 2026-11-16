using Avalonia.Controls;

namespace Workbench.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new ViewModels.MainViewModel();
        DataContext = vm;
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
    }
}