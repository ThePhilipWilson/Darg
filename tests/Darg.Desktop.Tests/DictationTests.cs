using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Darg.Core;
using Darg.Desktop.Views;

namespace Darg.Desktop.Tests;

/// <summary>Voice in (WOR-0118): the real Dictation code, the real Whisper model, a wave file standing in for the
/// microphone (`Dictation.SourceFactory`). The model is the one the app downloads to %APPDATA%/Darg/models; without it
/// these tests can't run, and say so.</summary>
public class DictationTests : IDisposable
{
    public void Dispose()
    {
        Dictation.Stop();
        Dictation.SourceFactory = null;
        Dictation.AfterLoad = null;
        Dictation.QuietWarning = TimeSpan.FromSeconds(6);
    }

    /// <summary>Plays the first <paramref name="seconds"/> of the fixture (16 kHz 16-bit mono) in 100 ms chunks, as
    /// the microphone would, only faster; then stays open and quiet.</summary>
    sealed class WaveSource(double seconds, bool silent = false) : ISampleSource
    {
        public event Action<float[]>? Samples;
        public event Action? Stopped;
        public readonly TaskCompletionSource Played = new();
        volatile bool stop;

        public void Start() => Task.Run(async () =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "speech.wav"));
            var data = bytes.AsMemory(44, Math.Min(bytes.Length - 44, (int)(seconds * 32000)));
            for (var at = 0; at + 3200 <= data.Length && !stop; at += 3200)
            {
                var x = new float[1600];
                for (var i = 0; i < x.Length; i++)
                    x[i] = silent ? 0.0003f * (i % 7 - 3) : BitConverter.ToInt16(data.Span.Slice(at + i * 2, 2)) / 32768f;
                Samples?.Invoke(x);
                await Task.Delay(10);
            }
            Played.TrySetResult();
        });

        public void Stop() { stop = true; Stopped?.Invoke(); }
        public void Dispose() => stop = true;
    }

    static string Log => File.Exists(Path.Combine(InstanceConfig.Folder(), "dictation.log"))
        ? File.ReadAllText(Path.Combine(InstanceConfig.Folder(), "dictation.log")) : "";

    static void NeedModel() => Assert.SkipUnless(File.Exists(Dictation.ModelPath),
        $"the speech model isn't at {Dictation.ModelPath}; click the mic once in Darg to download it");

    static (Button Mic, TextBox Box) Fettle(DargApp app)
    {
        var mic = app.Window.GetVisualDescendants().OfType<Button>().First(b =>
            Dictation.GetTarget(b) is { Name: "MascotBox" });
        return (mic, Dictation.GetTarget(mic)!);
    }

    static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task A_spoken_phrase_lands_in_the_box_and_the_slow_model_load_doesnt_trip_the_no_sound_warning()
    {
        NeedModel();
        using var app = DargApp.Make();
        await app.Start();
        app.Main.Mascot.Open = true;
        await DargApp.Settle();
        var (mic, box) = Fettle(app);
        var source = new WaveSource(7);
        Dictation.SourceFactory = () => source;
        // the model "takes" 2 s to load, the warning waits 1.5 s: it must count from when the mic opens
        Dictation.QuietWarning = TimeSpan.FromSeconds(1.5);
        Dictation.AfterLoad = () => Task.Delay(2000);

        Click(mic);
        await app.Until(() => (box.Text ?? "").Contains("board", StringComparison.OrdinalIgnoreCase), "the phrase typed into the box", 90);
        await source.Played.Task;

        Assert.DoesNotContain("No sound", Log);
        Assert.Contains("Listening:", Log);
        Assert.Contains("Typed:", Log);
    }

    [AvaloniaFact]
    public async Task Silence_types_nothing()
    {
        NeedModel();
        using var app = DargApp.Make();
        await app.Start();
        app.Main.Mascot.Open = true;
        await DargApp.Settle();
        var (mic, box) = Fettle(app);
        var source = new WaveSource(7, silent: true);
        Dictation.SourceFactory = () => source;

        Click(mic);
        await app.Until(() => Log.Contains("Listening:"), "dictation listening", 90);
        await source.Played.Task;
        Click(mic); // stop: flushes whatever was being heard, which is nothing
        await DargApp.Settle(1500);

        Assert.Equal("", box.Text ?? "");
        Assert.DoesNotContain("Typed:", Log);
    }

    [AvaloniaFact]
    public async Task Stopping_the_mic_mid_phrase_types_that_phrase()
    {
        NeedModel();
        using var app = DargApp.Make();
        await app.Start();
        app.Main.Mascot.Open = true;
        await DargApp.Settle();
        var (mic, box) = Fettle(app);
        // 4.4 s: the voice runs from 1.2 s to 5.4 s, so this ends in the middle of the sentence, no pause after it
        var source = new WaveSource(4.4);
        Dictation.SourceFactory = () => source;

        Click(mic);
        await app.Until(() => Log.Contains("Listening:"), "dictation listening", 90);
        await source.Played.Task;
        await DargApp.Settle(300);
        Assert.Equal("", box.Text ?? ""); // no pause yet, so nothing has been cut
        Click(mic);
        await app.Until(() => (box.Text ?? "").Length > 0, "the unfinished phrase typed on stop", 30);

        Assert.Contains("open", box.Text!, StringComparison.OrdinalIgnoreCase);
    }
}
