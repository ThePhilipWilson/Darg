using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Darg.Desktop;

public partial class App : Application
{
    System.Threading.Timer? _perfTimer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // the theme picked in Settings, before any view asks for a colour
        ThemeManager.Apply(Darg.Core.Themes.Get(Darg.Core.AppSettings.Load().Theme));
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
        // a heartbeat on the UI thread: any gap over 200 ms (a freeze) goes to stalls.log with what ran in it (WOR-0076)
        try { Darg.Core.StallWatch.Shared.LogFile = System.IO.Path.Combine(Darg.Core.InstanceConfig.Folder(), "stalls.log"); }
        catch (System.Exception) { }
        // DARG_STALL_MS: a lower threshold, to look at smaller hitches
        if (int.TryParse(System.Environment.GetEnvironmentVariable("DARG_STALL_MS"), out var ms) && ms > 0)
            Darg.Core.StallWatch.Shared.Threshold = System.TimeSpan.FromMilliseconds(ms);
        new Avalonia.Threading.DispatcherTimer(Darg.Core.StallWatch.Shared.Interval, Avalonia.Threading.DispatcherPriority.Input,
            (_, _) => Darg.Core.StallWatch.Shared.Beat()).Start();
        // held over a second: the UI thread's stack goes under the stall in stalls.log; and once a minute, what the
        // app was busy with (CPU, memory, GC, sections, file watchers, API calls) goes to perf.log (WOR-0161)
        UiStacks.Start();
        try { Darg.Core.PerfLog.Shared.LogFile = System.IO.Path.Combine(Darg.Core.InstanceConfig.Folder(), "perf.log"); }
        catch (System.Exception) { }
        var perfEvery = int.TryParse(System.Environment.GetEnvironmentVariable("DARG_PERF_SECONDS"), out var ps) && ps > 0 ? ps : 60;
        Darg.Core.PerfLog.Shared.Tick();
        _perfTimer = new System.Threading.Timer(_ => Darg.Core.PerfLog.Shared.Tick(), null,
            System.TimeSpan.FromSeconds(perfEvery), System.TimeSpan.FromSeconds(perfEvery));
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}