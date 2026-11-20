using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Darg.Mobile;

namespace Darg.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    protected MainApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // the connection lives in the app's private files folder, where tools/android.ps1 writes it (run-as) to pair over USB
        App.ConnectionFile = Path.Combine(FilesDir!.AbsolutePath, "connection.json");
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
