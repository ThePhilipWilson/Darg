using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Darg.Core;
#if WINDOWS
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;
#endif

namespace Darg.Desktop.Views;

/// <summary>Voice in (WOR-0118): a button with <c>views:Dictation.Target="{Binding #Box}"</c> toggles dictation into
/// that TextBox. A local Whisper model (small.en, downloaded once to %APPDATA%/Darg/models) listens to the default
/// microphone; each phrase, after a pause, is added at the caret. It replaced Windows' System.Speech, which couldn't
/// follow Philip's voice. Windows only; elsewhere the button is hidden. Nothing is sent, the person still presses
/// Send. One microphone at a time: starting one button stops the other.</summary>
public static class Dictation
{
    public static readonly AttachedProperty<TextBox?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Button, TextBox?>("Target", typeof(Dictation));

    public static TextBox? GetTarget(Button b) => b.GetValue(TargetProperty);
    public static void SetTarget(Button b, TextBox? v) => b.SetValue(TargetProperty, v);

    /// <summary>True where dictation can work at all (Windows with a microphone).</summary>
    public static bool Available { get; } = Probe();

    static bool Probe()
    {
#if WINDOWS
        try { return WaveInEvent.DeviceCount > 0; } catch { return false; }
#else
        return false;
#endif
    }

    static Button? active;
    static string? savedHint;
    static DateTime lastSound;
    static bool heardSound;
    static int session;

    /// <summary>Quiet this long, with nothing heard yet, shows the "No sound" warning. Tests shorten it.</summary>
    public static TimeSpan QuietWarning { get; set; } = TimeSpan.FromSeconds(6);
    /// <summary>Test seam: awaited after the model is loaded, before the mic opens (stands in for a slow load).</summary>
    public static Func<Task>? AfterLoad { get; set; }
    /// <summary>Test seam: the sample source in place of the real microphone (null = the default microphone).</summary>
    public static Func<ISampleSource>? SourceFactory { get; set; }
    /// <summary>Test seam: the folder the model is looked for in (null = %APPDATA%/Darg/models).</summary>
    public static string? ModelFolder { get; set; }

    /// <summary>The status goes where it can be seen: the box's own placeholder. Also appended to dictation.log in the
    /// instance folder so a failure can be read afterwards.</summary>
    static void Say(Button b, string text)
    {
        if (active != b) return;
        if (GetTarget(b) is { } box) box.PlaceholderText = text;
        ToolTip.SetTip(b, text);
        try { File.AppendAllText(Path.Combine(InstanceConfig.Folder(), "dictation.log"), $"{DateTime.Now:HH:mm:ss} {text}\n"); } catch { }
    }

    static Dictation() => TargetProperty.Changed.AddClassHandler<Button>((b, e) =>
    {
        b.Click -= OnClick;
        if (e.NewValue is null) return;
        b.Click += OnClick;
        b.IsVisible = Available;
    });

    static void OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var was = active;
        Stop();
        if (was == b) return;
        Start(b);
    }

#if WINDOWS
    const string ModelFile = "ggml-small.en.bin";
    static Listening? current;
    static WhisperFactory? factory;
    static readonly SemaphoreSlim loading = new(1, 1);

    public static string ModelPath => Path.Combine(ModelFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Darg", "models"), ModelFile);

    static async Task<WhisperFactory> LoadAsync(Action<string> say)
    {
        await loading.WaitAsync();
        try
        {
            if (factory != null) return factory;
            var path = ModelPath;
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                say("Downloading the speech model (about 470 MB, once)...");
                var tmp = path + ".part";
                using (var net = await new WhisperGgmlDownloader(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }).GetGgmlModelAsync(GgmlType.SmallEn))
                using (var file = File.Create(tmp))
                    await net.CopyToAsync(file);
                File.Move(tmp, path, true);
            }
            say("Loading the speech model (the first time after starting Darg takes a few seconds)...");
            return factory = await Task.Run(async () =>
            {
                var f = WhisperFactory.FromPath(path);
                // the first run on the GPU compiles its shaders (about 15 s); do it now, not on the first phrase
                using var warm = f.CreateBuilder().WithLanguage("en").Build();
                await foreach (var _ in warm.ProcessAsync(new float[SpeechSegmenter.Rate])) { }
                return f;
            });
        }
        finally { loading.Release(); }
    }
#endif

    static void Start(Button b)
    {
#if WINDOWS
        if (GetTarget(b) is not { } box) return;
        var mine = ++session;
        active = b;
        savedHint = box.PlaceholderText;
        lastSound = DateTime.UtcNow;
        heardSound = false;
        Show(b, true);
        _ = Task.Run(async () =>
        {
            void SayUi(string t) => Dispatcher.UIThread.Post(() => { if (mine == session) Say(b, t); });
            try
            {
                var f = await LoadAsync(SayUi);
                if (AfterLoad is { } hook) await hook();
                if (mine != session) return;
                await Run(b, box, f, mine, SayUi);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (mine != session) return;
                    Say(b, "Couldn't start dictation: " + ex.Message);
                    Stop(keepHint: true);
                });
            }
        });
#endif
    }

