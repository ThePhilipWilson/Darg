# WOR-0208: Agents start only the MCP servers ticked on the project's Context page

- **Status:** QA Ready
- **Priority:**
- **Type:** Story
- **Labels:** memory
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 03:50:48

## Description

Every agent Darg starts loads every MCP server in the person's user-scope config and plugins. On 2026-11-25 that was five servers per session (three through npx, one through uvx, plus the claude-draw plugin): about 1 GB and 7 node processes per agent, 56 node processes and ~8 GB with 8 agents running (WOR-0207's measurements). None of the npx/uvx ones had ever been called from any transcript on disk.

Philip's call (2026-11-25, on WOR-0207): MCP servers are opt-in per project, exclusively, picked on the project's Context page; Unity servers are pre-ticked for Unity projects (a repo with a ProjectSettings/ folder).

- Agents (card agents, chats, flow stages, planners, Fettle's per-project chats) start with --strict-mcp-config plus an --mcp-config Darg writes holding only the ticked servers. The project's own .mcp.json servers are listed and ticked by default (the project asked for them).
- The Context page lists the servers it can offer (the user-scope ones from ~/.claude.json, plugin ones such as claude-draw, the project's .mcp.json) with a tick each, saved per project in instance.json. Darg keeps each server's definition so it still works once removed from ~/.claude.json.
- Haiku one-shots already start none (WOR-0207, Haiku.Args).
- claude-draw: decide whether it is ticked by default, since Draw sends the agent a message expecting its plugin tools.

**Where:** src/Darg.Core/Agent.cs (AgentSession.Start args), src/Darg.Desktop/ViewModels/AgentsViewModel.cs (Begin), ContextViewModel / ContextView (WOR-0145), InstanceConfig.

**Done when:** an agent in a project with nothing ticked starts no node, uvx or python children (checked live in Task Manager or a process listing); ticking a server on the Context page gives the next agent that server's tools; a Unity project shows its Unity servers pre-ticked; an integration test checks the args (fake claude's FAKE_LOG) for --strict-mcp-config and an --mcp-config holding just the ticked servers.

## How to check

**What changed** (commit [9194d29](https://github.com/ThePhilipWilson/Darg/commit/9194d29) on `android`, pushed): every agent Darg starts (card agents, chats, flow stages, planners, Fettle's chats) now gets `--strict-mcp-config`, plus `--mcp-config <instance>/mcp/<project>.json` holding only the servers ticked for its project. The Context page has a new "MCP servers agents start" list at the top of its left column, a tick per server, saved at once as the project's `mcp` in instance.json (`on` keeps each ticked server's definition, so it still starts after you remove it from ~/.claude.json; `off` names default-on ones you unticked). The flags are read as each session starts, so a tick reaches a resumed agent too.

**On by default** (each row says why): the project's own `.mcp.json` servers and any `claude mcp add` local-scope ones for its folders; Unity servers (`unity` or `coplay` in the name, command, args or url) in a project with a `ProjectSettings/` repo; and **claude-draw, a choice I made for you**: ✎ Draw sends the agent a message expecting its tools, so I left it on. It costs one node process per agent. Untick it on any project's Context page if you'd rather it went.

**To try:** update (⟳) or `pwsh -File tools/publish.ps1` from a terminal, then:
1. Open a project's Context section. Your ~/.claude.json servers should be listed unticked, claude-draw ticked. In a Unity project, unity-mcp and coplay-mcp should already be ticked.
2. Start an agent and look at its process tree (Task Manager, or `Get-CimInstance Win32_Process`): no node/npx/uvx/python children except claude-draw's one node. Untick claude-draw and start another: none at all.
3. Tick a server, start an agent, ask it to list its `mcp__` tools: it should have that server's.

**Checked here:** unit tests `McpServersTests` (5), integration `McpPickTests` (2: Context ticks → the next agent's `--mcp-config`, and a Fettle chat), both failing with the launch hook removed. Live with the real claude on a throwaway instance: nothing ticked gave `--strict-mcp-config` alone and no node, uvx or python children (only your hooks' bash/powershell); claude-draw ticked gave one node and the agent listed `mcp__claude-draw__collect_drawing, draw_status, open_canvas, request_drawing`. Not checked: the page in the real app (headless picture below), a real Unity project, and Draw end to end with the new tool prefix (`mcp__claude-draw__` instead of `mcp__plugin_claude-draw_claude-draw__`; Draw's message and the skill name the tools without a prefix, so it should be fine).

![Context page with MCP ticks](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0208-context-mcp.png)
