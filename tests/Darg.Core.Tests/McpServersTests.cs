using System.Text.Json.Nodes;
using Darg.Core;

namespace Darg.Core.Tests;

/// <summary>WOR-0208: agents start only the MCP servers ticked for their project.</summary>
public sealed class McpServersTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("darg-mcp-").FullName;
    readonly string _claudeDir, _repo, _instance;
    readonly McpServers.Sources _from;

    public McpServersTests()
    {
        _claudeDir = Directory.CreateDirectory(Path.Combine(_root, "user", ".claude")).FullName;
        _repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        _instance = Directory.CreateDirectory(Path.Combine(_root, "instance")).FullName;
        _from = new McpServers.Sources(_claudeDir, Path.Combine(_root, "user", ".claude.json"));
        // the person's: two npx ones, a Unity editor one on localhost and coplay through uvx
        Write(_from.ClaudeJson, new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["design-tool"] = new JsonObject { ["command"] = "npx", ["args"] = new JsonArray("-y", "example-design-mcp") },
                ["google-sheets"] = new JsonObject { ["type"] = "stdio", ["command"] = "npx", ["args"] = new JsonArray("-y", "mcp-google-sheets"), ["env"] = new JsonObject { ["TOKEN"] = "secret" } },
                ["unity-mcp"] = new JsonObject { ["type"] = "http", ["url"] = "http://localhost:8080" },
                ["coplay-mcp"] = new JsonObject { ["type"] = "stdio", ["command"] = "uvx", ["args"] = new JsonArray("coplay-mcp-server@latest") },
            },
            ["projects"] = new JsonObject
            {
                [_repo.Replace('\\', '/')] = new JsonObject { ["mcpServers"] = new JsonObject { ["github"] = new JsonObject { ["type"] = "http", ["url"] = "https://api.example.com/mcp/" } } },
                [Path.Combine(_root, "elsewhere")] = new JsonObject { ["mcpServers"] = new JsonObject { ["other"] = new JsonObject { ["type"] = "http", ["url"] = "https://other.example.com" } } },
            },
        });
        // claude-draw is a plugin folder in ~/.claude/skills, as it is installed here
        var draw = Directory.CreateDirectory(Path.Combine(_claudeDir, "skills", "claude-draw")).FullName;
        Write(Path.Combine(draw, ".claude-plugin", "plugin.json"), new JsonObject { ["name"] = "claude-draw" });
        Write(Path.Combine(draw, ".mcp.json"), new JsonObject
        {
            ["mcpServers"] = new JsonObject { ["claude-draw"] = new JsonObject { ["command"] = "node", ["args"] = new JsonArray("${CLAUDE_PLUGIN_ROOT}/server/mcp.js") } },
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
    }

    static void Write(string file, JsonNode json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, json.ToJsonString());
    }

    ProjectConfig Project() => new() { Id = "proj", Name = "Proj", Repos = [_repo] };

    McpServer Server(ProjectConfig p, string name) => McpServers.Offered(p, _from).Single(s => s.Name == name);

    [Fact]
    public void The_persons_servers_are_offered_but_off_and_the_projects_and_claude_draw_are_on()
    {
        Write(Path.Combine(_repo, ".mcp.json"), new JsonObject { ["mcpServers"] = new JsonObject { ["repo-tool"] = new JsonObject { ["command"] = "node", ["args"] = new JsonArray("tool.js") } } });
        var p = Project();
        var offered = McpServers.Offered(p, _from);

        Assert.Equal(["github", "repo-tool", "design-tool", "google-sheets", "unity-mcp", "coplay-mcp", "claude-draw"], offered.Select(s => s.Name));
        Assert.DoesNotContain(offered, s => s.Name == "other"); // another folder's
        Assert.Equal(["github", "repo-tool", "claude-draw"], McpServers.Chosen(p, _from).Keys);
        // the plugin's own folder is filled in, as only a plugin load would
        var draw = (string)McpServers.Chosen(p, _from)["claude-draw"]["args"]![0]!;
        Assert.Equal(Path.Combine(_claudeDir, "skills", "claude-draw").Replace('\\', '/') + "/server/mcp.js", draw);
        // the tooltip never shows env (tokens)
        Assert.DoesNotContain("secret", Server(p, "google-sheets").Runs);
    }

    [Fact]
    public void A_unity_project_has_its_unity_servers_ticked()
    {
        var p = Project();
        Assert.DoesNotContain("unity-mcp", McpServers.Chosen(p, _from).Keys);
        Directory.CreateDirectory(Path.Combine(_repo, "ProjectSettings"));

        var chosen = McpServers.Chosen(p, _from).Keys.ToList();
        Assert.Contains("unity-mcp", chosen);
        Assert.Contains("coplay-mcp", chosen);
        Assert.DoesNotContain("design-tool", chosen);
    }

    [Fact]
    public void Ticks_are_kept_with_the_definition_and_unticking_a_default_one_turns_it_off()
    {
        var p = Project();
        McpServers.Tick(p, Server(p, "design-tool"), true);
        McpServers.Tick(p, Server(p, "claude-draw"), false);

        Assert.Equal(["github", "design-tool"], McpServers.Chosen(p, _from).Keys);
        Assert.Equal(["claude-draw"], p.Mcp!.Off);

        // gone from ~/.claude.json: Darg's copy still starts
        var json = JsonNode.Parse(File.ReadAllText(_from.ClaudeJson))!.AsObject();
        json["mcpServers"]!.AsObject().Remove("design-tool");
        Write(_from.ClaudeJson, json);
        var row = McpServers.Rows(p, _from).Single(r => r.Server.Name == "design-tool");
        Assert.True(row.Kept);
        Assert.True(row.On);
        Assert.Equal("npx", (string?)McpServers.Chosen(p, _from)["design-tool"]["command"]);

        // and it survives a save and a load of instance.json
        var config = new InstanceConfig { Projects = [p] };
        config.Save(_instance);
        var back = InstanceConfig.Load(_instance).Projects.Single();
        Assert.Equal(["github", "design-tool"], McpServers.Chosen(back, _from).Keys);
    }

    [Fact]
    public void Launch_args_name_a_file_with_just_the_ticked_servers_or_none_when_nothing_is()
    {
        var p = Project();
        foreach (var s in McpServers.Offered(p, _from).Where(s => s.DefaultOn)) McpServers.Tick(p, s, false);

        Assert.Equal(["--strict-mcp-config"], McpServers.LaunchArgs(p, _instance, _from));
        Assert.Equal(["--strict-mcp-config"], McpServers.LaunchArgs(null, _instance, _from));

        McpServers.Tick(p, Server(p, "google-sheets"), true);
        var args = McpServers.LaunchArgs(p, _instance, _from);
        Assert.Equal(3, args.Count);
        Assert.Equal(["--strict-mcp-config", "--mcp-config"], args.Take(2));
        var servers = JsonNode.Parse(File.ReadAllText(args[2]))!["mcpServers"]!.AsObject();
        Assert.Equal(["google-sheets"], servers.Select(kv => kv.Key));
        Assert.Equal("secret", (string?)servers["google-sheets"]!["env"]!["TOKEN"]); // the server still gets its env
    }

    [Fact]
    public void An_installed_plugin_counts_only_when_enabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(_claudeDir, "plugins", "cache", "m", "helper", "1.0")).FullName;
        Write(Path.Combine(root, ".claude-plugin", "plugin.json"), new JsonObject
        {
            ["name"] = "helper", ["mcpServers"] = new JsonObject { ["helper-srv"] = new JsonObject { ["command"] = "node", ["args"] = new JsonArray("${CLAUDE_PLUGIN_ROOT}/x.js", "${CLAUDE_PLUGIN_DATA}") } },
        });
        Write(Path.Combine(_claudeDir, "plugins", "installed_plugins.json"), new JsonObject
        {
            ["version"] = 2,
            ["plugins"] = new JsonObject { ["helper@m"] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = root }) },
        });
        var p = Project();
        Assert.DoesNotContain(McpServers.Offered(p, _from), s => s.Name == "helper-srv");

        Write(Path.Combine(_claudeDir, "settings.json"), new JsonObject { ["enabledPlugins"] = new JsonObject { ["helper@m"] = true, ["claude-draw@skills-dir"] = false } });
        var helper = Server(p, "helper-srv");
        Assert.False(helper.DefaultOn);
        Assert.Equal("plugin helper", helper.Source);
        Assert.EndsWith("plugins/data/helper-m", (string)helper.Definition["args"]![1]!);
        Assert.DoesNotContain(McpServers.Offered(p, _from), s => s.Name == "claude-draw"); // turned off in settings
    }
}
