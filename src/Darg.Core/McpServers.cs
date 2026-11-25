using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>A project's picks of MCP servers (WOR-0208), kept in instance.json as its <c>mcp</c>. <see cref="On"/> holds the
/// servers the person ticked, each with its definition as it was then, so it still starts once gone from ~/.claude.json;
/// <see cref="Off"/> names servers ticked by default (the project's own, Unity ones, claude-draw) that the person unticked.</summary>
public sealed class McpChoice
{
    public Dictionary<string, JsonObject> On { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Off { get; set; } = [];
}

/// <summary>An MCP server a project's agents could start: where it's defined, its definition (ready for an --mcp-config
/// file: plugin paths filled in), and whether it's ticked when the person hasn't said (<see cref="DefaultOn"/>, with why).</summary>
public sealed record McpServer(string Name, string Source, JsonObject Definition, bool DefaultOn = false, string? Why = null)
{
    /// <summary>What it runs, for a tooltip: its command and arguments, or its address. Never its env or headers (tokens).</summary>
    public string Runs => (string?)Definition["url"] is { Length: > 0 } url ? url
        : string.Join(' ', new[] { (string?)Definition["command"] ?? "" }.Concat((Definition["args"] as JsonArray ?? []).Select(a => a?.ToString() ?? ""))).Trim();
}

/// <summary>Which MCP servers a project's agents start (WOR-0208). Every Claude session used to start every server in the
/// person's ~/.claude.json and plugins: five per agent on 2026-11-25, seven node processes and about 1 GB each, none of
/// the npx ones ever called (WOR-0207). Now agents start with <c>--strict-mcp-config</c> (which leaves out the person's
/// config, plugins and the repo's .mcp.json alike) and an <c>--mcp-config</c> file holding only the servers ticked on the
/// project's Context page. Ticked when not said: the project's own .mcp.json servers and this machine's for its folders
/// (the project asked for them), Unity servers in a Unity project (a repo with ProjectSettings/), and claude-draw, which
/// Darg's ✎ Draw relies on.</summary>
public static class McpServers
{
    public const string ClaudeDraw = "claude-draw";

    /// <summary>Where Claude Code's own files are read from: its folder (settings, plugins, skills) and its .claude.json.</summary>
    public sealed record Sources(string ClaudeDir, string ClaudeJson)
    {
        /// <summary>CLAUDE_CONFIG_DIR and the .claude.json in it when that's set (where Claude Code then keeps it), else
        /// ~/.claude and ~/.claude.json.</summary>
        public static Sources Here() => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d
            ? new(d, Path.Combine(d, ".claude.json"))
            : new(UserHome.Claude, Path.Combine(UserHome.Folder, ".claude.json"));
    }

    /// <summary>A Unity project: one of its repos has a ProjectSettings folder.</summary>
    public static bool IsUnityProject(ProjectConfig project)
    {
        try { return project.ExistingRepos().Any(r => Directory.Exists(Path.Combine(r, "ProjectSettings"))); }
        catch (Exception) { return false; }
    }

    static readonly Regex UnityWords = new(@"unity|coplay", RegexOptions.IgnoreCase);

    /// <summary>A server for the Unity editor, by its name, command, arguments or address (unity-mcp on localhost,
    /// coplay-mcp through uvx, mcp-unity through npx…). Not its env or headers, where a token could say anything.</summary>
    public static bool IsUnityServer(string name, JsonObject definition) =>
        UnityWords.IsMatch(name) || UnityWords.IsMatch((string?)definition["command"] ?? "") || UnityWords.IsMatch((string?)definition["url"] ?? "")
        || (definition["args"] as JsonArray ?? []).Any(a => UnityWords.IsMatch(a?.ToString() ?? ""));

