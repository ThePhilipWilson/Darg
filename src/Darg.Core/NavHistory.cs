namespace Darg.Core;

/// <summary>A place in the app: a project's tab, a section of it, and what's open there (a doc, an agent,
/// an artifact), or null for the section with nothing in particular open.</summary>
public sealed record NavPlace(string Project, string Section, string? Item = null);

/// <summary>Where the person has been, for the mouse's back and forward buttons (WOR-0052), as a browser
/// keeps it: going somewhere new drops what was ahead. Something opening where nothing was open (a doc
/// picked in a Docs section that had none) takes the place of that empty step rather than adding one,
/// so back doesn't stop on a page that looks the same.</summary>
public sealed class NavHistory(int limit = 100)
{
    readonly List<NavPlace> _places = [];
    int _at = -1;

    public NavPlace? Current => _at >= 0 ? _places[_at] : null;
    public bool CanGoBack => _at > 0;
    public bool CanGoForward => _at < _places.Count - 1;

    /// <summary>The person is now at <paramref name="place"/>: a new step, unless it's where they are.</summary>
    public void Visit(NavPlace place)
    {
        if (place == Current) return;
        _places.RemoveRange(_at + 1, _places.Count - _at - 1);
        if (Current is { Item: null } here && here.Project == place.Project && here.Section == place.Section)
        {
            _places[_at] = place;
            return;
        }
        _places.Add(place);
        if (_places.Count > limit) _places.RemoveAt(0);
        _at = _places.Count - 1;
    }

    /// <summary>The step before, or null at the first.</summary>
    public NavPlace? Back() => CanGoBack ? _places[--_at] : null;

    /// <summary>The step after one gone back from, or null when there's none.</summary>
    public NavPlace? Forward() => CanGoForward ? _places[++_at] : null;

    /// <summary>Going back or forward landed somewhere a little different (the doc it named is gone):
    /// the step becomes where it landed, keeping the steps either side.</summary>
    public void Landed(NavPlace place) { if (_at >= 0) _places[_at] = place; }
}
