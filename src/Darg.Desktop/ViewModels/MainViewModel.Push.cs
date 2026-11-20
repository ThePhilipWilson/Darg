using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>Phone notifications (AND-0006): whatever makes a desktop toast also goes to the instance's ntfy topic, and a
/// card waiting on a decision carries its options as buttons. A tap comes back through the reply topic and answers the
/// card as the decide command does, so a decision raised here can be answered from the phone's lock screen.</summary>
public partial class MainViewModel
{
    PushClient? _push;

    /// <summary>The last thing that went wrong with phone notifications, or null (shown in Settings and by the push command).</summary>
    public string? PushProblem { get; private set; }

    /// <summary>What the phone answered last, in a line (shown in Settings).</summary>
    public string? PushLast { get; private set; }

    /// <summary>The instance's push settings now in use (null when off).</summary>
    public PushConfig? PushConfig => _push?.Config;

    /// <summary>Raised when push settings, a problem or an answer changed, for an open Settings page.</summary>
    public event Action? PushChanged;

    /// <summary>Starts (or restarts, or stops) phone notifications with these settings.</summary>
    public void StartPush(PushConfig? config)
    {
        _push?.Dispose();
        _push = null;
        PushProblem = null;
        if (config is { On: true })
        {
            _push = new PushClient(config, new PushStore());
            _push.Answered += a => Avalonia.Threading.Dispatcher.UIThread.Post(() => FromPhone(a));
            _push.Problem += why => Avalonia.Threading.Dispatcher.UIThread.Post(() => { PushProblem = why; PushChanged?.Invoke(); });
            _push.Listen();
        }
        PushChanged?.Invoke();
    }

    /// <summary>Saves push settings to instance.json and starts them; null when saved, else why not.</summary>
    public string? SavePush(PushConfig? config)
    {
        try
        {
            var instance = InstanceConfig.Load();
            instance.Push = config is { On: true } ? config : null;
            instance.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return $"Couldn't save instance.json: {e.Message}"; }
        StartPush(config);
        return null;
    }

    /// <summary>An agent's question or a long turn's end, to the phone too (called where the desktop toast shows).</summary>
    void PushTell(string title, string what, string detail)
    {
        if (_push is not { } push) return;
        var ask = what == "has a question" || what.StartsWith("wants to use ", StringComparison.Ordinal);
        var failed = what.Contains("error", StringComparison.OrdinalIgnoreCase);
        _ = push.Send(PushStore.PlainMessage(push.Config, title, detail, urgent: ask || failed, tag: ask ? "question" : failed ? "x" : "white_check_mark"));
    }

    /// <summary>A card waiting on a decision, to the phone with its options as buttons.</summary>
    void PushDecision(ProjectViewModel project, string card, string title, CardDecision decision)
    {
        if (_push is { } push) _ = push.SendDecision(project.Config.Id, project.Name, card, title, decision);
    }

    /// <summary>A card's decision was answered (here or from the phone): its buttons stop working and the notification goes.</summary>
    void PushSettled(ProjectViewModel project, string card)
    {
        if (_push is { } push) _ = push.Settled(project.Config.Id, card);
    }

    /// <summary>A button tapped on the phone: the card is answered as picking that option on its panel does.</summary>
    void FromPhone(PushAnswer a)
    {
        var project = Projects.FirstOrDefault(p => string.Equals(p.Config.Id, a.Project, StringComparison.OrdinalIgnoreCase));
        string? error = project?.Board is not { } board ? $"{a.Project} isn't open here, so {a.Card} wasn't answered."
            : board.Show(a.Card) is null ? $"No card {a.Card} on {project.Name}'s boards any more."
            : board.Decide(a.Card, a.Label, "");
        PushLast = error is null ? $"{DateTime.Now:HH:mm} {a.Card}: you picked {a.Label} on the phone" : $"{DateTime.Now:HH:mm} {a.Card}: {error}";
        PushChanged?.Invoke();
        // the tap cleared its notification, so a refusal has to come back as a new one
        if (error is not null && _push is { } push)
            _ = push.Send(PushStore.PlainMessage(push.Config, $"Couldn't answer {a.Card}", error, urgent: true, tag: "warning"));
    }

    /// <summary>The push command: says how phone notifications are set up, or changes them.</summary>
    string PushApi(ApiArgs a)
    {
        if (a.Get("off") is "true") return SavePush(null) ?? "Phone notifications are off.";
        if (a.Get("topic") is { } topic)
        {
            var config = new PushConfig
            {
                Topic = topic.Trim() is "new" or "" ? PushConfig.NewTopic() : topic.Trim(),
                Server = a.Get("server") ?? _push?.Config.Server ?? PushConfig.DefaultServer,
                Token = a.Get("token") ?? _push?.Config.Token,
            };
            if (SavePush(config) is { } why) throw new ApiError(why);
        }
        return PushStatus();
    }

    /// <summary>How phone notifications stand, in a few lines.</summary>
    public string PushStatus()
    {
        if (_push is not { } push) return "Phone notifications are off. Turn them on with {topic: \"new\"}.";
        var lines = new List<string>
        {
            $"On: subscribe to topic {push.Config.Topic} on {push.Config.Base} in the ntfy app.",
            $"Answers come back on {push.Config.ReplyTopic}; {push.Store.Pending.Count} decision(s) on the phone waiting.",
        };
        if (PushLast is { } last) lines.Add("Last answer: " + last);
        if (PushProblem is { } problem) lines.Add("Problem: " + problem);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The push-test command and Settings' Send test: a notification to check the phone gets them.</summary>
    public async Task<string> PushTest()
    {
        if (_push is not { } push) return "Phone notifications are off.";
        return await push.Send(PushStore.PlainMessage(push.Config, "Darg: test", "Phone notifications from Darg reach this phone.", urgent: false, tag: "tada"))
            ?? $"Sent a test to {push.Config.Topic}.";
    }
}
