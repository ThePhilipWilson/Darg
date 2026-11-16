using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>A project's consoles: tabs, each its own shell; the project's saved commands (dev servers,
/// test runs, a stack's "up") each open a tab running that command and leave the shell open after it.</summary>
public partial class ConsolesViewModel(ProjectConfig project) : ObservableObject
{
    public ObservableCollection<ConsoleTab> Tabs { get; } = [];
    public IReadOnlyList<CommandButton> Commands { get; } = project.Commands.Select(c => new CommandButton(c)).ToList();
    public bool HasCommands => Commands.Count > 0;
    [ObservableProperty] public partial ConsoleTab? Selected { get; set; }
    /// <summary>Raised to close a tab's process (the view owns the terminal controls).</summary>
    public event Action<ConsoleTab>? Closing;

    public static string Shell => OperatingSystem.IsWindows() ? "pwsh.exe" : Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";

    /// <summary>The first time the section opens, there's a shell waiting.</summary>
    public void EnsureOne() { if (Tabs.Count == 0) NewShell(); }

    [RelayCommand]
    void NewShell() => Open(new ConsoleTab(this, "Shell", project.WorkingFolder(), Shell, []));

    public void Run(SavedCommand c)
    {
        var cwd = c.Cwd is { Length: > 0 } d ? (Path.IsPathRooted(d) ? d : Path.Combine(project.WorkingFolder(), d)) : project.WorkingFolder();
        // the command runs in a shell that stays open afterwards, so its output (or error) can be read
        string[] args = OperatingSystem.IsWindows() ? ["-NoLogo", "-NoExit", "-Command", c.Command] : ["-c", c.Command + "; exec " + Shell];
        Open(new ConsoleTab(this, c.Name, cwd, Shell, args));
    }

    void Open(ConsoleTab t)
    {
        Tabs.Add(t);
        Selected = t;
    }

    internal void Close(ConsoleTab t)
    {
        Closing?.Invoke(t);
        var i = Tabs.IndexOf(t);
        Tabs.Remove(t);
        if (Selected == t) Selected = Tabs.Count == 0 ? null : Tabs[Math.Min(i, Tabs.Count - 1)];
    }

    public sealed class CommandButton(SavedCommand c)
    {
        public string Name { get; } = c.Name;
        public string Tip { get; } = c.Command;
        public SavedCommand Command { get; } = c;
    }

    [RelayCommand] void RunCommand(CommandButton? b) { if (b is not null) Run(b.Command); }
}

public sealed partial class ConsoleTab(ConsolesViewModel owner, string name, string cwd, string process, IReadOnlyList<string> args) : ObservableObject
{
    public string Name { get; } = name;
    public string Cwd { get; } = cwd;
    public string Process { get; } = process;
    public IReadOnlyList<string> Args { get; } = args;
    [ObservableProperty] public partial bool Ended { get; set; }
    IRelayCommand? _close;
    public IRelayCommand CloseCommand => _close ??= new RelayCommand(Close);
    internal ConsolesViewModel Owner { get; } = owner;
    public void Close() => Owner.Close(this);
}
