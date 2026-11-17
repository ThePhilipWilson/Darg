using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
#if WINDOWS
using System.Globalization;
using System.Speech.Recognition;
#endif

namespace Darg.Desktop.Views;

/// <summary>Voice in (WOR-0118): a button with <c>views:Dictation.Target="{Binding #Box}"</c> toggles dictation into
/// that TextBox. Uses Windows' own recogniser (System.Speech, offline, nothing to download), so it only exists on
/// Windows; elsewhere the button is hidden. What's heard is added at the caret as it is recognised; nothing is sent,
/// the person still presses Send. One microphone at a time: starting one button stops the other.</summary>
public static class Dictation
{
    public static readonly AttachedProperty<TextBox?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Button, TextBox?>("Target", typeof(Dictation));

    public static TextBox? GetTarget(Button b) => b.GetValue(TargetProperty);
    public static void SetTarget(Button b, TextBox? v) => b.SetValue(TargetProperty, v);

    /// <summary>True where dictation can work at all (Windows with a speech recogniser installed).</summary>
    public static bool Available { get; } = Probe();

    static bool Probe()
    {
#if WINDOWS
        try { return SpeechRecognitionEngine.InstalledRecognizers().Count > 0; } catch { return false; }
#else
        return false;
#endif
    }

    static Button? active;
#if WINDOWS
    static SpeechRecognitionEngine? engine;
#endif

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

    static void Start(Button b)
    {
#if WINDOWS
        if (GetTarget(b) is not { } box) return;
        try
        {
            var all = SpeechRecognitionEngine.InstalledRecognizers();
            var ui = CultureInfo.CurrentUICulture;
            var info = all.FirstOrDefault(r => r.Culture.Name == ui.Name)
                ?? all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == ui.TwoLetterISOLanguageName)
                ?? all.First();
            engine = new SpeechRecognitionEngine(info);
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += (_, r) =>
            {
                var text = r.Result.Text;
                if (r.Result.Confidence < 0.3 || string.IsNullOrWhiteSpace(text)) return;
                Dispatcher.UIThread.Post(() => Insert(box, text));
            };
            engine.RecognizeAsync(RecognizeMode.Multiple);
            active = b;
            Show(b, true);
        }
        catch (Exception ex)
        {
            Stop();
            ToolTip.SetTip(b, "Couldn't start the microphone: " + ex.Message);
        }
#endif
    }

    public static void Stop()
    {
#if WINDOWS
        try { engine?.RecognizeAsyncCancel(); engine?.Dispose(); } catch { }
        engine = null;
#endif
        if (active is { } b) Show(b, false);
        active = null;
    }

    static void Show(Button b, bool on)
    {
        b.Content = on ? "⏺" : "🎙";
        ToolTip.SetTip(b, on ? "Listening: what you say is typed into the box. Click to stop." : "Dictate: speak instead of typing (Windows speech recognition).");
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
