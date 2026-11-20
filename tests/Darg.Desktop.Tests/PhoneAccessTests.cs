using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>AND-0002: a phone reaches the running app's API. Pairing from Settings shows a QR; the phone, pinned to the
/// certificate the QR names, trades the code for a token and reads projects, cards and agents with it; without a token,
/// with a command off its list, or once revoked from Settings, it's refused.</summary>
public class PhoneAccessTests
{
    [AvaloniaFact]
    public async Task A_paired_phone_reads_projects_cards_and_agents_and_an_unpaired_one_is_refused()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo", "In Progress");
        await app.Start();

        // Fettle's phone-pair (and Settings' Pair a phone): Settings opens with the QR
        var pair = JsonNode.Parse(await app.Api("phone-pair"))!;
        var phone = app.Main.SettingsPage?.Phone ?? throw new InvalidOperationException("Settings didn't open.");
        Assert.True(phone.Pairing);
        Assert.NotNull(phone.Qr);
        await DargApp.Settle(200);
        app.Shot("pairing");

        var uri = new Uri(pair["uri"]!.GetValue<string>());
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var (port, code, fingerprint) = (q["port"]!, q["code"]!, q["fp"]!);
        Assert.Equal(RemoteHost.Server!.Fingerprint, fingerprint);
        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && RemoteCert.Fingerprint(cert) == fingerprint,
        }) { Timeout = TimeSpan.FromSeconds(20) };
        var root = $"https://127.0.0.1:{port}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(http, HttpMethod.Get, $"{root}/api/projects")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(http, HttpMethod.Post, $"{root}/api/cards", new { project = "proj" })).Status);

        var paired = await Send(http, HttpMethod.Post, $"{root}/pair", new { code, name = "Test phone" });
        Assert.Equal(HttpStatusCode.OK, paired.Status);
        var token = JsonNode.Parse(paired.Body)!["token"]!.GetValue<string>();
        await app.Until(() => !phone.Pairing && phone.Devices.Count == 1, "Settings to list the phone and close the pairing");
        Assert.Equal("Test phone", phone.Devices.Single().Name);
        app.Shot("paired");

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var projects = await Send(http, HttpMethod.Get, $"{root}/api/projects");
        Assert.Equal(HttpStatusCode.OK, projects.Status);
        Assert.Equal("proj", JsonNode.Parse(projects.Body)!.AsArray().Single()!["id"]!.GetValue<string>());
        var cards = await Send(http, HttpMethod.Post, $"{root}/api/cards", new { project = "proj" });
        Assert.Equal(HttpStatusCode.OK, cards.Status);
        Assert.Contains(card.Id, cards.Body);
        var agents = await Send(http, HttpMethod.Get, $"{root}/api/agents?project=proj");
        Assert.Equal(HttpStatusCode.OK, agents.Status);
        Assert.StartsWith("[", agents.Body.TrimStart());

        // off the phone's list: refused even paired
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(http, HttpMethod.Post, $"{root}/api/run-command", new { project = "proj", command = "x" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(http, HttpMethod.Post, $"{root}/api/phone-pair")).Status);

        // a second phone, paired from Settings' button this time
        phone.PairCommand.Execute(null);
        Assert.True(phone.Pairing);
        var second = await Send(http, HttpMethod.Post, $"{root}/pair", new { code = phone.Code, name = "Tablet" }); // typed, with its dash
        Assert.Equal(HttpStatusCode.OK, second.Status);
        var token2 = JsonNode.Parse(second.Body)!["token"]!.GetValue<string>();
        await app.Until(() => phone.Devices.Count == 2, "Settings to list both phones");

        // the first revoked from Settings: its next call is refused, the other's still works
        phone.Devices.Single(d => d.Name == "Test phone").RevokeCommand.Execute(null);
        await app.Until(() => phone.Devices.Count == 1, "Settings to drop the revoked phone");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(http, HttpMethod.Get, $"{root}/api/projects")).Status);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        Assert.Equal(HttpStatusCode.OK, (await Send(http, HttpMethod.Get, $"{root}/api/projects")).Status);

        // the last one revoked through the API: nothing paired and no pairing open, so it stops listening
        await app.Api("phone-revoke", new { device = "Tablet" });
        Assert.Empty(RemoteHost.Devices.All());
        Assert.Null(RemoteHost.Server);
        await Assert.ThrowsAsync<HttpRequestException>(() => Send(http, HttpMethod.Get, $"{root}/api/projects"));
    }

    /// <summary>A request from off the UI thread (the server's handlers come back to it), with the app pumped meanwhile.</summary>
    static async Task<(HttpStatusCode Status, string Body)> Send(HttpClient http, HttpMethod method, string url, object? body = null)
    {
        var task = Task.Run(async () =>
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req);
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
        });
        while (!task.IsCompleted) await DargApp.Settle(20);
        return await task;
    }
}
