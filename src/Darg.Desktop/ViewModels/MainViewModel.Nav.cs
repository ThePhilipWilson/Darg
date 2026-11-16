using System.ComponentModel;
using Avalonia.Threading;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The mouse's back and forward buttons (WOR-0052): a history of where the person has been, as
/// project tab, section and what's open in it (the doc, the agent, the artifact, the code file), walked
/// as a browser walks its pages. A step is recorded once whatever moved has settled, so opening an agent
/// from a notification (tab, section, agent: three changes) is one step, not three.</summary>
public partial class MainViewModel
{
    readonly NavHistory _nav = new();
    bool _navPending;

    /// <summary>Called for each project as it's made: its section and what's open in it count as moves.</summary>
    void WatchNav(ProjectViewModel p)
    {
        p.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProjectViewModel.Section)) NavMoved(); };
        void On(INotifyPropertyChanged? vm, string property)
        {
            if (vm is not null) vm.PropertyChanged += (_, e) => { if (e.PropertyName == property) NavMoved(); };
        }
        On(p.Docs, nameof(DocsViewModel.Selected));
        On(p.Agents, nameof(AgentsViewModel.Selected));
        On(p.Artifacts, nameof(ArtifactsViewModel.Selected));
        On(p.Code, nameof(CodeViewModel.Current));
    }

    /// <summary>Something that may be a move changed: look once it has settled.</summary>
    void NavMoved()
    {
        if (_navPending) return;
        _navPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _navPending = false;
            if (Here() is { } place) _nav.Visit(place);
        });
    }

    /// <summary>Where the person is now, or null with no project open.</summary>
    NavPlace? Here()
    {
        if (Selected is not { } p) return null;
        var item = p.Section switch
        {
            Section.Docs => p.Docs?.Selected?.Path,
            Section.Agents => p.Agents?.Selected?.Id,
            Section.Artifacts => p.Artifacts?.Selected?.Artifact.Path,
            Section.Code => p.Code?.Current?.FullPath,
            _ => null,
        };
        return new NavPlace(p.Config.Id, p.Section.ToString(), item);
    }

    /// <summary>The mouse's back button.</summary>
    public void GoBack() => Walk(back: true);
    /// <summary>The mouse's forward button.</summary>
    public void GoForward() => Walk(back: false);

    void Walk(bool back)
    {
        if (Setup is not null || ToolsPage is not null) return; // they sit over everything; leaving them is their own buttons' job
        // a step whose project has since gone (removed in Setup) is passed over
        while ((back ? _nav.Back() : _nav.Forward()) is { } place)
            if (Go(place)) return;
    }

    /// <summary>Show a place again. False if its project is gone. What it had open comes back if it's still
    /// there (an agent since forgotten or a code tab since closed leaves the section as it is).</summary>
    bool Go(NavPlace place)
    {
        var p = Projects.FirstOrDefault(x => string.Equals(x.Config.Id, place.Project, StringComparison.OrdinalIgnoreCase));
        if (p is null || !Enum.TryParse<Section>(place.Section, out var section) || !p.Has(section)) return false;
        Selected = p;
        p.Section = section;
        if (place.Item is { } item)
            switch (section)
            {
                case Section.Docs: p.Docs!.Show(item); break;
                case Section.Agents when p.Agents!.Find(item) is { } a: p.Agents.Selected = a; break;
                case Section.Artifacts: p.Artifacts!.Show(item); break;
                case Section.Code when p.Code!.Tabs.FirstOrDefault(f => string.Equals(f.FullPath, item, StringComparison.OrdinalIgnoreCase)) is { } f:
                    p.Code.Current = f; break;
            }
        // the step is where it landed, so the moves just made don't count as a new one
        if (Here() is { } landed) _nav.Landed(landed);
        return true;
    }
}
