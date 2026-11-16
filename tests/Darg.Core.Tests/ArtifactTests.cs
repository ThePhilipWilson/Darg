using System.Net;
using System.Text;
using Darg.Core;

namespace Darg.Core.Tests;

public sealed class ArtifactTests : IDisposable
{
    readonly string _home = Directory.CreateTempSubdirectory("wb-art-").FullName;
    static readonly HttpClient Http = new();

    string Make(string agent, string name, string html, string project = "alpha")
    {
        var f = Path.Combine(ArtifactStore.AgentFolder(project, agent, _home), name);
        File.WriteAllText(f, html);
        return f;
    }

    [Fact]
    public void Lists_by_title_newest_first_and_knows_the_owner()
    {
        var old = Make("a1", "art-review.html", "<html><head><title>  Art   review &amp; picks </title></head></html>");
        File.SetLastWriteTime(old, DateTime.Now.AddHours(-1));
        var fresh = Make("b2", "card_compare.html", "<p>no title</p>");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fresh)!, "notes.txt"), "not an artifact");

        var list = ArtifactStore.List("alpha", _home);
        Assert.Equal(["card compare", "Art review & picks"], list.Select(a => a.Title));
        Assert.Equal(["b2", "a1"], list.Select(a => a.Agent));
        Assert.Equal(("alpha", "a1"), ArtifactStore.Owner(old, _home));
        Assert.Null(ArtifactStore.Owner(Path.Combine(_home, "projects", "alpha", "notes", "x.html"), _home));
        Assert.Null(ArtifactStore.Owner(Path.ChangeExtension(old, ".txt"), _home));
    }

    [Theory]
    [InlineData("<html><head><title>x</title></head><body/></html>", "<html><head><script>")]
    [InlineData("<HTML><HEAD lang=en><title>x</title></HEAD></HTML>", "<HEAD lang=en><script>")]
    [InlineData("<div>bare fragment</div>", "<script>")]
    public void The_bridge_goes_in_the_head_or_first(string html, string startsWith)
    {
        var out_ = ArtifactServer.WithBridge(html, "/reply/t/p/a/f.html");
        Assert.Contains(startsWith, out_);
        Assert.Contains("window.darg", out_);
        Assert.Contains("window.darg", out_); // pages written before the rename still work
        Assert.Contains("'/reply/t/p/a/f.html'", out_);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(out_, "window.darg"));
    }

    [Fact]
    public async Task Serves_pages_with_the_bridge_and_takes_replies_only_with_the_token()
    {
        Make("a1", "review.html", "<html><head><title>Review</title></head><body>hi</body></html>");
        var img = Path.Combine(_home, "pic.png");
        File.WriteAllBytes(img, [0x89, 0x50, 0x4e, 0x47]);
        File.WriteAllText(Path.Combine(_home, "secret.txt"), "no");
        using var server = ArtifactServer.Start(_home, FreePort());
        var replies = new List<ArtifactReply>();
        server.Reply += replies.Add;

        var artifact = ArtifactStore.List("alpha", _home).Single();
        var page = await Http.GetStringAsync(server.Url(artifact));
        Assert.Contains("hi", page);
        var path = System.Text.RegularExpressions.Regex.Match(page, @"fetch\('([^']+)'").Groups[1].Value;
        Assert.StartsWith("/reply/", path);

        var ok = await Http.PostAsync(server.Root.TrimEnd('/') + path, new StringContent("{\"pick\":3}", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(new ArtifactReply("alpha", "a1", "review.html", "{\"pick\":3}"), Assert.Single(replies));

        var forged = await Http.PostAsync($"{server.Root}reply/{new string('0', 32)}/alpha/a1/review.html", new StringContent("x"));
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Single(replies);

        // images anywhere on the machine, nothing else; nothing outside the agent's folder through /a/
        Assert.Equal(4, (await Http.GetByteArrayAsync($"{server.Root}file/{img.Replace('\\', '/')}")).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"{server.Root}file/{Path.Combine(_home, "secret.txt").Replace('\\', '/')}")).StatusCode);
        // (http.sys may refuse an encoded slash itself, 403, before the server sees it: either way, not served)
        foreach (var sneaky in new[] { "..%2F..%2F..%2F..%2Fsecret.txt", "..%5C..%5C..%5C..%5Csecret.txt", "%2E%2E/%2E%2E/%2E%2E/%2E%2E/secret.txt" })
            Assert.False((await Http.GetAsync($"{server.Root}a/alpha/a1/{sneaky}")).IsSuccessStatusCode, sneaky);
    }

    [Fact]
    public void An_image_review_is_an_artifact_titled_from_its_json()
    {
        var titled = Make("a1", "round-3.review.json", "{\"title\": \"  Outfit   round 3 \", \"rows\": []}", "example");
        var bare = Make("a1", "hands_fix.review.json", "not json at all", "example");
        Make("a1", "data.json", "{}", "example");

        Assert.Equal(["Outfit round 3", "hands fix"], ArtifactStore.List("example", _home).Select(a => a.Title).Order().Reverse());
        Assert.Equal(("example", "a1"), ArtifactStore.Owner(titled, _home));
        Assert.Equal(("example", "a1"), ArtifactStore.Owner(bare, _home));
        Assert.Null(ArtifactStore.Owner(Path.Combine(Path.GetDirectoryName(titled)!, "data.json"), _home));
    }

    [Fact]
    public void A_review_page_carries_its_rows_and_nothing_in_them_can_end_the_script()
    {
        var page = ArtifactServer.ReviewPage("{\"rows\": [{\"id\": \"cat\", \"note\": \"</script><script>alert(1)</script>\", \"candidates\": [\"D:/x/a.png\"]}]}", "Fish & <chips>");
        Assert.Contains("<title>Fish &amp; &lt;chips&gt;</title>", page);
        Assert.Contains("D:/x/a.png", page);
        Assert.DoesNotContain("alert(1)</script>", page);
        Assert.Contains(@"</script>", page);
        Assert.Contains("const ERROR = null;", page);

        var broken = ArtifactServer.ReviewPage("{\"rows\": [", "x");
        Assert.Contains("const DATA = null;", broken);
        Assert.Contains("JSON doesn", broken);
        Assert.Contains("has no rows", ArtifactServer.ReviewPage("{\"title\": \"x\"}", "x"));
    }

    [Fact]
    public async Task A_review_is_served_as_the_picker_with_the_bridge_and_its_picks_come_back()
    {
        Make("a1", "round.review.json", "{\"title\": \"Round\", \"folder\": \"D:/run\", \"rows\": [{\"id\": \"cat\", \"candidates\": [\"cat/seed1.png\"]}]}", "example");
        using var server = ArtifactServer.Start(_home, FreePort());
        var replies = new List<ArtifactReply>();
        server.Reply += replies.Add;

        var page = await Http.GetStringAsync(server.Url(ArtifactStore.List("example", _home).Single()));
        Assert.Contains("Send picks", page);
        Assert.Contains("cat/seed1.png", page);
        var path = System.Text.RegularExpressions.Regex.Match(page, @"fetch\('([^']+)'").Groups[1].Value;
        Assert.EndsWith("/example/a1/round.review.json", path);

        await Http.PostAsync(server.Root.TrimEnd('/') + path, new StringContent("cat: seed1.png"));
        Assert.Equal(new ArtifactReply("example", "a1", "round.review.json", "cat: seed1.png"), Assert.Single(replies));
    }

    [Fact]
    public async Task Lists_and_closes_agents_only_with_the_token()
    {
        using var server = ArtifactServer.Start(_home, FreePort());
        var token = File.ReadAllText(Path.Combine(_home, "artifact-token")).Trim();
        List<string>? closed = null;
        server.ListAgents = () => Task.FromResult("[{\"id\":\"a1\"}]");
        server.CloseAgents = ids => { closed = [.. ids]; return Task.FromResult($"Closed {ids.Count}"); };

        Assert.Equal("[{\"id\":\"a1\"}]", await Http.GetStringAsync($"{server.Root}agents/{token}"));
        var r = await Http.PostAsync($"{server.Root}agents/{token}/close", new StringContent("[\"a1\", \"b2\"]"));
        Assert.Equal("Closed 2", await r.Content.ReadAsStringAsync());
        Assert.Equal(["a1", "b2"], closed);

        var bad = new string('0', 32);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.GetAsync($"{server.Root}agents/{bad}")).StatusCode);
        closed = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.PostAsync($"{server.Root}agents/{bad}/close", new StringContent("a1"))).StatusCode);
        Assert.Null(closed);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"{server.Root}agents/{token}/close")).StatusCode);
    }

    [Fact]
    public async Task The_api_lists_and_runs_commands_only_with_the_token()
    {
        using var server = ArtifactServer.Start(_home, FreePort());
        var token = File.ReadAllText(Path.Combine(_home, "artifact-token")).Trim();
        ApiArgs? got = null;
        server.Api.Add("show", "bring something up: {project, card?}", a => { got = a; return $"{a.Need("project")} on screen"; });
        server.Api.Add("projects", "the projects", _ => "[{\"id\":\"p1\"}]");

        var help = await Http.GetStringAsync($"{server.Root}api/{token}");
        Assert.Contains("show: bring something up", help);
        Assert.Contains("projects: the projects", help);

        var r = await Http.PostAsync($"{server.Root}api/{token}/show", new StringContent("{\"project\":\"p1\",\"card\":\"WOR-1\"}"));
        Assert.Equal("p1 on screen", await r.Content.ReadAsStringAsync());
        Assert.Equal("WOR-1", got!.Get("card"));
        // the query string works too, for a GET; a JSON answer says so
        Assert.Equal("p2 on screen", await Http.GetStringAsync($"{server.Root}api/{token}/show?project=p2"));
        Assert.Equal("application/json", (await Http.GetAsync($"{server.Root}api/{token}/projects")).Content.Headers.ContentType?.MediaType);

        var missing = await Http.PostAsync($"{server.Root}api/{token}/show", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("project", await missing.Content.ReadAsStringAsync());
        var unknown = await Http.GetAsync($"{server.Root}api/{token}/fly");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("show:", await unknown.Content.ReadAsStringAsync()); // says what there is

        var bad = new string('0', 32);
        got = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.GetAsync($"{server.Root}api/{bad}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.PostAsync($"{server.Root}api/{bad}/show", new StringContent("{\"project\":\"p1\"}"))).StatusCode);
        Assert.Null(got);
    }

    [Fact]
    public void Api_arguments_come_from_json_or_plain_text()
    {
        var a = ApiArgs.Parse("{\"ids\":[\"a1\",\"b2\"],\"last\":5,\"name\":\"  \"}", [new("project", "p1"), new("last", "9")]);
        Assert.Equal(["a1", "b2"], a.List("ids"));
        Assert.Equal("5", a.Get("last")); // the body wins over the query string
        Assert.Equal("p1", a.Get("project"));
        Assert.Null(a.Get("name"));
        Assert.Throws<ApiError>(() => a.Need("card"));
        var text = ApiArgs.Parse("a1, b2 c3");
        Assert.Equal("a1, b2 c3", text.Get("text"));
        Assert.Equal(["a1", "b2"], ApiArgs.Parse("{\"ids\":\"a1 b2\"}").List("ids"));
    }

    [Fact]
    public void Agent_ids_come_out_of_any_list() =>
        Assert.Equal(["a1", "b2", "c3"], ArtifactServer.Ids("a1, b2\nc3 a1"));

    [Fact]
    public void The_token_is_kept_across_restarts()
    {
        using (ArtifactServer.Start(_home, FreePort())) { }
        var first = File.ReadAllText(Path.Combine(_home, "artifact-token"));
        using (ArtifactServer.Start(_home, FreePort())) { }
        Assert.Equal(first, File.ReadAllText(Path.Combine(_home, "artifact-token")));
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void The_briefing_is_safe_on_a_command_line()
    {
        var b = ArtifactStore.Briefing(@"C:\Users\x\AppData\Roaming\Darg\home\projects\p\artifacts\a1");
        Assert.Contains("darg.send", b);
        Assert.DoesNotContain(b, c => c is '<' or '>' or '"' or '&' or '|' or '^' or '%');
    }

    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    public void Dispose() { try { Directory.Delete(_home, true); } catch (IOException) { } }
}
