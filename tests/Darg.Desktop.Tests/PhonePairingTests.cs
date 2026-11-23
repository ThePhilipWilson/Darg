using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;
using Darg.Mobile;
using Darg.Mobile.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>AND-0011: the phone app pairs from the QR's link over TLS pinned to the certificate in it, keeps the pin and its own
/// token, and is cut off by a Revoke in Settings. The desktop side is the real running app (RemoteServer on loopback).</summary>
public class PhonePairingTests
{
    /// <summary>The link Darg's QR holds, with its hosts swapped for ones the test can reach (the QR lists the LAN's;
    /// tests keep phone access on loopback), and the fingerprint replaced when a test pins another certificate.</summary>
    static async Task<string> Link(DargApp app, string hosts = "127.0.0.1", string? fingerprint = null)
    {
        var pair = JsonNode.Parse(await app.Api("phone-pair"))!;
        var uri = new Uri(pair["uri"]!.GetValue<string>());
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return $"darg://pair?v=1&name=Desk&hosts={hosts}&port={q["port"]}&code={q["code"]}&fp={fingerprint ?? q["fp"]}";
    }

    static ShellViewModel NewShell(DargApp app, out ConnectionStore store)
    {
        store = new ConnectionStore(Path.Combine(app.Folder, "phone", "connection.json"));
        return new ShellViewModel(store);
    }

    [AvaloniaFact]
    public async Task A_phone_pairs_from_the_link_pins_the_certificate_keeps_its_token_and_a_revoke_cuts_it_off()
    {
        using var app = DargApp.Make();
        await app.Start();
        var link = await Link(app, hosts: "127.0.0.2,127.0.0.1"); // the first host answers nothing: the phone moves on to the next
        var shell = NewShell(app, out var store);
        var connect = Assert.IsType<ConnectViewModel>(shell.Page);

        connect.PairText = link;
        await connect.Pair();
        var projects = shell.OpenProjects();
        Assert.Equal("proj", Assert.Single(projects.Projects).Id);

        // what's kept: the address over https on the phone port, a token of its own, the pin, and the PC's name
        var saved = store.Load()!;
        Assert.True(saved.IsPinned);
        Assert.StartsWith("https://127.0.0.1:", saved.Address);
        Assert.Equal(RemoteHost.Server!.Fingerprint, saved.Fingerprint);
        Assert.Equal(RemoteHost.Server.Port, new Uri(saved.Address).Port);
        Assert.DoesNotContain(saved.Token, saved.Address);
        Assert.NotEqual(ArtifactHost.Server!.ApiUrl[(ArtifactHost.Server.ApiUrl.LastIndexOf('/') + 1)..], saved.Token); // not the instance's token
        Assert.Contains("Paired with", projects.Where);
        Assert.Contains(saved.Fingerprint[..8], projects.Where);

        // Settings lists the phone under the name the phone gave
        var phone = app.Main.SettingsPage?.Phone ?? throw new InvalidOperationException("Settings didn't open.");
        await app.Until(() => phone.Devices.Count == 1, "Settings to list the phone");

        // opened again, the saved pairing opens to Needs you, the projects a step on, over the pinned route
        var again = new ShellViewModel(store);
        var list = again.OpenProjects();
        await app.Until(() => list.Projects.Count == 1, "the saved pairing to list the projects");

        // a second phone, so the server stays up when the first is revoked (the last revoke stops it listening)
        await Pairer.Pair(PairingLink.Parse(await Link(app))!, "Tablet");
        phone = app.Main.SettingsPage!.Phone!; // phone-pair opened Settings afresh
        await app.Until(() => phone.Devices.Count == 2, "Settings to list both phones");

        // revoked in Settings: the phone's next call is refused and says to pair again
        phone.Devices.Single(d => d.Name == Darg.Mobile.App.DeviceName).RevokeCommand.Execute(null);
        await app.Until(() => phone.Devices.Count == 1, "Settings to drop the phone");
        await list.Refresh();
        Assert.True(list.Bad);
        Assert.True(list.Unpaired);
        Assert.Contains("Pair again", list.Status);

        // the last phone revoked too: the PC stops listening, so a call fails outright rather than answering
        phone.Devices.Single().RevokeCommand.Execute(null);
        await app.Until(() => phone.Devices.Count == 0, "Settings to drop the last phone");
        await list.Refresh();
        Assert.True(list.Bad);
        Assert.Equal("", app.Crashes());
    }

