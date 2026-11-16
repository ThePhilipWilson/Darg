using System.Net;
using System.Text;
using Workbench.Core;

namespace Workbench.Core.Tests;

public sealed class ArtifactTests : IDisposable
{
    readonly string _home = Directory.CreateTempSubdirectory("wb-art-").FullName;
    static readonly HttpClient Http = new();

    string Make(string agent, string name, string html)
    {
        var f = Path.Combine(ArtifactStore.AgentFolder("alpha", agent, _home), name);
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
        Assert.Contains("window.workbench", out_);
        Assert.Contains("'/reply/t/p/a/f.html'", out_);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(out_, "window.workbench"));
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
        var b = ArtifactStore.Briefing(@"C:\Users\x\AppData\Roaming\Workbench\home\projects\p\artifacts\a1");
        Assert.Contains("workbench.send", b);
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
