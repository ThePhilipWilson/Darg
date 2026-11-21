using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>AND-0002: phone access to the Darg API. Devices keep only a token's hash, a pairing code works once and
/// not after it runs out or is guessed at, and the server, over real TLS, refuses anything without a paired token,
/// and anything not on the phone's list even with one.</summary>
public sealed class RemoteTests : IDisposable
{
    readonly string _home = Directory.CreateTempSubdirectory("darg-remote-").FullName;

    public void Dispose() { try { Directory.Delete(_home, true); } catch (IOException) { } }

    [Fact]
    public void A_device_keeps_only_the_hash_of_its_token_and_a_revoke_refuses_it()
    {
        var devices = new RemoteDevices(_home);
        var (phone, token) = devices.Add("Pixel");
        var file = File.ReadAllText(devices.FilePath);
        Assert.DoesNotContain(token, file);
        Assert.Contains(RemoteAccess.Hash(token), file);

        Assert.Equal(phone.Id, devices.Check(token)?.Id);
        Assert.NotNull(devices.All().Single().LastSeen);
        Assert.Null(devices.Check(token + "x"));
        Assert.Null(devices.Check(""));
        Assert.Null(devices.Check(null));

        Assert.Single(devices.Revoke("pixel"));
        Assert.Null(devices.Check(token));
        Assert.Empty(devices.All());
    }

    [Fact]
    public void A_pairing_code_works_once_and_not_once_run_out_or_guessed_at()
    {
        var p = new RemotePairing();
        var now = DateTime.Now;
        Assert.Equal(RemotePairing.Result.NoPairing, p.Redeem("ANYTHING", now));

        var code = p.Begin(now);
        Assert.Matches("^[A-HJ-NP-Z2-9]{8}$", code);
        Assert.Equal(RemotePairing.Result.Paired, p.Redeem($"{code[..4].ToLowerInvariant()}-{code[4..]}", now)); // as typed on a phone
        Assert.Equal(RemotePairing.Result.NoPairing, p.Redeem(code, now)); // once only

        code = p.Begin(now);
        Assert.Equal(RemotePairing.Result.NoPairing, p.Redeem(code, now + RemoteAccess.PairingLasts));

        code = p.Begin(now);
        for (var i = 0; i < RemoteAccess.WrongTries; i++) Assert.Equal(RemotePairing.Result.Wrong, p.Redeem("WRONGONE", now));
        Assert.Equal(RemotePairing.Result.NoPairing, p.Redeem(code, now)); // too many wrong: the right one is no good now
    }

    [Fact]
    public void The_pair_uri_carries_hosts_port_code_and_fingerprint()
    {
        var uri = new Uri(RemoteAccess.PairUri("My PC", ["192.168.1.20", "100.101.1.2"], 47818, "ABCDEFGH", "ab12"));
        Assert.Equal("darg", uri.Scheme);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("My PC", q["name"]);
        Assert.Equal("192.168.1.20,100.101.1.2", q["hosts"]);
        Assert.Equal("47818", q["port"]);
        Assert.Equal("ABCDEFGH", q["code"]);
        Assert.Equal("ab12", q["fp"]);
        Assert.True(RemoteAccess.IsTailscale(IPAddress.Parse("100.101.1.2")));
        Assert.False(RemoteAccess.IsTailscale(IPAddress.Parse("100.200.1.2")));
        Assert.False(RemoteAccess.IsTailscale(IPAddress.Parse("192.168.1.20")));
    }

    static DargApi Api()
    {
        var api = new DargApi();
        api.Add("projects", "the projects", _ => "[{\"id\":\"proj\"}]");
        api.Add("cards", "a project's cards: {project}", a => $"[{{\"project\":\"{a.Need("project")}\"}}]");
        api.Add("agents", "the agents", _ => "[]");
        api.Add("run-command", "runs a shell command on the PC", _ => "ran it");
        api.Add("phone-pair", "pair another phone", _ => "paired");
        return api;
    }

    static HttpClient Client(string fingerprint) => new(new HttpClientHandler
    {
        // what the phone does: trust exactly the certificate whose fingerprint the QR carried
        ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && RemoteCert.Fingerprint(cert) == fingerprint,
    }) { Timeout = TimeSpan.FromSeconds(20) };

