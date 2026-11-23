using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Darg.Mobile;

namespace Darg.Android;

[Activity(
    Label = "Darg",
    Name = "uk.co.philipwilson.darg.MainActivity", // a fixed name, so tools/android.ps1 can start it with am start
    Theme = "@style/DargTheme",
    Icon = "@mipmap/ic_launcher",
    RoundIcon = "@mipmap/ic_launcher_round",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask, // a pairing link opened from the camera reaches the running app, not a second one
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
// The pairing QR is a darg://pair link (AND-0011): the phone's own camera offers to open it here, with no camera permission asked.
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "darg", DataHost = "pair")]
public class MainActivity : AvaloniaMainActivity
{
    AndroidVoice? _voice;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // the in-app scanner is Google's (Play services): no camera permission and no camera code of ours
        App.Scanner = () => Scanner.Scan(this);
        App.DeviceName = Build.Model is { Length: > 0 } m ? m : "Android phone";
        base.OnCreate(savedInstanceState);
        Voice.Input = _voice = new AndroidVoice(this); // Fettle's mic (AND-0007)
        HandleLink(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleLink(intent);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == AndroidVoice.RequestCode) _voice?.Result(resultCode, data);
    }

    static void HandleLink(Intent? intent)
    {
        if (intent?.Action == Intent.ActionView && intent.DataString is { } link && link.StartsWith("darg://", StringComparison.OrdinalIgnoreCase))
            App.OpenLink(link);
    }
}
