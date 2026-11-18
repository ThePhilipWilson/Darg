namespace Darg.Core;

/// <summary>The person's home folder, where Darg reads what other tools keep in ~/.claude: claude-code-timer's
/// timings, Claude settings, the user CLAUDE.md, claude-draw's install. One place so the integration tests can
/// point it at an empty folder and never read (or picture) the real one (WOR-0127).</summary>
public static class UserHome
{
    static string? _folder;

    /// <summary>The home folder: the OS's user profile unless something set another.</summary>
    public static string Folder
    {
        get => _folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        set => _folder = value;
    }

    /// <summary>~/.claude.</summary>
    public static string Claude => Path.Combine(Folder, ".claude");
}
