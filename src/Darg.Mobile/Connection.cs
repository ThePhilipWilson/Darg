using System.Text.Json;
using System.Text.RegularExpressions;

namespace Darg.Mobile;

/// <summary>Where the desktop's Darg API is and the token it wants: the phone's one setting (AND-0003).
/// <see cref="Address"/> is scheme, host and port with no path (<c>http://localhost:47817</c>, which over USB
/// is the PC through <c>adb reverse</c>).</summary>
public sealed record Connection(string Address, string Token)
{
    /// <summary>The desktop's usual port (ArtifactServer's), reached over USB with adb reverse.</summary>
    public const string DefaultAddress = "http://localhost:47817";

    public bool IsComplete => Address.Length > 0 && Token.Length > 0;

    /// <summary>The API's root, token included: POST to this plus /&lt;command&gt; runs one.</summary>
    public string ApiBase => $"{Address}/api/{Uri.EscapeDataString(Token)}";

    /// <summary>Reads what was typed or pasted into the address box: a whole API address as the desktop gives it
    /// (<c>http://host:47817/api/&lt;token&gt;</c>, which fills in the token too), or a host with or without a
    /// scheme and port. Null when it isn't an address at all.</summary>
    public static (string Address, string? Token)? ParseAddress(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!Regex.IsMatch(text, "^[a-zA-Z][a-zA-Z0-9+.-]*://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            return null;
        var port = uri.IsDefaultPort && !Regex.IsMatch(text, @"://[^/]+:\d+") ? 47817 : uri.Port;
        var address = $"{uri.Scheme}://{(uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.DnsSafeHost}]" : uri.Host)}:{port}";
        var m = Regex.Match(uri.AbsolutePath, "^/api/([^/]+)");
        return (address, m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null);
    }
}

/// <summary>Keeps the <see cref="Connection"/> in a small JSON file (on Android, the app's private files folder;
/// tools/android.ps1 writes it there with run-as so the token needn't be typed on a phone).</summary>
public sealed class ConnectionStore(string file)
{
    public string File { get; } = file;

    /// <summary>The fallback when the host names no file (Android names its files folder: App.ConnectionFile).</summary>
    public static ConnectionStore Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "connection.json"));

    public Connection? Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return null;
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(File));
            var address = doc.RootElement.TryGetProperty("address", out var a) ? a.GetString() ?? "" : "";
            var token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";
            return new Connection(address.TrimEnd('/'), token.Trim());
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Save(Connection c)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new { address = c.Address, token = c.Token }));
    }
}
