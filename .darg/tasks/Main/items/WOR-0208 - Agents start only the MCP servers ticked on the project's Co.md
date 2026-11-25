# WOR-0208: Agents start only the MCP servers ticked on the project's Context page

- **Status:** Backlog
- **Priority:**
- **Type:** Story
- **Labels:** memory
- **Created:** 2026-11-25
- **Done:**

## Description

Every agent Darg starts loads every MCP server in the person's user-scope config and plugins. On 2026-11-25 that was five servers per session (three through npx, one through uvx, plus the claude-draw plugin): about 1 GB and 7 node processes per agent, 56 node processes and ~8 GB with 8 agents running (WOR-0207's measurements). None of the npx/uvx ones had ever been called from any transcript on disk.

Philip's call (2026-11-25, on WOR-0207): MCP servers are opt-in per project, exclusively, picked on the project's Context page; Unity servers are pre-ticked for Unity projects (a repo with a ProjectSettings/ folder).

- Agents (card agents, chats, flow stages, planners, Fettle's per-project chats) start with --strict-mcp-config plus an --mcp-config Darg writes holding only the ticked servers. The project's own .mcp.json servers are listed and ticked by default (the project asked for them).
- The Context page lists the servers it can offer (the user-scope ones from ~/.claude.json, plugin ones such as claude-draw, the project's .mcp.json) with a tick each, saved per project in instance.json. Darg keeps each server's definition so it still works once removed from ~/.claude.json.
- Haiku one-shots already start none (WOR-0207, Haiku.Args).
- claude-draw: decide whether it is ticked by default, since Draw sends the agent a message expecting its plugin tools.

**Where:** src/Darg.Core/Agent.cs (AgentSession.Start args), src/Darg.Desktop/ViewModels/AgentsViewModel.cs (Begin), ContextViewModel / ContextView (WOR-0145), InstanceConfig.

**Done when:** an agent in a project with nothing ticked starts no node, uvx or python children (checked live in Task Manager or a process listing); ticking a server on the Context page gives the next agent that server's tools; a Unity project shows its Unity servers pre-ticked; an integration test checks the args (fake claude's FAKE_LOG) for --strict-mcp-config and an --mcp-config holding just the ticked servers.
