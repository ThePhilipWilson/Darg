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
        Closed += (_, _) => Notifier.Clear();
    }
}