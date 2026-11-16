namespace Darg.Desktop;

/// <summary>OS notifications that lead somewhere: each carries the project and agent it is about, and
/// clicking it raises <see cref="Opened"/> with them, so the app opens that agent rather than a list.
/// Windows: toasts (Action Center), which also reopen the app if it has been closed. Elsewhere: not
/// yet (the tab badge still shows), to do with macOS's UserNotifications.</summary>
public static class Notifier
{
    /// <summary>(project id, agent id) of a notification the person clicked. Raised off the UI thread.</summary>
    public static event Action<string, string>? Opened;

    public static void Init()
    {
#if WINDOWS
        Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = Microsoft.Toolkit.Uwp.Notifications.ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("project", out var p) && args.TryGetValue("agent", out var a)) Opened?.Invoke(p, a);
        };
#endif
    }

    /// <summary>A toast: "Alpha: has a question" over the agent's name, then the detail.</summary>
    public static void Show(string projectId, string agentId, string title, string agentName, string detail)
    {
#if WINDOWS
        try
        {
            new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                .AddArgument("project", projectId)
                .AddArgument("agent", agentId)
                .AddText(title)
                .AddText(agentName)
                .AddText(detail.Length > 180 ? detail[..180] + "…" : detail)
                .Show();
        }
        catch (Exception) { /* notifications off or unavailable: the tab badge still shows */ }
#endif
    }

    /// <summary>Clear this app's notifications from Action Center (on exit, so stale ones don't linger).</summary>
    public static void Clear()
    {
#if WINDOWS
        try { Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.History.Clear(); } catch (Exception) { }
#endif
    }
}
