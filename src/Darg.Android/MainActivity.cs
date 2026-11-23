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
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity
{
    AndroidVoice? _voice;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Voice.Input = _voice = new AndroidVoice(this); // Fettle's mic (AND-0007)
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == AndroidVoice.RequestCode) _voice?.Result(resultCode, data);
    }
}
