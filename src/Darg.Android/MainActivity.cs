using Android.App;
using Android.Content.PM;
using Avalonia.Android;

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
}
