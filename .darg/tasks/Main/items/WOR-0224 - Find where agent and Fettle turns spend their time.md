# WOR-0224: Find where agent and Fettle turns spend their time

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

## Description

> Continue working out why the Claude side of the app is slow: agent sessions and Fettle replies taking a long time to start, stream or finish. Separate time spent in Darg (spawning the process, session resume, transcript reading and parsing, passing messages over IPC, UI updates as output streams in) from time spent in Claude itself (model, effort, context size, tool calls). Related to WOR-0223 and WOR-0161. Done when: the slow steps are measured with timings, the cause of the delay is identified for each, and fixes are suggested or made into cards.

WOR-0206 timed one short turn and found the Haiku model pick was the biggest slice; WOR-0207 then started the pick with no MCP servers. This card picks up from there with real use: read the day's `agents.log` across all agents and Fettle chats, split each slow turn into Darg's time and Claude's time, and find the cause of the biggest Darg slices. Not app-wide UI lag (that's WOR-0223).

**Where:**
- The data: `agents.log` in the instance folder (`%APPDATA%\Darg\home\agents.log`; about 6,000 lines and 70-odd turns from 2026-11-25 alone), written by `AgentTimings` (`src/Darg.Core/AgentTimings.cs`), started in `AgentViewModel` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, around line 1123), which Fettle's chats use too (`MascotViewModel`). Each "Turn done" line in a transcript carries the sum.
- The model pick: `ModelProfiles` and `Haiku.Ask` / `Haiku.Args` (`src/Darg.Core/Haiku.cs`). It still takes 4.5 to 7 s per new agent in today's log.
- Darg's side of a turn: `AgentSession` and `StreamParser` (`src/Darg.Core/Agent.cs`); transcript updates on the UI thread show in `stalls.log` and `perf.log` beside agents.log.
- `tools/perf/analyze.ps1` summarises perf.log and stalls.log but not agents.log.

**Done when:**
- A summary of agents.log (a script, or a part added to `tools/perf/analyze.ps1`) gives per turn: model pick, process start to init, time to first reply, time to first tool, turn total. It also lists which agents were Fettle chats, which were resumed sessions and which were new, and the slowest turns are ranked.
- For the slowest turns and for typical Fettle replies, a table on this card splits the time into Darg (pick, spawn, hooks, resume, gaps between Claude's events and the transcript showing them) and Claude (model and effort, tool time, permission waits).
- Each Darg slice over about a second has a named cause and a suggested fix. Fixes worth doing are made into cards and linked here.
- The `system thinking_tokens` lines (about 60% of the log) are either dropped from agents.log or their keeping is justified, so the log stays readable.

**Questions:**
- Whether streaming feels slow (text arriving in bursts in the transcript) isn't in agents.log yet. If the table can't answer it, adding a "first text shown" line is a fair first fix.
