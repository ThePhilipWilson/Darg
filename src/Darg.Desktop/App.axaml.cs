using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Darg.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // A bug in one view must not take the whole app (and every agent and console) down: log it to
        // crash.log in the instance folder and carry on.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            try
            {
                var log = System.IO.Path.Combine(Darg.Core.InstanceConfig.Folder(), "crash.log");
                System.IO.File.AppendAllText(log, $"--- {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.Exception}\n\n");
            }
            catch (System.Exception) { }
            e.Handled = true;
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}