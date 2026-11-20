using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A pull request or issue (a GitLab merge request too). The init-only parts come from GitHub's
/// GraphQL list and stay empty where a source doesn't give them.</summary>
public sealed record HubItem(int Number, string Title, string Author, string Url, DateTime Updated, IReadOnlyList<string> Labels, string Body)
{
    public bool Draft { get; init; }
    /// <summary>The checks on its newest commit: SUCCESS, FAILURE, PENDING, ERROR, EXPECTED, or "" for none.</summary>
    public string Checks { get; init; } = "";
    /// <summary>APPROVED, CHANGES_REQUESTED, REVIEW_REQUIRED, or "".</summary>
    public string Review { get; init; } = "";
    public string Head { get; init; } = "";
    public string Base { get; init; } = "";
    public int Additions { get; init; }
    public int Deletions { get; init; }
    public int Comments { get; init; }
    public IReadOnlyList<string> Assignees { get; init; } = [];
}

public sealed record HubRun(string Name, string Branch, string Status, string Conclusion, string Url, DateTime Updated)
{
    /// <summary>One word for the run: running, success, failure, cancelled…</summary>
    public string State => Status == "completed" ? (Conclusion.Length > 0 ? Conclusion : "done") : Status;
    public long Id { get; init; }
    /// <summary>What GitHub titles the run with: the commit's or PR's headline.</summary>
    public string Title { get; init; } = "";
    public string Event { get; init; } = "";
    public int Number { get; init; }
    public DateTime Started { get; init; }
    public string Sha { get; init; } = "";
    /// <summary>How long it ran (to now while it still runs); zero when unknown.</summary>
    public TimeSpan Took => Started == default || Updated == DateTime.MinValue ? TimeSpan.Zero
        : (Status == "completed" ? Updated : DateTime.UtcNow) - Started;
}

public sealed record HubCommit(string Sha, string Message, string Author, DateTime Date, string Checks, string Url)
{
    public string Short => Sha.Length > 7 ? Sha[..7] : Sha;
    public string Headline => Message.Split('\n')[0];
}
public sealed record HubBranch(string Name, HubCommit? Tip, int Pull, bool Default);
public sealed record HubAsset(string Name, long Size, int Downloads, string Url);
public sealed record HubRelease(string Tag, string Name, string Author, DateTime Date, bool Draft, bool Prerelease, string Body, string Url, IReadOnlyList<HubAsset> Assets);
/// <summary>A notification thread on the repo. <see cref="Subject"/> is the API address of what it's about
/// (a pull or issue), empty for a CI run's.</summary>
public sealed record HubNotice(string Id, string Title, string Type, string Reason, DateTime Updated, bool Unread, string Subject)
{
    /// <summary>The pull request or issue number the notice is about, when it's one.</summary>
    public int Number => Regex.Match(Subject, @"/(?:pulls|issues)/(\d+)$") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
}

public sealed record HubRepo(string Owner, string Name, IReadOnlyList<HubItem> Pulls, IReadOnlyList<HubItem> Issues, IReadOnlyList<HubRun> Runs, string Error)
{
    public string Full => $"{Owner}/{Name}";
    /// <summary>The repo's folder here.</summary>
    public string Folder { get; init; } = "";
    public bool IsGitLab { get; init; }
    public string Description { get; init; } = "";
    public string Url { get; init; } = "";
    public bool Private { get; init; }
    public string DefaultBranch { get; init; } = "";
    /// <summary>All open, where the lists hold only the newest.</summary>
    public int PullCount { get; init; }
    public int IssueCount { get; init; }
    public int BranchCount { get; init; }
    public int ReleaseCount { get; init; }
    public IReadOnlyList<HubCommit> Commits { get; init; } = [];
    public IReadOnlyList<HubBranch> Branches { get; init; } = [];
    public IReadOnlyList<HubRelease> Releases { get; init; } = [];
    public IReadOnlyList<HubNotice> Notices { get; init; } = [];
}

