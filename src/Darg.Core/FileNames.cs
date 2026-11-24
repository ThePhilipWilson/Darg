namespace Darg.Core;

/// <summary>File and folder names Darg makes from free text (story names, agent and project ids).</summary>
public static class FileNames
{
    // Windows' own invalid set, used on every OS: a Mac allows ':' in a name, and a folder made there
    // under .darg or the instance must still open on Windows.
    static readonly char[] Invalid = ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];

    /// <summary>The text with every character Windows refuses in a file name (and control characters) made '_'.</summary>
    public static string Safe(string s) => string.Concat(s.Select(c => c < ' ' || Invalid.Contains(c) ? '_' : c));
}
