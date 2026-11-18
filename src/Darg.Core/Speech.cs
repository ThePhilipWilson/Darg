using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>Cuts a stream of 16 kHz mono samples into spoken phrases for a speech-to-text model (WOR-0118): a phrase
/// starts when the level rises above the room's noise and ends after a pause. Pure, so it is tested without a mic.</summary>
public sealed class SpeechSegmenter
{
    public const int Rate = 16000;
    const int Frame = Rate / 50;             // 20 ms
    const int PreRollFrames = 15;            // 300 ms kept from before the level rose, so a first word isn't clipped
    readonly int endFrames;                  // silence that ends a phrase
    readonly int maxFrames;                  // a phrase is cut here however long it runs
    const int MinVoicedFrames = 10;          // under 200 ms of voice is a click or cough, not a phrase

    readonly List<float> carry = new();
    readonly Queue<float[]> preRoll = new();
    readonly List<float[]> phrase = new();
    double floor = 0.004;
    int voiced, silent;

    public SpeechSegmenter(int pauseMs = 700, int maxSeconds = 25)
    {
        endFrames = Math.Max(1, pauseMs / 20);
        maxFrames = maxSeconds * 50;
    }

    /// <summary>Level of the latest frame, 0 to 1 (RMS).</summary>
    public double Level { get; private set; }
    /// <summary>True while a phrase is being heard.</summary>
    public bool Hearing => phrase.Count > 0;
    double Threshold => Math.Max(0.012, floor * 4);

    /// <summary>Adds samples (-1..1); returns each phrase completed by them.</summary>
    public List<float[]> Feed(ReadOnlySpan<float> samples)
    {
        var done = new List<float[]>();
        foreach (var s in samples) carry.Add(s);
        var at = 0;
        while (carry.Count - at >= Frame)
        {
            var f = carry.GetRange(at, Frame).ToArray();
            at += Frame;
            OnFrame(f, done);
        }
        carry.RemoveRange(0, at);
        return done;
    }

    /// <summary>The phrase in progress, if long enough to be speech, when the mic is stopped.</summary>
    public float[]? Flush()
    {
        var p = Hearing && voiced >= MinVoicedFrames ? Take() : null;
        Reset();
        return p;
    }

    void Reset() { carry.Clear(); preRoll.Clear(); phrase.Clear(); voiced = silent = 0; }

    void OnFrame(float[] f, List<float[]> done)
    {
        double sum = 0;
        foreach (var x in f) sum += x * x;
        var rms = Math.Sqrt(sum / f.Length);
        Level = rms;
        var loud = rms > Threshold;
        if (!Hearing)
        {
            if (!loud)
            {
                floor = Math.Min(0.02, floor * 0.95 + rms * 0.05);
                preRoll.Enqueue(f);
                while (preRoll.Count > PreRollFrames) preRoll.Dequeue();
                return;
            }
            phrase.AddRange(preRoll);
            preRoll.Clear();
            voiced = 0; silent = 0;
        }
        phrase.Add(f);
        if (loud) { voiced++; silent = 0; } else silent++;
        if (silent >= endFrames || phrase.Count >= maxFrames)
        {
            var p = voiced >= MinVoicedFrames ? Take() : null;
            phrase.Clear(); voiced = silent = 0;
            if (p != null) done.Add(p);
        }
    }

    float[] Take()
    {
        // drop most of the trailing silence; the model needs a little, not 700 ms of it
        var keep = Math.Max(1, phrase.Count - Math.Max(0, silent - 8));
        var all = new float[keep * Frame];
        for (var i = 0; i < keep; i++) phrase[i].CopyTo(all, i * Frame);
        return all;
    }
}

/// <summary>Tidies what a speech model returns. Whisper-style models answer silence and noise with text that was never
/// said; the class: bracketed or starred sound tags ("[BLANK_AUDIO]", "(music)", "*coughs*"), sign-off phrases learned
/// from subtitles ("Thanks for watching."), and a word looped many times. Each gives an empty string.</summary>
public static partial class SpeechText
{
    static readonly string[] Phantoms =
    {
        "thank you", "thanks for watching", "thank you for watching", "you", "bye", "subtitles by the amara.org community",
    };

    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = Tags().Replace(raw, " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0) return "";
        var bare = Regex.Replace(t.ToLowerInvariant(), @"[^\p{L}\p{N} ']", "").Trim();
        if (Phantoms.Contains(bare)) return "";
        if (Looped(bare)) return "";
        return t;
    }

    /// <summary>One word or short phrase repeated over and over ("the the the the the the").</summary>
    static bool Looped(string bare)
    {
        var w = bare.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length < 6) return false;
        return w.Distinct().Count() <= Math.Max(1, w.Length / 6);
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*|♪[^♪]*♪?")]
    private static partial Regex Tags();
}
