using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Darg.Core;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0208: agents start with --strict-mcp-config and an --mcp-config holding only the servers ticked on the
/// project's Context page; ticking one there reaches the next agent.</summary>
public class McpPickTests
{
    /// <summary>The arguments of each claude started so far, oldest first.</summary>
    static List<List<string>> Starts(DargApp app) => app.ClaudeCalls().Where(c => (string?)c["kind"] == "start")
        .Select(c => c["args"]!.AsArray().Select(a => (string)a!).ToList()).ToList();

    /// <summary>Start an agent and wait for its claude: its args, and the servers its --mcp-config file named as it started
    /// (null: no --mcp-config). The file is the project's, rewritten by the next start, so it's read straight away.</summary>
    static async Task<(List<string> Args, List<string>? Servers)> StartOne(DargApp app, string prompt)
    {
        var before = Starts(app).Count;
        await app.Api("start-agent", new { project = "proj", prompt });
        await app.Until(() => Starts(app).Count > before, $"the agent for \"{prompt}\" to start");
        var args = Starts(app)[before];
        var at = args.IndexOf("--mcp-config");
        var servers = at < 0 ? null : JsonNode.Parse(File.ReadAllText(args[at + 1]))!["mcpServers"]!.AsObject().Select(kv => kv.Key).ToList();
        return (args, servers);
    }

    [AvaloniaFact]
    public async Task Agents_start_only_the_servers_ticked_on_the_context_page()
    {
        using var app = DargApp.Make();
        // the project's own .mcp.json (ticked unless unticked) and one of the person's (offered, not ticked)
        File.WriteAllText(Path.Combine(app.Repo, ".mcp.json"), """{ "mcpServers": { "repo-tool": { "command": "node", "args": ["tool.js"] } } }""");
        var claudeJson = McpServers.Sources.Here().ClaudeJson; // the sandbox's, never the real ~/.claude.json
        Assert.StartsWith(Path.GetFullPath(Sandbox.Root), Path.GetFullPath(claudeJson), StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(claudeJson, """{ "mcpServers": { "design-tool": { "command": "npx", "args": ["-y", "example-design-mcp"] } } }""");
        try
        {
            await app.Start();

            var (args, servers) = await StartOne(app, "first agent");
            Assert.Contains("--strict-mcp-config", args);
            Assert.Equal(["repo-tool"], servers);

            await app.Api("show", new { project = "proj", section = "Context" });
            var ctx = app.Project.Context;
            await app.Until(() => ctx.Servers.Any(s => s.Name == "design-tool"), "the person's server listed on the Context page");
            Assert.True(ctx.Servers.Single(s => s.Name == "repo-tool").On);
            Assert.False(ctx.Servers.Single(s => s.Name == "design-tool").On);
            app.Shot("context-mcp");

            ctx.Servers.Single(s => s.Name == "design-tool").On = true;
            ctx.Servers.Single(s => s.Name == "repo-tool").On = false;
            // saved in instance.json, definition and all
            var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(app.Home, "instance.json")))!["projects"]![0]!["mcp"]!;
            Assert.Equal("npx", (string?)saved["on"]!["design-tool"]!["command"]);
            Assert.Equal("repo-tool", (string?)saved["off"]![0]);

            (args, servers) = await StartOne(app, "second agent");
            Assert.Contains("--strict-mcp-config", args);
            Assert.Equal(["design-tool"], servers);

            ctx.Servers.Single(s => s.Name == "design-tool").On = false;
            (args, servers) = await StartOne(app, "third agent");
            Assert.Contains("--strict-mcp-config", args);
            Assert.Null(servers); // nothing ticked: no servers at all
            Assert.Equal("", app.Crashes());
        }
        finally { File.Delete(claudeJson); }
    }

    [AvaloniaFact]
    public async Task A_fettle_chat_starts_its_projects_ticked_servers()
    {
        using var app = DargApp.Make();
        File.WriteAllText(Path.Combine(app.Repo, ".mcp.json"), """{ "mcpServers": { "repo-tool": { "command": "node", "args": ["tool.js"] } } }""");
        await app.Start();

        var fettle = app.Main.Mascot;
        fettle.Compose = "what's on the board?";
        fettle.SendCommand.Execute(null);
        await app.Until(() => Starts(app).Count > 0, "Fettle's process to start");
        var args = Starts(app)[0];
        Assert.Contains("--strict-mcp-config", args);
        var servers = JsonNode.Parse(File.ReadAllText(args[args.IndexOf("--mcp-config") + 1]))!["mcpServers"]!.AsObject().Select(kv => kv.Key);
        Assert.Equal(["repo-tool"], servers);
    }
}
