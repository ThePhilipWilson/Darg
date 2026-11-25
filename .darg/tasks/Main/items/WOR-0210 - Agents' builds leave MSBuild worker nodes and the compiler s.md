# WOR-0210: Agents' builds leave MSBuild worker nodes and the compiler server running

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** memory
- **Created:** 2026-11-25
- **Done:**

## Description

The ".NET Host" entries in Task Manager are dotnet.exe, and on 2026-11-25 (WOR-0207) they were build tooling, not Darg: MSBuild worker nodes started with /nodeReuse:true that outlive the dotnet build that started them (4 per build, 60 to 540 MB each, parent gone), the Roslyn compiler server VBCSCompiler (1.2 GB at peak), and dotnet test / vstest. With several agents building in their own worktrees at once, dotnet peaked at 14 processes and 3.2 GB, and sat at 0.8 to 1.1 GB between builds. Darg itself was flat over five days (400 to 900 MB, perf.log).

Proposal to measure, not assume: run agents' builds without node reuse (dotnet build -nodeReuse:false in tools/test.ps1, or MSBUILDDISABLENODEREUSE=1 in the environment Darg gives agents) and compare a suite's build time and the dotnet memory left after it. Leave the compiler server alone unless the numbers say otherwise: it makes every build faster and idles out by itself.

**Where:** tools/test.ps1 (the build step), src/Darg.Core/Agent.cs (AgentSession.Start environment).

**Done when:** a before and after of build time and of dotnet processes left 1 minute after a suite is written into this card, and the change is made if it frees the workers for less than a few seconds per build, or the card says why not.
