using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The one phone-access server for this run of the app (AND-0002, <see cref="RemoteServer"/>). Nothing listens
/// until a phone is paired: the app starts it at launch when one is, and Pair starts it otherwise. An instance with
/// "remote": false in instance.json never starts it.</summary>
public static class RemoteHost
{
    static RemoteServer? _server;

    /// <summary>The API it serves (the artifact server's); set by the app at launch.</summary>
    public static DargApi? Api { get; set; }
    /// <summary>False when the instance leaves phone access out.</summary>
    public static bool Allowed { get; set; } = true;
    public static RemoteServer? Server => _server;
    /// <summary>Why it couldn't start, if it couldn't.</summary>
    public static string? Error { get; private set; }
    /// <summary>Raised (on any thread) when it starts or stops, a pairing starts or ends, or a phone pairs.</summary>
    public static event Action? Changed;

    static int Port() => int.TryParse(Environment.GetEnvironmentVariable("DARG_REMOTE_PORT"), out var p) ? p : RemoteAccess.PreferredPort;

    public static RemoteDevices Devices => _server?.Devices ?? new RemoteDevices(InstanceConfig.Folder());

    /// <summary>The server, started if it isn't (null when not allowed or it failed: see <see cref="Error"/>).</summary>
    public static RemoteServer? Ensure()
    {
        if (_server is not null) return _server;
        if (Api is null || !Allowed) return null;
        try
        {
            _server = RemoteServer.Start(Api, port: Port());
            _server.Pairing.Changed += () => Changed?.Invoke();
            _server.Paired += _ => Changed?.Invoke();
            Error = null;
        }
        catch (Exception e) { Error = e.Message; }
        Changed?.Invoke();
        return _server;
    }

    /// <summary>At launch: listen if this instance has a paired phone.</summary>
    public static void StartIfPaired()
    {
        if (Allowed && Devices.All().Count > 0) Ensure();
    }

    /// <summary>Stop listening once no phone is paired and no pairing is open.</summary>
    public static void StopIfIdle()
    {
        if (_server is null || _server.Pairing.Code is not null || _server.Devices.All().Count > 0) return;
        _server.Dispose();
        _server = null;
        Changed?.Invoke();
    }

    /// <summary>Unpair by id or name; what was removed.</summary>
    public static IReadOnlyList<RemoteDevice> Revoke(string idOrName)
    {
        var gone = Devices.Revoke(idOrName);
        Changed?.Invoke();
        StopIfIdle();
        return gone;
    }

    /// <summary>A code as shown: ABCD-EFGH.</summary>
    public static string Shown(string code) => code.Length == 8 ? $"{code[..4]}-{code[4..]}" : code;
}

/// <summary>Settings' Phone part (AND-0002): pair a phone by QR, see the paired ones and revoke them.</summary>
public partial class PhoneViewModel : ObservableObject
{
    readonly DispatcherTimer _tick;

    public ObservableCollection<PhoneDevice> Devices { get; } = [];
    [ObservableProperty] public partial string State { get; set; } = "";
    /// <summary>The pairing QR as geometry (one unit per module), or null when no pairing is open.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Pairing))] public partial Geometry? Qr { get; set; }
    [ObservableProperty] public partial string Code { get; set; } = "";
    [ObservableProperty] public partial string Countdown { get; set; } = "";
    /// <summary>What the QR holds, for a phone that can't scan.</summary>
    [ObservableProperty] public partial string PairUri { get; set; } = "";
    public bool Pairing => Qr is not null;
    public bool Allowed => RemoteHost.Allowed;
    public bool NoDevices => Devices.Count == 0;

    public PhoneViewModel()
    {
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        RemoteHost.Changed += OnChanged;
        Refresh();
    }

    void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    /// <summary>Settings closed: stop listening for changes.</summary>
    public void Detach()
    {
        RemoteHost.Changed -= OnChanged;
        _tick.Stop();
    }

    public void Refresh()
    {
        var server = RemoteHost.Server;
        State = !RemoteHost.Allowed ? "Phone access is turned off for this instance (\"remote\": false in instance.json)."
            : RemoteHost.Error is { } e ? $"Couldn't start: {e}"
            : server is null ? "Nothing listens until a phone is paired."
            : $"Listening on port {server.Port}, over TLS, at {string.Join(", ", RemoteAccess.Hosts().DefaultIfEmpty("no network address"))}. Off the home network, it's reachable over Tailscale once the PC and the phone are both on it.";
        ListSync(RemoteHost.Devices.All());
        if (server?.Pairing.Code is { } code)
        {
            if (Code != RemoteHost.Shown(code))
            {
                PairUri = server.PairUri() ?? "";
                Qr = Geometry.Parse(QrCode.Encode(PairUri).PathData(0));
                Code = RemoteHost.Shown(code);
            }
            _tick.Start();
            Tick();
        }
        else
        {
            Qr = null; Code = ""; PairUri = ""; Countdown = "";
            _tick.Stop();
        }
    }

    void Tick()
    {
        var server = RemoteHost.Server;
        if (server?.Pairing.Code is null) { if (Qr is not null) Refresh(); return; }
        var left = server.Pairing.Expires - DateTime.Now;
        Countdown = $"Good for {(int)left.TotalMinutes}:{left.Seconds:D2}, once.";
    }

    void ListSync(IReadOnlyList<RemoteDevice> devices)
    {
        Devices.Clear();
        foreach (var d in devices.OrderBy(d => d.Paired)) Devices.Add(new PhoneDevice(d, new RelayCommand(() => RemoteHost.Revoke(d.Id))));
        OnPropertyChanged(nameof(NoDevices));
    }

    /// <summary>Open a pairing: start listening if need be and show the QR.</summary>
    [RelayCommand]
    public void Pair()
    {
        if (RemoteHost.Ensure() is { } server) server.Pairing.Begin();
        Refresh();
    }

    [RelayCommand]
    void CancelPair()
    {
        RemoteHost.Server?.Pairing.Cancel();
        RemoteHost.StopIfIdle();
        Refresh();
    }
}

/// <summary>A paired phone as a row: name, when paired and last seen, and Revoke.</summary>
public sealed class PhoneDevice(RemoteDevice device, IRelayCommand revoke)
{
    public RemoteDevice Device { get; } = device;
    public string Name => Device.Name;
    public string Seen => $"paired {Device.Paired:d MMM yyyy}, " + (Device.LastSeen is { } s ? $"last seen {s:d MMM HH:mm}" : "not used yet");
    public IRelayCommand RevokeCommand { get; } = revoke;
    public string RevokeName => $"Revoke {Device.Name}";
}
