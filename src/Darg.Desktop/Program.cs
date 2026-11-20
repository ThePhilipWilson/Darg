using Avalonia;
using System;

namespace Darg.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // too many agents' copies at once ran the machine out of memory (WOR-0170): say why in crash.log, where agents look
        var refusal = Darg.Core.Copies.Refusal(Environment.ProcessPath ?? "", Darg.Core.Copies.OthersRunning(),
            Darg.Core.Copies.Max(Environment.GetEnvironmentVariable(Darg.Core.Copies.MaxVar)), Darg.Core.Copies.Exempt);
        if (refusal is not null)
        {
            Console.Error.WriteLine(refusal);
            try
            {
                var folder = Darg.Core.InstanceConfig.Folder();
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "crash.log"), $"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{refusal}\n\n");
            }
            catch (Exception) { }
            Environment.Exit(3);
        }
        Darg.Core.ChildProcesses.TieToThisProcess();
        Notifier.Init();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