/// <summary>GitHub for a project, through the gh CLI. Each project names the gh account it signs in as
/// (work, home, game): that account's token is passed to gh for the call only, so the person's active gh
/// account is never switched and accounts never mix. Reads, apart from marking a notification read.
/// GH_PATH names another gh (the integration tests' fake).</summary>
public static class GitHub
{
    static readonly ConcurrentDictionary<string, string?> Tokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>owner/name from a repo's origin, when it's on github.com (ssh or https form).</summary>
    public static (string Owner, string Name)? ParseRemote(string url)
    {
        var m = Regex.Match(url.Trim(), @"github\.com(?:-[^:/\s]+)?[:/]+([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    public static (string Owner, string Name)? RepoOf(string repoPath)
    {
        var (code, output) = Run(repoPath, null, "git", "remote", "get-url", "origin");
        return code == 0 ? ParseRemote(output) : null;
    }

    /// <summary>One GraphQL call for everything listed but runs and notifications. Pull requests and issues carry
    /// their body, which a card made from one is given.</summary>
    public const string RepoQuery = """
        query($owner:String!,$name:String!){repository(owner:$owner,name:$name){
          description url isPrivate
          defaultBranchRef{ name target{ ... on Commit{ history(first:30){ nodes{ oid messageHeadline messageBody committedDate url author{ name user{login} } statusCheckRollup{state} } } } } }
          pullRequests(states:OPEN,first:30,orderBy:{field:UPDATED_AT,direction:DESC}){ totalCount nodes{ number title url body isDraft updatedAt headRefName baseRefName additions deletions reviewDecision author{login} labels(first:10){nodes{name}} comments{totalCount} assignees(first:5){nodes{login}} commits(last:1){nodes{commit{statusCheckRollup{state}}}} } }
          issues(states:OPEN,first:50,orderBy:{field:UPDATED_AT,direction:DESC}){ totalCount nodes{ number title url body updatedAt author{login} labels(first:10){nodes{name}} comments{totalCount} assignees(first:5){nodes{login}} } }
          releases(first:20,orderBy:{field:CREATED_AT,direction:DESC}){ totalCount nodes{ tagName name url isDraft isPrerelease publishedAt createdAt author{login} description releaseAssets(first:30){nodes{name downloadCount size downloadUrl}} } }
          refs(refPrefix:"refs/heads/",first:50,orderBy:{field:TAG_COMMIT_DATE,direction:DESC}){ totalCount nodes{ name target{ ... on Commit{ oid messageHeadline messageBody committedDate url author{name user{login}} statusCheckRollup{state} } } associatedPullRequests(first:1,states:OPEN){nodes{number}} } }
        }}
        """;

    public static HubRepo Load(string repoPath, string? account)
    {
        var repo = RepoOf(repoPath);
        if (repo is null)
        {
            // not GitHub: GitLab, if the origin is there
            var (code, origin) = Run(repoPath, null, "git", "remote", "get-url", "origin");
            if (code == 0 && GitLab.ParseRemote(origin) is { } lab) return GitLab.Load(repoPath, lab.Host, lab.Path) with { Folder = repoPath, IsGitLab = true };
            return new HubRepo("", Path.GetFileName(repoPath), [], [], [], "Its origin isn't on GitHub or GitLab.") { Folder = repoPath };
        }
        var (owner, name) = repo.Value;
        var gh = Gh.For(account);
        if (gh is null) return new HubRepo(owner, name, [], [], [], $"gh has no login for {account}: run gh auth login and pick that account.") { Folder = repoPath };
        var graph = gh.Graph(RepoQuery, ("owner", owner), ("name", name));
        var runs = gh.Get($"repos/{owner}/{name}/actions/runs?per_page=30")?["workflow_runs"] as JsonArray ?? [];
        // no & in a path: a gh that is a .cmd (the tests' fake) would have cmd split the line there
        var notices = gh.Get($"repos/{owner}/{name}/notifications?all=true", quiet: true) as JsonArray ?? [];
        return Parse(owner, name, graph?["repository"], runs, notices, gh.Error) with { Folder = repoPath };
    }

    /// <summary>The repo from the GraphQL answer's repository, the runs endpoint's workflow_runs and the
    /// notifications list (unread first).</summary>
    public static HubRepo Parse(string owner, string name, JsonNode? r, JsonArray runs, JsonArray notices, string error)
    {
        var pulls = Nodes(r?["pullRequests"]).Select(GraphItem).ToList();
        var issues = Nodes(r?["issues"]).Select(GraphItem).ToList();
        var def = (string?)r?["defaultBranchRef"]?["name"] ?? "";
        var commits = Nodes(r?["defaultBranchRef"]?["target"]?["history"]).Select(Commit).ToList();
        var branches = Nodes(r?["refs"]).Select(b => new HubBranch((string?)b?["name"] ?? "", b?["target"] is JsonObject t ? Commit(t) : null,
            (int?)Nodes(b?["associatedPullRequests"]).FirstOrDefault()?["number"] ?? 0, (string?)b?["name"] == def))
            .OrderByDescending(b => b.Default).ThenByDescending(b => b.Tip?.Date).ToList();
        var releases = Nodes(r?["releases"]).Select(n => new HubRelease((string?)n?["tagName"] ?? "", (string?)n?["name"] ?? "", (string?)n?["author"]?["login"] ?? "",
            Date(n?["publishedAt"]) is var p && p != DateTime.MinValue ? p : Date(n?["createdAt"]), (bool?)n?["isDraft"] ?? false, (bool?)n?["isPrerelease"] ?? false,
            (string?)n?["description"] ?? "", (string?)n?["url"] ?? "",
            Nodes(n?["releaseAssets"]).Select(a => new HubAsset((string?)a?["name"] ?? "", (long?)a?["size"] ?? 0, (int?)a?["downloadCount"] ?? 0, (string?)a?["downloadUrl"] ?? "")).ToList())).ToList();
        return new HubRepo(owner, name, pulls, issues, runs.Select(RunOf).ToList(), error)
        {
            Description = (string?)r?["description"] ?? "",
            Url = (string?)r?["url"] ?? $"https://github.com/{owner}/{name}",
            Private = (bool?)r?["isPrivate"] ?? false,
            DefaultBranch = def,
            PullCount = (int?)r?["pullRequests"]?["totalCount"] ?? pulls.Count,
            IssueCount = (int?)r?["issues"]?["totalCount"] ?? issues.Count,
            BranchCount = (int?)r?["refs"]?["totalCount"] ?? branches.Count,
            ReleaseCount = (int?)r?["releases"]?["totalCount"] ?? releases.Count,
            Commits = commits, Branches = branches, Releases = releases,
            Notices = notices.Select(n => new HubNotice((string?)n?["id"] ?? "", (string?)n?["subject"]?["title"] ?? "", (string?)n?["subject"]?["type"] ?? "",
                    (string?)n?["reason"] ?? "", Date(n?["updated_at"]), (bool?)n?["unread"] ?? false, (string?)n?["subject"]?["url"] ?? ""))
                .OrderByDescending(n => n.Unread).ThenByDescending(n => n.Updated).ToList(),
        };
    }

    internal static IEnumerable<JsonNode?> Nodes(JsonNode? connection) => connection?["nodes"] as JsonArray ?? [];

    /// <summary>A pull request or issue from the GraphQL list.</summary>
    public static HubItem GraphItem(JsonNode? n) => new(
        (int?)n?["number"] ?? 0, (string?)n?["title"] ?? "", (string?)n?["author"]?["login"] ?? "", (string?)n?["url"] ?? "",
        Date(n?["updatedAt"]), Nodes(n?["labels"]).Select(l => (string?)l?["name"]).OfType<string>().ToList(), (string?)n?["body"] ?? "")
    {
        Draft = (bool?)n?["isDraft"] ?? false,
        Checks = (string?)Nodes(n?["commits"]).FirstOrDefault()?["commit"]?["statusCheckRollup"]?["state"] ?? "",
        Review = (string?)n?["reviewDecision"] ?? "",
        Head = (string?)n?["headRefName"] ?? "", Base = (string?)n?["baseRefName"] ?? "",
        Additions = (int?)n?["additions"] ?? 0, Deletions = (int?)n?["deletions"] ?? 0,
        Comments = (int?)n?["comments"]?["totalCount"] ?? 0,
        Assignees = Nodes(n?["assignees"]).Select(a => (string?)a?["login"]).OfType<string>().ToList(),
    };

    /// <summary>A pull request or issue from the REST API.</summary>
    public static HubItem Item(JsonNode? n) => new(
        (int?)n?["number"] ?? 0, (string?)n?["title"] ?? "", (string?)n?["user"]?["login"] ?? "", (string?)n?["html_url"] ?? "",
        Date(n?["updated_at"]), (n?["labels"] as JsonArray ?? []).Select(l => (string?)l?["name"]).OfType<string>().ToList(),
        (string?)n?["body"] ?? "");

    public static HubCommit Commit(JsonNode? c)
    {
        var head = (string?)c?["messageHeadline"] ?? "";
        var body = (string?)c?["messageBody"] ?? "";
        return new HubCommit((string?)c?["oid"] ?? "", body.Length > 0 ? head + "\n\n" + body : head,
            (string?)c?["author"]?["user"]?["login"] ?? (string?)c?["author"]?["name"] ?? "", Date(c?["committedDate"]),
            (string?)c?["statusCheckRollup"]?["state"] ?? "", (string?)c?["url"] ?? "");
    }

    /// <summary>A workflow run from the REST API.</summary>
    public static HubRun RunOf(JsonNode? r) => new(
        (string?)r?["name"] ?? "", (string?)r?["head_branch"] ?? "", (string?)r?["status"] ?? "", (string?)r?["conclusion"] ?? "",
        (string?)r?["html_url"] ?? "", Date(r?["updated_at"]))
    {
        Id = (long?)r?["id"] ?? 0, Title = (string?)r?["display_title"] ?? "", Event = (string?)r?["event"] ?? "",
        Number = (int?)r?["run_number"] ?? 0, Started = Date(r?["run_started_at"]) is var s && s != DateTime.MinValue ? s : default,
        Sha = (string?)r?["head_sha"] ?? "",
    };

    /// <summary>A date from the API as UTC (MinValue when missing).</summary>
    internal static DateTime Date(JsonNode? n) =>
        DateTime.TryParse((string?)n, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.MinValue;

    /// <summary>Mark a notification thread read. Null when it worked, else what gh said.</summary>
    public static string? MarkRead(string? account, string threadId)
    {
        if (Gh.For(account) is not { } gh) return $"gh has no login for {account}.";
        gh.Call("api", "-X", "PATCH", $"notifications/threads/{threadId}");
        return gh.Error.Length > 0 ? gh.Error : null;
    }

    static string? TokenFor(string account)
    {
        var (code, output) = Run(null, null, "gh", "auth", "token", "--user", account);
        return code == 0 && output.Trim().Length > 0 ? output.Trim() : null;
    }

    /// <summary>The gh accounts this machine is logged in to (for Setup's choices).</summary>
    public static List<string> Accounts()
    {
        var (_, output) = Run(null, null, "gh", "auth", "status");
        return Regex.Matches(output, @"account (\S+)").Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    /// <summary>gh calls as one account, keeping the first thing that went wrong.</summary>
    internal sealed class Gh
    {
        readonly string? _token;
        readonly string? _account;
        public string Error { get; private set; } = "";
        Gh(string? token, string? account) { _token = token; _account = account; }

        /// <summary>Null when the account is named but gh has no login for it.</summary>
        public static Gh? For(string? account)
        {
            var token = account is { Length: > 0 } ? Tokens.GetOrAdd(account, TokenFor) : null;
            return account is { Length: > 0 } && token is null ? null : new Gh(token, account);
        }

        public string? Call(params string[] args) => Call(false, args);

        string? Call(bool quiet, params string[] args)
        {
            var (code, output) = Run(null, _token, "gh", args);
            if (code == 0) return output;
            if (!quiet && Error.Length == 0)
            {
                var said = output.Trim().Split('\n')[0];
                // GitHub answers 404 (GraphQL: "Could not resolve to a Repository") for a private repo the account can't see, not 403
                Error = said.Contains("404") || said.Contains("Could not resolve to a Repository")
                    ? $"Not found as {_account ?? "gh's active account"}: if the repo is private, set this project's GitHub account in Setup." : said;
            }
            return null;
        }

        public JsonNode? Get(string path, bool quiet = false) => Parse(Call(quiet, "api", path));

        /// <summary>A REST path's text as it comes (a job's log).</summary>
        public string? Text(string path) => Call(true, "api", path);

        /// <summary>A GraphQL query's data. Variables go as strings (-f), numbers as -F.</summary>
        public JsonNode? Graph(string query, params (string Name, object Value)[] vars)
        {
            // one line: GraphQL doesn't mind, and a gh that is a .cmd (the tests' fake) would lose everything past a newline
            var args = new List<string> { "api", "graphql", "-f", "query=" + Regex.Replace(query, @"\s+", " ").Trim() };
            foreach (var (n, v) in vars) { args.Add(v is int ? "-F" : "-f"); args.Add($"{n}={v}"); }
            var output = Call(false, [.. args]);
            if (output is null) return null;
            var node = Parse(output);
            if (node?["errors"] is JsonArray { Count: > 0 } errs && Error.Length == 0) Error = (string?)errs[0]?["message"] ?? "GitHub refused the query.";
            return node?["data"];
        }

        static JsonNode? Parse(string? s)
        {
            if (s is null) return null;
            try { return JsonNode.Parse(s); } catch (System.Text.Json.JsonException) { return null; }
        }
    }

    internal static (int Code, string Output) Run(string? cwd, string? token, string exe, params string[] args)
    {
        if (exe == "gh" && Environment.GetEnvironmentVariable("GH_PATH") is { Length: > 0 } fake) exe = fake;
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (cwd is not null) psi.WorkingDirectory = cwd;
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (token is not null) psi.Environment["GH_TOKEN"] = token;
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        try
        {
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000)) { p.Kill(); return (-1, "timed out"); }
            return (p.ExitCode, p.ExitCode == 0 ? output.Result : err.Result + output.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, $"{exe} isn't installed or isn't on PATH."); }
    }
}
