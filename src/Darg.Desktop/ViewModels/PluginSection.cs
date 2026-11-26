using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A project's plugin as a section (HOM-0001): a rail button and its page in a web view, served by the
/// <see cref="ArtifactHost"/> with the <c>darg.run</c> bridge. The page loads the first time the section shows and stays
/// loaded after, so switching away and back keeps its state.</summary>
public partial class PluginSection(string project, PluginInfo info) : ObservableObject
{
    public PluginInfo Info { get; } = info;
    public string Project { get; } = project;
    public string Name => Info.Name;
    public string Icon => Info.Icon;
    public string Tip => $"{Info.Name}: {Info.Tip}";

    /// <summary>It is the section on screen.</summary>
    [ObservableProperty] public partial bool Showing { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPage))] public partial Uri? Page { get; set; }
    public bool HasPage => Page is not null;

    /// <summary>Where the page is served, or "" when the server couldn't start.</summary>
    public string Address => ArtifactHost.Server?.PluginUrl(Project, Info) ?? "";
    public string Problem => Address.Length == 0
        ? $"{Info.Name} can't be shown: Darg's page server didn't start (another program may hold port {ArtifactServer.PreferredPort})."
        : "";

    partial void OnShowingChanged(bool value)
    {
        if (value && Page is null && Address.Length > 0) Page = new Uri(Address);
    }

    /// <summary>Load the page again (after its files changed, or the theme did).</summary>
    [RelayCommand]
    public void Reload()
    {
        if (Address.Length > 0) Page = new Uri(Address + $"?r={DateTime.Now.Ticks}");
    }

    [RelayCommand]
    void OpenInBrowser()
    {
        if (Address.Length == 0) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Address) { UseShellExecute = true }); } catch (Exception) { }
    }

    [RelayCommand]
    void OpenFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Info.Folder) { UseShellExecute = true }); } catch (Exception) { }
    }
}
