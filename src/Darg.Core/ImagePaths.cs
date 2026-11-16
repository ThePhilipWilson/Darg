using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>Image files a tool's input or output mentions: what a generator saved, a script printed, an
/// upscale wrote. Finds absolute paths (Windows or Unix, plain or JSON-escaped) and relative ones (taken
/// against the agent's folder), keeps those that exist, in order, without repeats.</summary>
public static class ImagePaths
{
    static readonly string Ext = @"\.(?:png|jpe?g|webp|gif|bmp)";
    // a drive path (D:\… or D:/…, with JSON's doubled backslashes too), a Unix path, or a relative one;
    // ends at the extension, so quotes, commas and brackets around it don't matter
    static readonly Regex Path = new(
        @"(?:[A-Za-z]:(?:\\\\|\\|/)|/|\.{0,2}[\\/]|\b)[^\s""'<>|*?\r\n,;()\[\]{}]*?" + Ext + @"\b",
        RegexOptions.IgnoreCase);

    public static List<string> Find(string text, string? cwd, int max = 12)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Path.Matches(text))
        {
            var p = m.Value.Replace(@"\\", @"\").Trim();
            string full;
            try { full = System.IO.Path.IsPathRooted(p) ? p : cwd is null ? p : System.IO.Path.GetFullPath(System.IO.Path.Combine(cwd, p)); }
            catch (ArgumentException) { continue; }
            if (!File.Exists(full) || !seen.Add(System.IO.Path.GetFullPath(full))) continue;
            found.Add(full);
            if (found.Count == max) break;
        }
        return found;
    }
}