#if WINDOWS
    /// <summary>One open microphone: its phrase cutter, the phrases waiting for the model, and the way to close it
    /// that hands over a phrase still being spoken.</summary>
    sealed class Listening(ISampleSource source)
    {
        public readonly ISampleSource Source = source;
        public readonly SpeechSegmenter Seg = new();
        public readonly System.Threading.Channels.Channel<float[]> Work = System.Threading.Channels.Channel.CreateUnbounded<float[]>();
        bool closed;

        public void Feed(float[] x)
        {
            lock (Seg)
            {
                if (closed) return;
                foreach (var p in Seg.Feed(x)) Work.Writer.TryWrite(p);
            }
        }

        /// <summary>Stops the mic, queues the phrase in progress (if long enough to be speech) and ends the queue.</summary>
        public void Finish()
        {
            lock (Seg)
            {
                if (closed) return;
                closed = true;
                try { Source.Stop(); } catch { }
                if (Seg.Flush() is { } tail) Work.Writer.TryWrite(tail);
                Work.Writer.TryComplete();
            }
        }
    }

    static async Task Run(Button b, TextBox box, WhisperFactory f, int mine, Action<string> say)
    {
        using var processor = f.CreateBuilder().WithLanguage("en")
            .WithPrompt("Darg, Fettle, Claude, Avalonia, agent, card, board, commit, branch.").Build();
        using var src = (SourceFactory ?? (() => new MicSource()))();
        var run = new Listening(src);
        src.Samples += x =>
        {
            if (mine != session) return;
            run.Feed(x);
            double level; bool hearing;
            lock (run.Seg) { level = run.Seg.Level * 100; hearing = run.Seg.Hearing; }
            Dispatcher.UIThread.Post(() =>
            {
                if (mine != session) return;
                b.Content = hearing ? "🔊" : level >= 1 ? "🔉" : "⏺";
                if (level >= 1) { lastSound = DateTime.UtcNow; heardSound = true; }
                else if (!heardSound && DateTime.UtcNow - lastSound > QuietWarning)
                {
                    lastSound = DateTime.UtcNow;
                    Say(b, "No sound from the microphone. In Windows Settings > Sound > Input pick your mic and watch its level bar while you talk.");
                }
            });
        };
        src.Stopped += run.Finish; // also when the device ends by itself: hand over what was heard
        current = run;
        // the clock for the "No sound" warning starts when the mic opens, not when the button was pressed:
        // loading the model can take longer than the warning's wait
        lastSound = DateTime.UtcNow;
        src.Start();
        say("Listening: speak, then pause. Each phrase appears when you stop.");
        await foreach (var phrase in run.Work.Reader.ReadAllAsync())
        {
            say("Transcribing...");
            var sb = new System.Text.StringBuilder();
            await foreach (var s in processor.ProcessAsync(phrase)) sb.Append(s.Text);
            var text = SpeechText.Clean(sb.ToString());
            if (text.Length == 0) { say("Listening (heard no words in that)."); continue; }
            // typed even when the mic was stopped meanwhile: stopping mid-phrase still delivers that phrase
            Dispatcher.UIThread.Post(() => { Say(b, "Typed: " + text); Insert(box, text); });
        }
    }
#endif

    public static void Stop() => Stop(false);

    static void Stop(bool keepHint)
    {
        session++;
#if WINDOWS
        current?.Finish();
        current = null;
#endif
        if (active is { } b)
        {
            Show(b, false);
            if (!keepHint && GetTarget(b) is { } bx) bx.PlaceholderText = savedHint;
        }
        active = null;
    }

    static void Show(Button b, bool on)
    {
        b.Content = on ? "⏺" : "🎙";
        ToolTip.SetTip(b, on ? "Listening: what you say is typed into the box. Click to stop." : "Dictate: speak instead of typing (local Whisper model).");
    }

    /// <summary>Adds a recognised phrase at the caret (or the end), with a space between it and what's there.</summary>
    internal static void Insert(TextBox box, string phrase)
    {
        var text = box.Text ?? "";
        var at = Math.Clamp(box.CaretIndex, 0, text.Length);
        var before = text[..at];
        var lead = before.Length == 0 || char.IsWhiteSpace(before[^1]) ? "" : " ";
        var add = lead + phrase;
        box.Text = before + add + text[at..];
        box.CaretIndex = at + add.Length;
    }
}

/// <summary>Where dictation's samples (16 kHz mono, -1..1) come from: the microphone, or a test's wave file.</summary>
public interface ISampleSource : IDisposable
{
    event Action<float[]>? Samples;
    /// <summary>Raised when recording has ended.</summary>
    event Action? Stopped;
    void Start();
    void Stop();
}

#if WINDOWS
sealed class MicSource : ISampleSource
{
    readonly WaveInEvent mic = new() { WaveFormat = new WaveFormat(SpeechSegmenter.Rate, 16, 1), BufferMilliseconds = 100 };
    public event Action<float[]>? Samples;
    public event Action? Stopped;

    public MicSource()
    {
        mic.DataAvailable += (_, a) =>
        {
            var n = a.BytesRecorded / 2;
            var x = new float[n];
            for (var i = 0; i < n; i++) x[i] = BitConverter.ToInt16(a.Buffer, i * 2) / 32768f;
            Samples?.Invoke(x);
        };
        mic.RecordingStopped += (_, _) => Stopped?.Invoke();
    }

    public void Start() => mic.StartRecording();
    public void Stop() => mic.StopRecording();
    public void Dispose() => mic.Dispose();
}
#endif