    [AvaloniaFact]
    public async Task A_phone_refuses_a_certificate_other_than_the_one_in_the_QR_and_sends_nothing()
    {
        using var app = DargApp.Make();
        await app.Start();
        var shell = NewShell(app, out var store);
        var connect = Assert.IsType<ConnectViewModel>(shell.Page);

        connect.PairText = await Link(app, fingerprint: new string('0', 64)); // the QR names some other certificate
        await connect.Pair();
        Assert.True(connect.Bad);
        Assert.Contains("isn't the PC in the QR", connect.Status);
        Assert.Null(store.Load());
        Assert.IsType<ConnectViewModel>(shell.Page);
        Assert.Empty(RemoteHost.Devices.All()); // the code was never sent, so nothing paired

        // and the code is still good for the right certificate
        connect.PairText = await Link(app);
        await connect.Pair();
        shell.OpenProjects();
        Assert.Single(RemoteHost.Devices.All());
    }

    [AvaloniaFact]
    public async Task A_paired_phone_refuses_a_server_whose_certificate_changed()
    {
        using var app = DargApp.Make();
        await app.Start();
        var shell = NewShell(app, out var store);
        var connect = (ConnectViewModel)shell.Page!;
        connect.PairText = await Link(app);
        await connect.Pair();
        var saved = store.Load()!;

        // the phone's pin is for another certificate now (a stand-in on the network): no call goes through
        using var client = new DargClient(saved with { Fingerprint = new string('a', 64) });
        var e = await Assert.ThrowsAsync<DargClientException>(() => client.Projects());
        Assert.Contains("different certificate", e.Message);
    }

    [AvaloniaFact]
    public async Task A_wrong_code_a_closed_pairing_and_a_bad_link_each_say_what_is_wrong()
    {
        using var app = DargApp.Make();
        await app.Start();
        var shell = NewShell(app, out var store);
        var connect = (ConnectViewModel)shell.Page!;

        connect.PairText = "https://example.com/not-a-pairing";
        await connect.Pair();
        Assert.Contains("isn't a Darg pairing link", connect.Status);

        var link = await Link(app);
        connect.PairText = link.Replace("code=", "code=ZZZZ");
        await connect.Pair();
        Assert.True(connect.Bad);
        Assert.True(connect.Status.Contains("Wrong code"), connect.Status);

        connect.PairText = "darg://pair?hosts=127.0.0.1&port=1&code=ABCD2345"; // no fingerprint: nothing to pin, so not a link
        await connect.Pair();
        Assert.Contains("isn't a Darg pairing link", connect.Status);
        Assert.Null(store.Load());
    }

    [Fact]
    public void The_pairing_link_reads_what_the_QR_holds_and_nothing_less()
    {
        var fp = new string('a', 32) + new string('B', 32);
        var link = PairingLink.Parse($" darg://pair?v=1&name=My%20PC&hosts=192.168.1.5,100.64.0.9&port=47818&code=abcd-efgh&fp={fp} ")!;
        Assert.Equal("My PC", link.Name);
        Assert.Equal(["192.168.1.5", "100.64.0.9"], link.Hosts);
        Assert.Equal(47818, link.Port);
        Assert.Equal("ABCDEFGH", link.Code);
        Assert.Equal(fp.ToLowerInvariant(), link.Fingerprint);

        Assert.Null(PairingLink.Parse(null));
        Assert.Null(PairingLink.Parse("http://x/pair?hosts=a&port=1&code=A&fp=" + fp));
        Assert.Null(PairingLink.Parse($"darg://pair?hosts=a&port=1&code=A&fp={fp[..60]}")); // a short fingerprint pins nothing
        Assert.Null(PairingLink.Parse($"darg://pair?port=1&code=A&fp={fp}")); // no host
        Assert.Null(PairingLink.Parse($"darg://pair?hosts=a&port=1&fp={fp}")); // no code
        Assert.Null(PairingLink.Parse($"darg://pair?hosts=a&port=0&code=A&fp={fp}"));
    }

    [Fact]
    public void A_connection_keeps_its_pin_and_the_old_usb_file_still_reads()
    {
        var dir = Directory.CreateTempSubdirectory("darg-conn-").FullName;
        try
        {
            var store = new ConnectionStore(Path.Combine(dir, "c.json"));
            var fp = new string('c', 64);
            store.Save(new Connection("https://10.0.0.2:47818", "tok", fp, "Desk"));
            var back = store.Load()!;
            Assert.Equal(new Connection("https://10.0.0.2:47818", "tok", fp, "Desk"), back);
            Assert.Equal("https://10.0.0.2:47818/api", back.ApiBase); // the token travels in a header, not the URL

            File.WriteAllText(store.File, """{"address":"http://localhost:47817","token":"abc"}"""); // what AND-0003's script writes
            var usb = store.Load()!;
            Assert.False(usb.IsPinned);
            Assert.Equal("http://localhost:47817/api/abc", usb.ApiBase);

            store.Clear();
            Assert.Null(store.Load());
        }
        finally { Directory.Delete(dir, true); }
    }
}
