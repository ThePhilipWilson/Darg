namespace Darg.Core;

/// <summary>How long an agent may sit idle before Darg gives its claude process back (WOR-0209). Each process holds about
/// 0.5 GB and its MCP servers about 1 GB more (WOR-0207); the agent stays listed and a message resumes it with --resume.
/// Set in <c>settings.json</c> as <c>idleCloseMinutes</c> (0 never closes one).</summary>
public static class IdleClose
{
    public const int DefaultMinutes = 10;

    static TimeSpan? _limit;
    static bool _loaded;

    /// <summary>The idle time that closes an agent's session; <see cref="TimeSpan.Zero"/> means never.</summary>
    public static TimeSpan Limit
    {
        get
        {
            if (!_loaded) Load();
            return _limit ?? TimeSpan.FromMinutes(DefaultMinutes);
        }
        set { _limit = value; _loaded = true; }
    }

    /// <summary>Read the instance's setting (again); the app does this as it starts.</summary>
    public static void Load(string? instanceFolder = null)
    {
        _limit = TimeSpan.FromMinutes(Math.Max(0, AppSettings.Load(instanceFolder).IdleCloseMinutes));
        _loaded = true;
    }
}
