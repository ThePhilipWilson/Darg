using Android.App;
using Android.Content;
using Android.Speech;
using Darg.Mobile;

namespace Darg.Android;

/// <summary>Dictation for Fettle's mic (AND-0007): the system's speech recogniser, started as an activity for its result
/// (Google's listening sheet on most phones). Asked this way the recogniser holds the microphone, so the app needs no
/// RECORD_AUDIO permission of its own. The activity hands the result back through <see cref="Result"/>.</summary>
sealed class AndroidVoice(Activity activity) : IVoiceInput
{
    public const int RequestCode = 7007;

    TaskCompletionSource<string?>? _pending;

    static Intent Recognise()
    {
        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraPrompt, "Say it to Fettle");
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
        return intent;
    }

    // seen through the manifest's <queries> entry for this intent (package visibility, Android 11 on)
    public bool Available => activity.PackageManager?.QueryIntentActivities(Recognise(), 0)?.Count > 0;

    public Task<string?> Listen()
    {
        _pending?.TrySetResult(null);
        var pending = new TaskCompletionSource<string?>();
        _pending = pending;
        try { activity.StartActivityForResult(Recognise(), RequestCode); }
        catch (ActivityNotFoundException) { pending.TrySetResult(null); }
        return pending.Task;
    }

    public void Result(Result code, Intent? data)
    {
        var said = code == global::Android.App.Result.Ok ? data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault() : null;
        _pending?.TrySetResult(said);
        _pending = null;
    }
}
