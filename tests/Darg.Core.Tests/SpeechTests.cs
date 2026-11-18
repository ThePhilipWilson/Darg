using Darg.Core;

namespace Darg.Core.Tests;

public sealed class SpeechTests
{
    // 1 s of a 200 Hz tone at the given amplitude, or silence (amp 0), as 16 kHz samples
    static float[] Tone(double seconds, float amp) =>
        Enumerable.Range(0, (int)(seconds * SpeechSegmenter.Rate)).Select(i => amp * (float)Math.Sin(2 * Math.PI * 200 * i / SpeechSegmenter.Rate)).ToArray();

    static List<float[]> Run(params float[][] parts)
    {
        var seg = new SpeechSegmenter();
        var all = new List<float[]>();
        foreach (var p in parts) all.AddRange(seg.Feed(p));
        if (seg.Flush() is { } last) all.Add(last);
        return all;
    }

    [Fact]
    public void A_phrase_ends_at_a_pause_and_two_phrases_are_two()
    {
        var phrases = Run(Tone(0.5, 0), Tone(1, 0.3f), Tone(1, 0), Tone(1, 0.3f), Tone(1, 0));
        Assert.Equal(2, phrases.Count);
        Assert.All(phrases, p => Assert.InRange(p.Length / (double)SpeechSegmenter.Rate, 1.0, 1.8));
    }

    [Fact]
    public void Room_noise_and_a_click_make_no_phrase()
    {
        Assert.Empty(Run(Tone(2, 0.002f), Tone(0.06, 0.4f), Tone(2, 0.002f)));
    }

    [Fact]
    public void The_first_word_keeps_its_lead_in_and_a_phrase_is_cut_at_the_longest()
    {
        var p = Run(Tone(0.5, 0), Tone(1, 0.3f), Tone(1, 0))[0];
        Assert.True(p.Length > 1.1 * SpeechSegmenter.Rate, "lead-in from before the voice is kept");
        var long_ = new SpeechSegmenter(maxSeconds: 3).Feed(Tone(7, 0.3f));
        Assert.Equal(2, long_.Count);
    }

    [Fact]
    public void Stopping_hands_over_a_phrase_still_being_spoken()
    {
        var seg = new SpeechSegmenter();
        Assert.Empty(seg.Feed(Tone(1, 0.3f)));
        Assert.NotNull(seg.Flush());
    }

    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData(" (music) ")]
    [InlineData("*coughs*")]
    [InlineData("♪ la la ♪")]
    [InlineData("Thank you.")]
    [InlineData("Thanks for watching!")]
    [InlineData("the the the the the the the the")]
    [InlineData("")]
    public void What_a_model_makes_up_from_silence_is_dropped(string raw) => Assert.Equal("", SpeechText.Clean(raw));

    [Fact]
    public void Real_words_pass_and_tags_inside_them_go()
    {
        Assert.Equal("Open the board and start a new card.", SpeechText.Clean(" Open the board and start a new card."));
        Assert.Equal("Open the board.", SpeechText.Clean("Open [BLANK_AUDIO] the board."));
        Assert.Equal("Thank you for the fix on the board.", SpeechText.Clean("Thank you for the fix on the board."));
    }
}
