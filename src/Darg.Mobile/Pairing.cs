using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Web;

namespace Darg.Mobile;

/// <summary>What the desktop's pairing QR holds (AND-0011; docs/REMOTE.md):
/// <c>darg://pair?v=1&amp;name=&amp;hosts=a,b&amp;port=47818&amp;code=ABCDEFGH&amp;fp=&lt;sha256 hex&gt;</c>. The phone tries the hosts
/// in order and trusts the one certificate whose SHA-256 is <see cref="Fingerprint"/>.</summary>
public sealed record PairingLink(string Name, IReadOnlyList<string> Hosts, int Port, string Code, string Fingerprint)
{
    /// <summary>Reads the link as scanned or pasted. Null when it isn't one, or lacks what pairing needs: a host, the code,
    /// or a 64-digit fingerprint (without which the certificate couldn't be pinned).</summary>
    public static PairingLink? Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (!uri.Scheme.Equals("darg", StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals("pair", StringComparison.OrdinalIgnoreCase)) return null;
        var q = HttpUtility.ParseQueryString(uri.Query);
        var hosts = (q["hosts"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var code = new string((q["code"] ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        var fp = new string((q["fp"] ?? "").Where(char.IsAsciiHexDigit).ToArray()).ToLowerInvariant();
        if (hosts.Length == 0 || code.Length == 0 || fp.Length != 64 || !int.TryParse(q["port"], out var port) || port is < 1 or > 65535) return null;
        return new PairingLink(q["name"] is { Length: > 0 } n ? n : "Darg", hosts, port, code, fp);
    }
}

/// <summary>The certificate check of the phone route: the desktop's self-signed certificate is trusted when its SHA-256 is the
/// one pinned and by no other test, so a different certificate (someone on the Wi-Fi standing in for the PC) is refused
/// whoever signed it, and the desktop's own is accepted with no authority or domain behind it.</summary>
public static class PinnedTls
{
    public static string Fingerprint(System.Security.Cryptography.X509Certificates.X509Certificate cert) =>
        Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).ToLowerInvariant();

    /// <summary>A handler that talks TLS only to the certificate with this fingerprint. <paramref name="mismatch"/> is
    /// called when the server showed another one, so the caller can say why the connection failed.</summary>
    public static HttpMessageHandler Handler(string fingerprint, Action<string>? mismatch = null) => new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(8),
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
            {
                if (cert is null) return false;
                var seen = Fingerprint(cert);
                if (string.Equals(seen, fingerprint, StringComparison.OrdinalIgnoreCase)) return true;
                mismatch?.Invoke(seen);
                return false;
            },
        },
    };

    /// <summary>True when a failed call died on the certificate check, however deep the framework buried it.</summary>
    public static bool IsCertificateFailure(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException) if (x is AuthenticationException) return true;
        return false;
    }
}

/// <summary>Trades a pairing code for a token (AND-0011): <c>POST /pair {code, name}</c> to each host of the link in turn,
/// over TLS pinned to the link's fingerprint, and gives back the <see cref="Connection"/> to keep.</summary>
public static class Pairer
{
    public static async Task<Connection> Pair(PairingLink link, string deviceName, CancellationToken ct = default)
    {
        string? last = null;
        var certProblem = false;
        foreach (var host in link.Hosts)
        {
            var shown = host.Contains(':') ? $"[{host}]" : host;
            var address = $"https://{shown}:{link.Port}";
            using var http = new HttpClient(PinnedTls.Handler(link.Fingerprint, _ => certProblem = true), true) { Timeout = TimeSpan.FromSeconds(12) };
            try
            {
                using var res = await http.PostAsync($"{address}/pair", DargClient.Body(new { code = link.Code, name = deviceName }), ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                if (res.StatusCode is HttpStatusCode.Forbidden)
                    throw new DargClientException(body.Length > 0 ? body : "Darg refused the pairing code.");
                if (!res.IsSuccessStatusCode) { last = $"{address} answered {(int)res.StatusCode}."; continue; }
                var json = JsonNode.Parse(body);
                var token = json?["token"]?.GetValue<string>();
                if (string.IsNullOrEmpty(token)) { last = $"{address} answered, but with no token."; continue; }
                return new Connection(address, token, link.Fingerprint, json?["pc"]?.GetValue<string>() ?? link.Name);
            }
            catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                if (certProblem || PinnedTls.IsCertificateFailure(e)) certProblem = true;
                last = $"{address} didn't answer ({e.GetType().Name}: {e.InnerException?.Message ?? e.Message}).";
            }
        }
        throw new DargClientException(certProblem
            ? "That isn't the PC in the QR: its certificate is not the one the QR names, so nothing was sent and nothing was saved. Scan the QR from your own Darg."
            : $"Couldn't reach Darg on {string.Join(" or ", link.Hosts)} (port {link.Port}). {last} Is the phone on the same Wi-Fi (or Tailscale), Darg running, and the firewall open for it? Pairing also runs out after 10 minutes: open a new one.");
    }
}
