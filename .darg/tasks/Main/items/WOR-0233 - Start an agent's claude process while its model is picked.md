# WOR-0233: Start an agent's claude process while its model is picked

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-27 12:48:07

## Description

From WOR-0224: a new card agent waits for its model pick before its claude process even starts, then waits again for Claude Code to start up (node, the person's SessionStart hooks, MCP servers). The two run one after the other: in 25 Nov's agents.log the pick took a median 5.2 s (up to 18 s when several agents started at once) and start-up a median 1.9 s more (up to 7 s). WOR-0224 cut the pick itself to about 2 s; this overlaps what is left.

Claude Code starts up (runs its SessionStart hooks, answers `initialize`) before any message arrives, and takes `set_model` and `apply_flag_settings` before the first message: checked by hand on Claude Code 2.1.326 (hooks done and `initialize` answered at 1.55 s with nothing sent; `set_model sonnet` sent at 6 s, then the message, and its `system init` said claude-sonnet-5-5).

**Where:** `AgentViewModel.StartOnProfile` and `Begin` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs`): start the session at once on the person's own settings, hold the first message, and when the pick lands send `AgentSession.SetModel` / `SetEffort` (with Optimise tokens applied, `TokenSaver`) and then the message. `AgentTimings` already logs both ends, and `pwsh -File tools/perf/agents.ps1` shows Pick and Startup per turn.

**Done when:**
- A new agent with a Haiku pick starts its claude process before the pick ends, and its first message goes only after the picked model and effort were sent.
- A hand-picked model or effort made during the pick still wins (as now), and an agent closed during the pick ends its process.
- An integration test with fake claude checks the order (process start, then pick end, then set_model, then the message) and fails on the old code.
- agents.ps1 on a day's log shows a first turn's Waited lower by about the Startup column for new card agents.

## How to check

**What changed** (commits [b587ecc](https://github.com/ThePhilipWilson/Darg/commit/b587ecc) and [bd1c031](https://github.com/ThePhilipWilson/Darg/commit/bd1c031) on `android`): while Haiku picks a new agent's model, its claude now starts on your own settings with the first message held. When the pick lands, Darg sends `set_model` / `apply_flag_settings` (Optimise tokens applied), then the message. A rule's pick or a queue's named profile (WOR-0246) starts on the profile as before. A Haiku pick whose Hooks setting differs from what it started with restarts it once. An agent held past the project's cap (WOR-0245) starts no claude until it may.

**Try it:** update the installed copy (⟳ menu, or `pwsh -File tools/publish.ps1` from a terminal), work a day as usual, then run `pwsh -File tools/perf/agents.ps1 -Since <that morning>`.

**Look for:** in the "first turn, new" rows for card agents (kind work), Startup should be well under 25 Nov's 1.9 s median, and Waited lower by about that much. In agents.log, a new agent's `process start` comes before `model pick end`. A live run on a throwaway instance (real claude, Haiku picking Quick) had process start at 0.05 s, the SessionStart hooks at 1.3 to 2.0 s, pick end at 3.26 s, the message at 3.28 s, and 0.5 s of Startup left (one slow hook's tail); the transcript said Profile Quick, then Model now claude-haiku-5-5, then the reply.

**Also worth a glance:** set a card agent's model by hand while it still says "Picking a model…": it should run on your pick.
