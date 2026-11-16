namespace Workbench.Core;

/// <summary>Filtering by labels (or tags): every "must" label has to be there (AND), and if any "any"
/// labels are picked, at least one of those has to be there too (OR). Nothing picked keeps everything.</summary>
public static class LabelFilter
{
    public static bool Matches(IEnumerable<string> labels, IReadOnlyCollection<string> any, IReadOnlyCollection<string> all)
    {
        var have = new HashSet<string>(labels, StringComparer.OrdinalIgnoreCase);
        return all.All(have.Contains) && (any.Count == 0 || any.Any(have.Contains));
    }
}
