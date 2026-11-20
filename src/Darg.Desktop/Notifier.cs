namespace Darg.Desktop;

/// <summary>OS notifications that lead somewhere: each carries the project and agent it is about, and
/// clicking it raises <see cref="Opened"/> with them, so the app opens that agent rather than a list.
/// Windows: toasts (Action Center), which also reopen the app if it has been closed. Elsewhere: not
/// yet (the tab badge still shows), to do with macOS's UserNotifications.</summary>
public static class Notifier
{
    /// <summary>(project id, agent id) of a notification the person clicked. Raised off the UI thread.</summary>
    public static event Action<string, string>? Opened;

    /// <summary>A notification as shown. NeedsYou: it waits on the person's answer, and says so (WOR-0163).</summary>
    public sealed record Notification(string ProjectId, string AgentId, string Title, string AgentName, string Detail, bool NeedsYou = false);

    /// <summary>The small line a toast that waits on the person's answer carries.</summary>
    public const string NeedsYouLine = "Waiting on your decision";

    /// <summary>When set (the integration tests set it), notifications go here instead of to the OS, and
    /// the app's own are never cleared from Action Center.</summary>
    public static Action<Notification>? Capture { get; set; }

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

    /// <summary>A toast: "Alpha: has a question" over the agent's name, then the detail; one that waits on
    /// the person's answer (<paramref name="needsYou"/>) carries <see cref="NeedsYouLine"/> beneath.</summary>
    public static void Show(string projectId, string agentId, string title, string agentName, string detail, bool needsYou = false)
    {
        if (Capture is { } capture) { capture(new Notification(projectId, agentId, title, agentName, detail, needsYou)); return; }
#if WINDOWS
        // off the UI thread: a toast takes about 100 ms to show, and five agents finishing together froze the window (WOR-0076)
        Task.Run(() =>
        {
            try
            {
                var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddArgument("project", projectId)
                    .AddArgument("agent", agentId)
                    .AddText(title)
                    .AddText(agentName)
                    .AddText(detail.Length > 180 ? detail[..180] + "…" : detail);
                if (needsYou) toast.AddAttributionText("● " + NeedsYouLine);
                toast.Show();
            }
            catch (Exception) { /* notifications off or unavailable: the tab badge still shows */ }
        });
#endif
    }

    /// <summary>Clear this app's notifications from Action Center (on exit, so stale ones don't linger).</summary>
    public static void Clear()
    {
        if (Capture is not null) return;
#if WINDOWS
        try { Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.History.Clear(); } catch (Exception) { }
#endif
    }
}