    [Fact]
    public async Task Over_TLS_a_request_without_a_paired_token_is_refused_and_a_paired_one_reads_projects_cards_and_agents()
    {
        using var server = RemoteServer.Start(Api(), () => _home, port: 0, loopbackOnly: true);
        using var http = Client(server.Fingerprint);
        var root = $"https://127.0.0.1:{server.Port}";

        // nobody paired: refused, with or without a made-up token
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"{root}/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"{root}/api")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"{root}/anything")).StatusCode);
        var guess = new HttpRequestMessage(HttpMethod.Get, $"{root}/api/projects") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", RemoteAccess.NewToken()) } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(guess)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"{root}/hello")).StatusCode);

        // pairing: no code open, then a wrong one, then the right one
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{root}/pair", Json(new { code = "ABCDEFGH", name = "Pixel" }))).StatusCode);
        var code = server.Pairing.Begin();
        Assert.Contains($"code={code}", server.PairUri());
        Assert.Contains($"fp={server.Fingerprint}", server.PairUri());
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{root}/pair", Json(new { code = "WRONGONE", name = "Pixel" }))).StatusCode);
        var paired = await http.PostAsync($"{root}/pair", Json(new { code, name = "Pixel" }));
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var token = JsonNode.Parse(await paired.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();
        Assert.Equal("Pixel", server.Devices.All().Single().Name);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{root}/pair", Json(new { code, name = "Again" }))).StatusCode);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal("[{\"id\":\"proj\"}]", await http.GetStringAsync($"{root}/api/projects"));
        var cards = await http.PostAsync($"{root}/api/cards", Json(new { project = "proj" }));
        Assert.Equal("[{\"project\":\"proj\"}]", await cards.Content.ReadAsStringAsync());
        Assert.Equal("[{\"project\":\"q\"}]", await http.GetStringAsync($"{root}/api/cards?project=q"));
        Assert.Equal("[]", await http.GetStringAsync($"{root}/api/agents"));
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"{root}/api/cards")).StatusCode); // an ApiError still reads

        // what isn't on the phone's list stays refused, token or not, and isn't listed
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{root}/api/run-command", Json(new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{root}/api/phone-pair", Json(new { }))).StatusCode);
        var help = await http.GetStringAsync($"{root}/api");
        Assert.Contains("projects:", help);
        Assert.DoesNotContain("run-command", help);
        Assert.DoesNotContain("phone-pair", help);

        // revoked: refused on the next call
        server.Devices.Revoke("Pixel");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"{root}/api/projects")).StatusCode);
    }

    [Fact]
    public async Task A_phone_pinned_to_another_certificate_does_not_connect_and_the_certificate_survives_a_restart()
    {
        string fingerprint;
        int port;
        using (var server = RemoteServer.Start(Api(), () => _home, port: 0)) { fingerprint = server.Fingerprint; port = server.Port; }
        using var again = RemoteServer.Start(Api(), () => _home, port: 0, loopbackOnly: true);
        Assert.Equal(fingerprint, again.Fingerprint); // a paired phone's pin stays good

        using var wrong = Client(new string('0', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => wrong.GetAsync($"https://127.0.0.1:{again.Port}/hello"));
    }

    [Fact]
    public async Task Plain_http_gets_nothing_and_an_oversized_head_is_refused()
    {
        using var server = RemoteServer.Start(Api(), () => _home, port: 0, loopbackOnly: true);
        using var plain = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        await Assert.ThrowsAnyAsync<Exception>(() => plain.GetStringAsync($"http://127.0.0.1:{server.Port}/hello"));

        using var http = Client(server.Fingerprint);
        var big = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{server.Port}/hello");
        big.Headers.Add("X-Big", new string('a', 20_000));
        Assert.Equal((HttpStatusCode)431, (await http.SendAsync(big)).StatusCode);
    }

    [Fact]
    public void Loopback_only_binds_127_0_0_1_and_the_default_binds_every_address()
    {
        using (var local = RemoteServer.Start(Api(), () => _home, port: 0, loopbackOnly: true))
            Assert.Equal(IPAddress.Loopback, local.LocalEndpoint.Address);
        // the default isn't started here: binding every address is what makes Windows ask about the firewall
        Assert.Equal(IPAddress.IPv6Any, RemoteServer.BindAddress(loopbackOnly: false));
    }

    static StringContent Json(object o) => new(System.Text.Json.JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
}
