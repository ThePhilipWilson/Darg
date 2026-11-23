using Android.Content;
using Android.Gms.Extensions;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace Darg.Android;

/// <summary>Reads the pairing QR with Google's code scanner (AND-0011): a full-screen scanner Play services shows itself,
/// so the app asks for no camera permission and carries no camera or decoding code.</summary>
static class Scanner
{
    public static async Task<string?> Scan(Context context)
    {
        var options = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatQrCode)
            .Build();
        var client = GmsBarcodeScanning.GetClient(context, options);
        try
        {
            var barcode = (Barcode?)await client.StartScan();
            return barcode?.RawValue;
        }
        catch (Exception e) when (e.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase) || e is OperationCanceledException)
        {
            return null; // backed out of the scanner
        }
    }
}
