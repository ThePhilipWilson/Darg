using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The ⟳ menu in the header: restart Darg, or rebuild it from its repo and restart into the
/// new build. The build happens while Darg keeps running; only the restart closes it. Agents mid-turn
/// are named first, and restarting then takes a second click.</summary>
public partial class RestartViewModel : ObservableObject
{
    readonly Func<IReadOnlyList<string>> _busyAgents;
    readonly Action _saveAll;
    readonly string _install = Updater.InstallFolder;
    static string FailedFile => Path.Combine(InstanceConfig.Folder(), "update-failed.txt");
    static string HelperFile => Path.Combine(InstanceConfig.Folder(), "restart.ps1");

    /// <summary>The repo this copy was published from; null for a copy that can only restart.</summary>
    public string? Source { get; } = Updater.SourceRepo();
    public bool CanUpdate => Source is not null;
    /// <summary>Whether the repo has changed since this copy was built: null when it can't be told.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SourceNote), nameof(UpdateLabel), nameof(HeaderLabel))] public partial bool? HasChanges { get; set; }
    public string SourceNote => Source is not { } repo
        ? "This copy wasn't installed by tools/publish.ps1, so it can restart but not update itself."
        : HasChanges switch
        {
            false => $"Nothing to update: {repo} hasn't changed since this copy was built.",
            true => $"{repo} has changes this copy doesn't. Update builds it as it is on disk now (uncommitted changes too), while you carry on. Restarting into it closes and reopens Darg.",
            _ => $"Update builds {repo} as it is on disk now (uncommitted changes too), while you carry on. Restarting into it closes and reopens Darg. (This copy predates update checking, so it can't say whether anything changed.)",
        };

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UpdateLabel), nameof(HeaderLabel))] public partial bool Building { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UpdateLabel), nameof(HeaderLabel))] public partial bool Built { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    /// <summary>Why the last update went wrong: a build error, or a copy the helper couldn't finish.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasFailure), nameof(HeaderLabel))] public partial string Failure { get; set; } = "";
    public bool HasFailure => Failure.Length > 0;
    /// <summary>Agents mid-turn, said when the menu opens and before a restart.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWarning))] public partial string Warning { get; set; } = "";
    public bool HasWarning => Warning.Length > 0;
    /// <summary>Set by a first click while agents are mid-turn: the next click restarts anyway.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RestartLabel), nameof(UpdateLabel))] public partial bool Armed { get; set; }

    public string RestartLabel => Armed ? "Restart anyway" : "Restart";
    public string UpdateLabel => Building ? "Building…" : Built ? (Armed ? "Restart into it anyway" : "Restart into the new build") : (HasChanges == false ? "Rebuild and restart anyway" : "Update and restart");
    public string HeaderLabel => Building ? "⟳ Building…" : Built ? "⟳ Update ready" : HasFailure ? "⟳ !" : HasChanges == true ? "⟳ •" : "⟳";

    public RestartViewModel(Func<IReadOnlyList<string>> busyAgents, Action saveAll)
    {
        _busyAgents = busyAgents;
        _saveAll = saveAll;
        if (Source is { } repo) HasChanges = Updater.HasChanges(repo, _install);
        try { if (File.Exists(FailedFile)) Failure = File.ReadAllText(FailedFile).Trim(); }
        catch (IOException) { }
    }

    /// <summary>The menu opened: say which agents a restart would cut off now.</summary>
    public void Refresh()
    {
        Armed = false;
        if (!Built && !Building && Source is { } repo) HasChanges = Updater.HasChanges(repo, _install);
        var busy = _busyAgents();
        Warning = busy.Count == 0 ? "" :
            $"{Plural(busy.Count, "agent is", "agents are")} mid-turn: {string.Join(", ", busy)}. Restarting stops {(busy.Count == 1 ? "it" : "them")}; " +
            "the chat comes back saved, to carry on with.";
    }

    static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    [RelayCommand]
    void Restart() => Go(staging: null);

    [RelayCommand]
    async Task Update()
    {
        if (Building || Source is null) return;
        if (Built) { Go(Updater.StagingFolder(_install)); return; }
        Building = true; Failure = ""; Status = $"Building from {Source}…";
        Forget();
        string? error;
        try
        {
            error = await Updater.Build(Source, _install,
                line => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (Building) Status = line; }));
        }
        catch (Exception e) { error = e.Message; }
        Building = false;
        if (error is not null) { Failure = error; Status = ""; return; }
        Built = true;
        Status = "Built. Restart into it when you're ready; nothing has changed until then.";
        Refresh();
    }

    [RelayCommand]
    void Dismiss() { Failure = ""; Forget(); }

    void Forget() { try { File.Delete(FailedFile); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    /// <summary>Restart: name agents mid-turn first (a second click goes ahead), then start the helper
    /// outside Darg's job and close. The helper waits for Darg to go, copies the new build in
    /// if there is one, and starts it again.</summary>
    void Go(string? staging)
    {
        if (!Armed) { Refresh(); if (HasWarning) { Armed = true; return; } }
        var exe = Environment.ProcessPath ?? Path.Combine(_install, Updater.ExeName);
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        try
        {
            Updater.StartHelper(Updater.HelperScript(Environment.ProcessId, exe, args, staging, _install, FailedFile), HelperFile);
        }
        catch (Exception e)
        {
            Failure = $"Couldn't start the restart helper, so Darg stays open: {e.Message}";
            Armed = false;
            return;
        }
        _saveAll();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
