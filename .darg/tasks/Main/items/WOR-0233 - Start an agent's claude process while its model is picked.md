# WOR-0233: Start an agent's claude process while its model is picked

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

## Description

From WOR-0224: a new card agent waits for its model pick before its claude process even starts, then waits again for Claude Code to start up (node, the person's SessionStart hooks, MCP servers). The two run one after the other: in 25 Nov's agents.log the pick took a median 5.2 s (up to 18 s when several agents started at once) and start-up a median 1.9 s more (up to 7 s). WOR-0224 cut the pick itself to about 2 s; this overlaps what is left.

Claude Code starts up (runs its SessionStart hooks, answers `initialize`) before any message arrives, and takes `set_model` and `apply_flag_settings` before the first message: checked by hand on Claude Code 2.1.326 (hooks done and `initialize` answered at 1.55 s with nothing sent; `set_model sonnet` sent at 6 s, then the message, and its `system init` said claude-sonnet-5-5).

**Where:** `AgentViewModel.StartOnProfile` and `Begin` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs`): start the session at once on the person's own settings, hold the first message, and when the pick lands send `AgentSession.SetModel` / `SetEffort` (with Optimise tokens applied, `TokenSaver`) and then the message. `AgentTimings` already logs both ends, and `pwsh -File tools/perf/agents.ps1` shows Pick and Startup per turn.

**Done when:**
- A new agent with a Haiku pick starts its claude process before the pick ends, and its first message goes only after the picked model and effort were sent.
- A hand-picked model or effort made during the pick still wins (as now), and an agent closed during the pick ends its process.
- An integration test with fake claude checks the order (process start, then pick end, then set_model, then the message) and fails on the old code.
- agents.ps1 on a day's log shows a first turn's Waited lower by about the Startup column for new card agents.
