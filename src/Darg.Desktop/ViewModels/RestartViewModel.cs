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
    readonly string _install;
    static string FailedFile => Path.Combine(InstanceConfig.Folder(), "update-failed.txt");
    static string HelperFile => Path.Combine(InstanceConfig.Folder(), "restart.ps1");

    /// <summary>The repo this copy was published from; null for a copy that can only restart.</summary>
    public string? Source { get; }
    public bool CanUpdate => Source is not null && Live is null;
    /// <summary>Set when this is a live copy (tools/live.ps1, WOR-0126): updates are pulled in and applied
    /// while it runs, rather than built and restarted into.</summary>
    public LiveCopy? Live { get; }
    public bool IsLive => Live is not null;

    /// <summary>Which copy this is, as the header names it (WOR-0242): only the live copy takes changes while it runs, and
    /// the installed copy looked the same as it, so a change that never arrived was a surprise.</summary>
    public string CopyName => Live is not null ? "live" : Source is not null ? "installed" : "debug";
    public string HeaderTip => Live is { } live
        ? $"The live copy, running under dotnet watch from {live.Tree}: Pull in takes {live.Branch}'s new commits while it runs."
        : Source is { } repo
        ? $"The installed copy, from {_install}. It doesn't hot reload: what lands in {repo} reaches it only through Update, which restarts it "
          + "(work agents on cards carry on by themselves afterwards). tools/live.ps1 runs the live copy, which takes changes while it runs."
          + (Built ? " An update is built: restart into it when you're ready." : HasChanges == true ? $" {repo} has changes this copy doesn't." : "")
        : $"A debug copy, from {_install}: it doesn't hot reload or update itself; it can only restart into the same build.";
    /// <summary>Commits the followed branch has that the live copy hasn't pulled in; null when git can't say.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SourceNote), nameof(PullInLabel), nameof(CanPullIn), nameof(HeaderLabel))] public partial int? Behind { get; set; }
    public bool CanPullIn => Behind > 0;
    public string PullInLabel => Behind is not > 0 || Live is null ? "Up to date"
        : Armed ? "Pull in anyway" : $"Pull in {(Behind == 1 ? "1 commit" : $"{Behind} commits")} from {Live.Branch}";
    /// <summary>Whether the repo has changed since this copy was built: null when it can't be told.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SourceNote), nameof(UpdateLabel), nameof(HeaderLabel), nameof(HeaderTip))] public partial bool? HasChanges { get; set; }
    public string SourceNote => Live is { } live
        ? $"A live copy, running under dotnet watch from {live.Tree}. " + (Behind switch
        {
            0 => $"It has everything on {live.Branch}.",
            null => $"git couldn't say whether {live.Branch} has moved on.",
            _ => $"{live.Branch} has moved on: Pull in applies it while Darg runs.",
        }) + " An edit hot reload can't take makes dotnet watch close and reopen Darg, which stops agents mid-turn."
        : Source is not { } repo
        ? "This copy wasn't installed by tools/publish.ps1, so it can restart but not update itself."
        : "The installed copy: it doesn't hot reload, so changes reach it only through Update. " + (Built ? (StaleBuild
            ? $"{repo} has changed since the update was built. Rebuild to take those changes in too, or restart into the build you have."
            : $"The update is built from {repo} as it is now.")
        : HasChanges switch
        {
            false => $"Nothing to update: {repo} hasn't changed since this copy was built.",
            true => $"{repo} has changes this copy doesn't. Update builds it as it is on disk now (uncommitted changes too), while you carry on. Restarting into it closes and reopens Darg.",
            _ => $"Update builds {repo} as it is on disk now (uncommitted changes too), while you carry on. Restarting into it closes and reopens Darg. (This copy predates update checking, so it can't say whether anything changed.)",
        });

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UpdateLabel), nameof(HeaderLabel))] public partial bool Building { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UpdateLabel), nameof(HeaderLabel), nameof(SourceNote), nameof(CanUseBuilt), nameof(HeaderTip))] public partial bool Built { get; set; }
    /// <summary>The repo changed after the update was built (or while it was building): Update builds again
    /// rather than restarting into a build that leaves those changes out (WOR-0104).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UpdateLabel), nameof(HeaderLabel), nameof(SourceNote), nameof(CanUseBuilt))] public partial bool StaleBuild { get; set; }
    /// <summary>A built update that's behind the repo can still be restarted into, from its own button.</summary>
    public bool CanUseBuilt => Built && StaleBuild;
    [ObservableProperty] public partial string Status { get; set; } = "";
    /// <summary>Why the last update went wrong: a build error, or a copy the helper couldn't finish.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasFailure), nameof(HeaderLabel))] public partial string Failure { get; set; } = "";
    public bool HasFailure => Failure.Length > 0;
    /// <summary>Agents mid-turn, said when the menu opens and before a restart.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWarning))] public partial string Warning { get; set; } = "";
    public bool HasWarning => Warning.Length > 0;
    /// <summary>Set by a first click while agents are mid-turn: the next click restarts anyway.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RestartLabel), nameof(UpdateLabel), nameof(UseBuiltLabel), nameof(PullInLabel))] public partial bool Armed { get; set; }

    public string RestartLabel => Armed ? "Restart anyway" : "Restart";
    public string UpdateLabel => Building ? "Building…"
        : Built ? (StaleBuild ? "Rebuild" : Armed ? "Restart into it anyway" : "Restart into the new build")
        : (HasChanges == false ? "Rebuild and restart anyway" : "Update and restart");
    public string UseBuiltLabel => Armed ? "Use it anyway" : "Use last build";
    /// <summary>The header button: which copy this is (live, installed or debug), then what's waiting on it.</summary>
    public string HeaderLabel => Live is not null ? (HasFailure ? "⟳ live !" : Behind > 0 ? "⟳ live •" : "⟳ live")
        : Source is null ? "⟳ debug"
        : Building ? "⟳ installed · building…" : Built ? (StaleBuild ? "⟳ installed · update ready •" : "⟳ installed · update ready")
        : HasFailure ? "⟳ installed !" : HasChanges == true ? "⟳ installed •" : "⟳ installed";

    /// <param name="install">The folder this copy runs from (tests give one; else the exe's).</param>
    /// <param name="live">This copy as a live one (tests; else read from the environment tools/live.ps1 sets).</param>
    public RestartViewModel(Func<IReadOnlyList<string>> busyAgents, Action saveAll, string? install = null, LiveCopy? live = null)
    {
        _busyAgents = busyAgents;
        _saveAll = saveAll;
        _install = install ?? Updater.InstallFolder;
        Source = Updater.SourceRepo(_install);
        Live = install is null ? LiveCopy.FromEnvironment() : live;
        if (Live is { } l) Behind = l.Behind();
        else if (Source is { } repo) HasChanges = Updater.HasChanges(repo, _install);
        try { if (File.Exists(FailedFile)) Failure = File.ReadAllText(FailedFile).Trim(); }
        catch (IOException) { }
        // the installed copy looks again every minute, so work landing in its repo shows on the header (•) without the menu
        // being opened: a change that never arrives is then no surprise (WOR-0242)
        if (install is null && Live is null && Source is not null)
        {
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += async (_, _) => await CheckSource();
            timer.Start();
        }
    }

    /// <summary>The installed copy: whether its repo has changes it doesn't, read off the UI thread (the read walks src/).
    /// Nothing while an update builds or waits: the menu checks a built one against the repo when it opens.</summary>
    public async Task CheckSource()
    {
        if (Live is not null || Source is not { } repo || Building || Built) return;
        var changed = await Task.Run(() => Updater.HasChanges(repo, _install));
        if (!Building && !Built) HasChanges = changed;
    }

    /// <summary>The menu opened: say which agents a restart would cut off now.</summary>
    public void Refresh()
    {
        Armed = false;
        if (Live is { } live) Behind = live.Behind();
        else if (!Building && Source is { } repo)
        {
            // a built update is checked against the repo too: a change made after it (or during it) means build again
            if (Built) StaleBuild = Updater.StagedBuildIsStale(repo, _install);
            else HasChanges = Updater.HasChanges(repo, _install);
        }
        var busy = _busyAgents();
        Warning = busy.Count == 0 ? "" :
            $"{Plural(busy.Count, "agent is", "agents are")} mid-turn: {string.Join(", ", busy)}. Restarting stops {(busy.Count == 1 ? "it" : "them")}. " +
            RestartNote;
    }

    /// <summary>What happens to agents stopped mid-turn (WOR-0221's resume, said where the person decides to restart).</summary>
    public const string RestartNote = "Work agents on cards carry on by themselves once Darg is back; chats come back saved, to carry on with a message; "
        + "a QA or Improve run ends and needs starting again.";

    static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    [RelayCommand]
    void Restart() => Go(staging: null);

    [RelayCommand]
    async Task Update()
    {
        if (Building || Source is null) return;
        if (Built && !StaleBuild) { Go(Updater.StagingFolder(_install)); return; }
        // a first build, or a rebuild over a staged one the repo has moved past (Build clears the staging folder first)
        Built = false; StaleBuild = false;
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

    /// <summary>Live copy: move its worktree to the branch's newest commit; dotnet watch applies it in place,
    /// or closes and reopens Darg for an edit it can't, so agents mid-turn are named first, as for a restart.</summary>
    [RelayCommand]
    async Task PullIn()
    {
        if (Live is not { } live || Building) return;
        if (!Armed) { Refresh(); if (HasWarning && Behind > 0) { Armed = true; return; } }
        Armed = false; Building = true; Failure = ""; Status = $"Pulling in {live.Branch}…";
        string? error;
        try { error = await Task.Run(live.PullIn); }
        catch (Exception e) { error = e.Message; }
        Building = false;
        if (error is not null) { Failure = error; Status = ""; return; }
        Status = "Pulled in. dotnet watch applies it in a moment (its window says what it did); views show a change when they next draw.";
        Behind = live.Behind();
    }

    /// <summary>Restart into the built update even though the repo has changed since.</summary>
    [RelayCommand]
    void UseBuilt() { if (Built && !Building) Go(Updater.StagingFolder(_install)); }

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
            // a live copy restarts through live.ps1, so it comes back under dotnet watch
            var script = Live is { } live ? live.HelperScript(Environment.ProcessId)
                : Updater.HelperScript(Environment.ProcessId, exe, args, staging, _install, FailedFile);
            Updater.StartHelper(script, HelperFile);
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
