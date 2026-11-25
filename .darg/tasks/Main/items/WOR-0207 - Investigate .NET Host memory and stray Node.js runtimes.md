# WOR-0207: Investigate .NET Host memory and stray Node.js runtimes

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-25
- **Done:** 2026-11-25
- **Moved:** 2026-11-25 02:59:00

## Description

Philip reports the .NET Host process using a ton of memory, and a load of Node.js runtimes running alongside Darg. Find out which processes these are (Darg itself, its agents, MCP servers or hooks), how much memory each uses, whether they leak or pile up over time (for example one node process per agent or per MCP server that never exits), and what spawns them. Report findings and propose fixes. Done when the source of the memory use and the extra Node.js runtimes is identified and a fix or follow-up card exists.

## Findings

Measured live on 2026-11-25 between 02:28 and 02:58 (process listings with parent chains, sampled every 20 s; Darg's perf.log since 2026-11-20).

- **The node processes are MCP servers, started by every Claude session.** Each agent's claude.exe starts the person's user-scope servers and the claude-draw plugin: three through `npx` (a 78 MB npx wrapper node plus the server's own node each), one through `uvx` (uv plus python), and claude-draw's node. That's 7 node processes and about 1 GB per session, on top of claude's own ~0.5 GB. With 8 agents running: 56 node processes and ~12 GB for claude plus its servers. None of the npx/uvx servers had ever been called in any transcript on disk. No orphans: every server's claude parent was alive, and they go when it does. The other 13 node processes on the machine (node20.exe) are a desk controller app's plugins, nothing to do with Darg.
- **Haiku one-shots started the whole set too**, for a one-word answer with no tools. Fixed: `Haiku.Args` adds `--strict-mcp-config` (48ed882). Live check with the real CLI: peak child memory 1386 MB to 327 MB (hooks only), no node, 8.6 s to 6.5 s.
- **Agents keep their process and servers while idle.** An idle chat (since 01:52) still held its whole tree an hour later.
- **".NET Host" is dotnet.exe, and it's build tooling, not Darg.** It's MSBuild worker nodes (`/nodeReuse:true`, 4 per build, 60 to 540 MB each) that outlive the build that started them, the Roslyn compiler server (VBCSCompiler, up to 1.2 GB) and dotnet test / vstest. Peak 14 processes and 3.2 GB with agents building in parallel, 0.8 to 1.1 GB between builds. The integration suite's test host peaks at 1.9 to 2.6 GB (CLAUDE.md says ~1.6 GB).
- **Darg itself doesn't leak:** 400 to 900 MB working set over five days in perf.log, flat between bursts of work.

Follow-ups: [WOR-0208](card:WOR-0208) agents start only the MCP servers ticked on the project's Context page (Philip's call: exclusively opt-in per project, Unity servers pre-ticked for Unity projects); [WOR-0209](card:WOR-0209) close an idle agent's process and resume it on its next message; [WOR-0210](card:WOR-0210) measure turning MSBuild node reuse off for agents' builds; [WOR-0211](card:WOR-0211) the TerminalReadTests failure every QA pass has named.
