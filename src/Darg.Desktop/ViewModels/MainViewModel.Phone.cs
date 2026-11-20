using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>Phone access (AND-0002): the remote server started at launch when a phone is paired, and the phone-*
/// commands, which run on the PC only: <see cref="RemoteAccess.Commands"/> leaves them out, so a phone can't pair another.</summary>
public partial class MainViewModel
{
    void StartRemote(InstanceConfig config, DargApi api)
    {
        RemoteHost.Api = api;
        RemoteHost.Allowed = config.Remote is not false;
        RemoteHost.StartIfPaired();
    }

    void RegisterPhoneApi(DargApi api)
    {
        void Add(string name, string usage, Func<ApiArgs, string> run) =>
            api.Add(name, usage, a => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => run(a)).GetTask());

        Add("phones", "phone access: whether it's on, the port, addresses and certificate fingerprint, the open pairing and the paired phones (id, name, paired, last seen).", _ => Json(Phones()));
        Add("phone-pair", "start pairing a phone, as Settings' Pair a phone does: opens Settings with the QR to scan and returns the code (good 10 minutes, once). Only when Philip asks.", _ => PhonePair());
        Add("phone-revoke", "unpair a phone: {device} (its id or name). Its next call is refused. Only when Philip asks.", PhoneRevoke);
    }

    JsonObject Phones()
    {
        var server = RemoteHost.Server;
        var devices = new JsonArray();
        foreach (var d in RemoteHost.Devices.All())
            devices.Add(new JsonObject { ["id"] = d.Id, ["name"] = d.Name, ["paired"] = d.Paired.ToString("yyyy-MM-dd HH:mm"), ["lastSeen"] = d.LastSeen?.ToString("yyyy-MM-dd HH:mm") });
        return new JsonObject
        {
            ["allowed"] = RemoteHost.Allowed, ["listening"] = server is not null, ["port"] = server?.Port, ["fingerprint"] = server?.Fingerprint,
            ["hosts"] = new JsonArray(RemoteAccess.Hosts().Select(h => (JsonNode?)h).ToArray()),
            ["pairing"] = server?.Pairing.Code is { } code ? new JsonObject { ["code"] = RemoteHost.Shown(code), ["expires"] = server.Pairing.Expires.ToString("HH:mm:ss") } : null,
            ["devices"] = devices, ["error"] = RemoteHost.Error,
        };
    }

    string PhonePair()
    {
        if (!RemoteHost.Allowed) throw new ApiError("Phone access is turned off for this instance (\"remote\": false in instance.json).");
        OpenSettings();
        SettingsPage!.Phone.Pair();
        var server = RemoteHost.Server ?? throw new ApiError($"Phone access couldn't start: {RemoteHost.Error}");
        return Json(new JsonObject
        {
            ["code"] = RemoteHost.Shown(server.Pairing.Code ?? ""), ["expires"] = server.Pairing.Expires.ToString("HH:mm:ss"),
            ["uri"] = server.PairUri(), ["shown"] = "Settings is open with the QR.",
        });
    }

    string PhoneRevoke(ApiArgs a)
    {
        var key = a.Need("device");
        var gone = RemoteHost.Revoke(key);
        SettingsPage?.Phone.Refresh();
        if (gone.Count == 0)
        {
            var paired = RemoteHost.Devices.All().Select(d => $"{d.Id} ({d.Name})").ToList();
            throw new ApiError($"No paired phone \"{key}\". Paired: {(paired.Count == 0 ? "none" : string.Join(", ", paired))}.");
        }
        return $"Revoked {string.Join(", ", gone.Select(d => d.Name))}.";
    }

    partial void OnSettingsPageChanged(SettingsViewModel? oldValue, SettingsViewModel? newValue) => oldValue?.Phone.Detach();
}