    /// <summary>Every server the project could tick, one per name, in the order Claude Code ranks them (this machine's for
    /// the project's folders, the project's .mcp.json, the person's, then plugins'): a name taken earlier is left out later.</summary>
    public static List<McpServer> Offered(ProjectConfig project, Sources? from = null)
    {
        from ??= Sources.Here();
        var list = new List<McpServer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unity = IsUnityProject(project);
        void Add(string name, string source, JsonObject def, bool defaultOn, string? why)
        {
            if (name.Trim().Length == 0 || !seen.Add(name)) return;
            if (!defaultOn && unity && IsUnityServer(name, def)) (defaultOn, why) = (true, "a Unity server, in a Unity project");
            list.Add(new McpServer(name, source, def, defaultOn, why));
        }
        var repos = SafeRepos(project);
        var claudeJson = ReadObject(from.ClaudeJson);

        // local scope: what `claude mcp add` put on this machine for one of the project's folders
        if (claudeJson?["projects"] is JsonObject projects)
            foreach (var (path, entry) in projects)
                if (repos.Any(r => SamePath(r, path)))
                    foreach (var (name, def) in Servers(entry?["mcpServers"]))
                        Add(name, $"this machine, for {ProjectConfig.RepoName(path)}", def, true, "set up for this project's folder");
        // the project's own .mcp.json in each repo
        foreach (var r in repos)
            foreach (var (name, def) in Servers(ReadObject(Path.Combine(r, ".mcp.json"))?["mcpServers"]))
                Add(name, $"{ProjectConfig.RepoName(r)}/.mcp.json", def, true, "the project's .mcp.json");
        // the person's, from ~/.claude.json
        foreach (var (name, def) in Servers(claudeJson?["mcpServers"]))
            Add(name, "~/.claude.json", def, false, null);
        // plugins' (claude-draw among them)
        foreach (var (plugin, name, def) in PluginServers(repos, from.ClaudeDir))
            Add(name, $"plugin {plugin}", def, name.Equals(ClaudeDraw, StringComparison.OrdinalIgnoreCase),
                name.Equals(ClaudeDraw, StringComparison.OrdinalIgnoreCase) ? "Darg's ✎ Draw uses it" : null);
        return list;
    }

    /// <summary>One row of the Context page: a server, ticked or not. <see cref="Kept"/>: only Darg's copy is left (it has
    /// gone from where it was defined), still started while ticked.</summary>
    public sealed record Row(McpServer Server, bool On, bool Kept);

    /// <summary>What the Context page lists: every offered server with its tick, then the ticked ones only Darg still has.</summary>
    public static List<Row> Rows(ProjectConfig project, Sources? from = null)
    {
        var offered = Offered(project, from);
        var rows = offered.Select(s => new Row(s, Ticked(s, project.Mcp), false)).ToList();
        foreach (var (name, def) in project.Mcp?.On ?? [])
            if (!offered.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                rows.Add(new Row(new McpServer(name, "kept by Darg", def), true, true));
        return rows;
    }

    public static bool Ticked(McpServer server, McpChoice? choice) =>
        !(choice?.Off.Contains(server.Name, StringComparer.OrdinalIgnoreCase) ?? false)
        && (server.DefaultOn || (choice?.On.ContainsKey(server.Name) ?? false));

    /// <summary>Tick or untick a server for the project (its <see cref="ProjectConfig.Mcp"/>; saving it is the caller's). A
    /// ticked one keeps its definition; an unticked one that's on by default is named in Off.</summary>
    public static void Tick(ProjectConfig project, McpServer server, bool on)
    {
        var c = project.Mcp ??= new McpChoice();
        c.Off.RemoveAll(n => n.Equals(server.Name, StringComparison.OrdinalIgnoreCase));
        c.On.Remove(server.Name);
        if (on) c.On[server.Name] = (JsonObject)server.Definition.DeepClone();
        else if (server.DefaultOn) c.Off.Add(server.Name);
    }

    /// <summary>The ticked servers, by name, with the definition each starts with: where it's defined now, else Darg's copy.</summary>
    public static Dictionary<string, JsonObject> Chosen(ProjectConfig project, Sources? from = null)
    {
        var chosen = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows(project, from))
            if (row.On) chosen[row.Server.Name] = row.Server.Definition;
        return chosen;
    }

