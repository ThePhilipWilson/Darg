namespace Darg.Mobile;

/// <summary>Speech to text on the phone (AND-0007): typing is slow there, so Fettle's chat has a mic. Darg.Mobile is
/// plain net10.0, so the host provides it (Darg.Android: the system's speech recogniser) by setting <see cref="Voice.Input"/>.</summary>
public interface IVoiceInput
{
    /// <summary>Whether this phone can take dictation at all (a recogniser is installed).</summary>
    bool Available { get; }

    /// <summary>Listens and gives back what was said, or null when nothing was (cancelled, no match, no recogniser).</summary>
    Task<string?> Listen();
}

public static class Voice
{
    /// <summary>The host's speech recogniser; null where there is none (the desktop tests, unless they set one).</summary>
    public static IVoiceInput? Input { get; set; }
}
