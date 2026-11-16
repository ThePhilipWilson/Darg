using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core;

namespace Workbench.Desktop.ViewModels;

/// <summary>A project's consoles: tabs, each its own shell; the project's saved commands (dev servers,
/// test runs, a stack's "up") each open a tab running that command and leave the shell open after it.</summary>
public partial class ConsolesViewModel : ObservableObject
{
    public ObservableCollection<ConsoleTab> Tabs { get; } = [];
    public IReadOnlyList<CommandButton> Commands { get; }
    public bool HasCommands => Commands.Count > 0;
    [ObservableProperty] public partial ConsoleTab? Selected { get; set; }
    /// <summary>Raised to close a tab's process (the view owns the terminal controls).</summary>
    public event Action<ConsoleTab>? Closing;

    /// <summary>Every shell installed here, for the ▾ menu beside + Shell.</summary>
    public IReadOnlyList<ShellChoice> ShellChoices => _choices ??= Shells.Available().Select(sh => new ShellChoice(this, sh)).ToList();
    IReadOnlyList<ShellChoice>? _choices;

    /// <summary>What + Shell opens: the shell last picked from the menu (kept in instance.json, so every
    /// project and the next run open it too), else the platform's usual one.</summary>
    [ObservableProperty] public partial string DefaultShell { get; set; } = "";
    public string NewShellTip => DefaultShell.Length == 0 ? "Open a console" : $"Open a {DefaultShell} console (▾ for another shell)";

    public ConsolesViewModel(ProjectConfig project)
    {
        _project = project;
        SelectedRepo = RepoChoices.FirstOrDefault();
        Commands =project.Commands.Select(c => new CommandButton(c)).ToList();
        // WSL's distro list is slow to ask for: have the shells ready before the section opens
        _ = Task.Run(() =>
        {
            var name = Shells.Pick(SavedDefault()).Name;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (DefaultShell.Length == 0) DefaultShell = name; });
        });
    }

    readonly ProjectConfig _project;

    partial void OnDefaultShellChanged(string value) => OnPropertyChanged(nameof(NewShellTip));

    /// <summary>The first time the section opens, there's a shell waiting.</summary>
    public void EnsureOne() { if (Tabs.Count == 0) NewShell(); }

    [RelayCommand]
    void NewShell() => OpenShell(Shells.Pick(SavedDefault()));

    string? SavedDefault()
    {
        try { return InstanceConfig.Load().Shell; } catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return null; }
    }

    void OpenShell(ShellInfo sh)
    {
        DefaultShell = sh.Name;
        var folder = SelectedRepo?.Path ?? _project.WorkingFolder();
        // in a project with several repos the tab says which one it's in
        var name = HasRepoChoice && SelectedRepo is { } r ? $"{sh.Name} · {r.Name}" : sh.Name;
        Open(new ConsoleTab(this, name, folder, sh.Path, sh.Args));
    }

    /// <summary>The project's repos, for the picker beside + Shell (shown when there's more than one).</summary>
    public IReadOnlyList<RepoChoice> RepoChoices => _repoChoices ??= _project.ExistingRepos().Select(r => new RepoChoice(r)).ToList();
    IReadOnlyList<RepoChoice>? _repoChoices;
    public bool HasRepoChoice => RepoChoices.Count > 1;
    /// <summary>Where + Shell and the ▾ shells open; the first repo until another is picked.</summary>
    [ObservableProperty] public partial RepoChoice? SelectedRepo { get; set; }

    /// <summary>A shell picked from the menu opens and becomes what + Shell opens from now on.</summary>
    internal void Pick(ShellInfo sh)
    {
        OpenShell(sh);
        try
        {
            var config = InstanceConfig.Load();
            if (config.Shell == sh.Name) return;
            config.Shell = sh.Name;
            config.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { } // still opened; just not remembered
    }

    public void Run(SavedCommand c)
    {
        var cwd = c.Cwd is { Length: > 0 } d ? (Path.IsPathRooted(d) ? d : Path.Combine(_project.WorkingFolder(), d)) : _project.WorkingFolder();
        // saved commands always run in the platform's usual shell (PowerShell on Windows), the one they were
        // written for, whatever + Shell opens; the shell stays open afterwards, so the output can be read
        var sh = Shells.Available()[0];
        Open(new ConsoleTab(this, c.Name, cwd, sh.Path, sh.RunArgs(c.Command)));
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

    public sealed class ShellChoice(ConsolesViewModel owner, ShellInfo shell)
    {
        public string Name { get; } = shell.Name;
        public string Tip { get; } = shell.Path;
        IRelayCommand? _open;
        public IRelayCommand OpenCommand => _open ??= new RelayCommand(() => owner.Pick(shell));
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