    /// <summary>The --mcp-config file's text: {"mcpServers": {name: definition}}.</summary>
    public static string ConfigText(IReadOnlyDictionary<string, JsonObject> servers)
    {
        var all = new JsonObject();
        foreach (var (name, def) in servers) all[name] = def.DeepClone();
        return new JsonObject { ["mcpServers"] = all }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Where a project's --mcp-config file is written: the instance's mcp folder, a file per project.</summary>
    public static string FileFor(string projectId, string? instanceFolder = null) =>
        Path.Combine(instanceFolder ?? InstanceConfig.Folder(), "mcp", FileNames.Safe(projectId.Length > 0 ? projectId : "default") + ".json");

    /// <summary>What a new (or resumed) agent of the project starts with: <c>--strict-mcp-config</c>, and when anything is
    /// ticked, <c>--mcp-config</c> naming a file written now with just those servers. No project (a Fettle chat that
    /// belongs to none): none at all. Never throws: a file that can't be written leaves the agent with no servers.</summary>
    public static List<string> LaunchArgs(ProjectConfig? project, string? instanceFolder = null, Sources? from = null)
    {
        List<string> args = ["--strict-mcp-config"];
        if (project is null) return args;
        try
        {
            var chosen = Chosen(project, from);
            if (chosen.Count == 0) return args;
            var file = FileFor(project.Id, instanceFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // several agents start at once: each writes the whole file aside and moves it in, so none reads half of one
            var temp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(temp, ConfigText(chosen), new UTF8Encoding(false));
            File.Move(temp, file, overwrite: true);
            args.AddRange(["--mcp-config", file]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return args;
    }

    // --- reading Claude Code's files ---

    static List<string> SafeRepos(ProjectConfig project)
    {
        try { return project.ExistingRepos(); } catch (Exception) { return []; }
    }

    static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    static JsonObject? ReadObject(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            return JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    static IEnumerable<(string Name, JsonObject Def)> Servers(JsonNode? node)
    {
        if (node is not JsonObject o) yield break;
        foreach (var (name, def) in o)
            if (def is JsonObject d) yield return (name, (JsonObject)d.DeepClone());
    }

    /// <summary>The servers of the plugins Claude Code would load: installed ones enabled in the person's settings (or a
    /// repo's), and plugin folders in ~/.claude/skills (claude-draw is one), unless a setting turns them off. Each
    /// definition with ${CLAUDE_PLUGIN_ROOT} and ${CLAUDE_PLUGIN_DATA} filled in, which only a plugin's own load would do.</summary>
    static IEnumerable<(string Plugin, string Server, JsonObject Def)> PluginServers(List<string> repos, string dir)
    {
        var enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        void ReadEnabled(string file)
        {
            if (ReadObject(file)?["enabledPlugins"] is not JsonObject e) return;
            foreach (var (k, v) in e)
                if (v is JsonValue jv && jv.TryGetValue<bool>(out var b)) enabled[k] = b;
        }
        ReadEnabled(Path.Combine(dir, "settings.json"));
        foreach (var r in repos)
        {
            ReadEnabled(Path.Combine(r, ".claude", "settings.json"));
            ReadEnabled(Path.Combine(r, ".claude", "settings.local.json"));
        }

        var roots = new List<(string Key, string Root)>();
        if (ReadObject(Path.Combine(dir, "plugins", "installed_plugins.json"))?["plugins"] is JsonObject installed)
            foreach (var (key, entries) in installed)
            {
                if (!enabled.TryGetValue(key, out var on) || !on) continue;
                foreach (var e in entries as JsonArray ?? [])
                {
                    if ((string?)e?["installPath"] is not { Length: > 0 } root) continue;
                    var scope = (string?)e?["scope"] ?? "user";
                    if (scope != "user" && (string?)e?["projectPath"] is { } pp && !repos.Any(r => SamePath(r, pp) || IsUnder(r, pp))) continue;
                    roots.Add((key, root));
                    break;
                }
            }
        var skills = Path.Combine(dir, "skills");
        try
        {
            if (Directory.Exists(skills))
                foreach (var d in Directory.EnumerateDirectories(skills))
                    if (File.Exists(Path.Combine(d, ".claude-plugin", "plugin.json")))
                    {
                        var name = (string?)ReadObject(Path.Combine(d, ".claude-plugin", "plugin.json"))?["name"] ?? Path.GetFileName(d);
                        var key = $"{name}@skills-dir";
                        if (enabled.TryGetValue(key, out var on) && !on) continue;
                        roots.Add((key, d));
                    }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        foreach (var (key, root) in roots)
        {
            var at = key.IndexOf('@');
            var plugin = at > 0 ? key[..at] : key;
            var data = Path.Combine(dir, "plugins", "data", key.Replace('@', '-'));
            var manifest = ReadObject(Path.Combine(root, ".claude-plugin", "plugin.json"));
            var found = new List<(string, JsonObject)>();
            switch (manifest?["mcpServers"])
            {
                case JsonObject inline: found.AddRange(ServersIn(inline)); break;
                case JsonValue v when v.TryGetValue<string>(out var rel): found.AddRange(ServersIn(ReadObject(Path.Combine(root, rel)))); break;
            }
            if (found.Count == 0) found.AddRange(ServersIn(ReadObject(Path.Combine(root, ".mcp.json"))));
            foreach (var (name, def) in found)
                yield return (plugin, name, (JsonObject)Fill(def, root, data));
        }
    }

    static bool IsUnder(string folder, string parent)
    {
        try { return Path.GetFullPath(folder).StartsWith(Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    /// <summary>A plugin's .mcp.json: {"mcpServers": {…}}, or the servers straight at the top.</summary>
    static IEnumerable<(string, JsonObject)> ServersIn(JsonObject? file) =>
        file is null ? [] : Servers(file["mcpServers"] is JsonObject o ? o : file);

    static JsonNode Fill(JsonNode node, string root, string data) => node switch
    {
        JsonObject o => new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, kv.Value is null ? null : Fill(kv.Value, root, data)))),
        JsonArray a => new JsonArray(a.Select(x => x is null ? null : Fill(x, root, data)).ToArray()),
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(s
            .Replace("${CLAUDE_PLUGIN_ROOT}", root.Replace('\\', '/'))
            .Replace("${CLAUDE_PLUGIN_DATA}", data.Replace('\\', '/'))),
        _ => node.DeepClone(),
    };
}
