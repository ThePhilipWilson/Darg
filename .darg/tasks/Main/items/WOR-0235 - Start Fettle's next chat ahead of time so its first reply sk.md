# WOR-0235: Start Fettle's next chat ahead of time so its first reply skips start-up

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:** 2026-11-27
- **Moved:** 2026-11-27 03:45:00

## Description

From WOR-0224: in 25 Nov's agents.log, 13 of 23 Fettle turns were a new chat's first. Those waited a median 14.5 s, first reply at 6.4 s, of which about 2.2 s was Claude Code starting up (node, the person's five SessionStart hooks, about 1.1 s, and MCP servers) before the model was even asked; a chat's later turns had their first reply at 2.0 s. Fettle's model pick is a rule (0.00 s), so start-up is the one Darg-side slice left in a new chat.

Claude Code starts up and answers `initialize` before any message arrives (checked on 2.1.326), so a session started ahead and kept idle is ready the moment a message comes.

**Where:** `MascotViewModel` (`src/Darg.Desktop/ViewModels/MascotViewModel.cs`, `Ask`, `Fresh`, `PhoneSay`, `Args`, `McpFor`) and `AgentViewModel` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `Begin`): keep one spare Fettle session per project (its args and MCP set as the next chat's would be), hand it to the next new chat, and start another. Leave the phone's chat and resumes alone. Only while the app is open; the spare dies with it (`ChildProcesses`).

**Done when:**
- A new Fettle chat's first message goes to an already started session when one is ready, and to a fresh one otherwise, and its first turn's Startup in `pwsh -File tools/perf/agents.ps1` drops to about 0.
- A spare whose project's MCP ticks or Fettle briefing changed is replaced, not used.
- At most one spare per project, none for a project not open, and it costs no model call (nothing is sent to it).
- An integration test with fake claude checks a new chat uses the spare (one process start, not two) and fails on the old code.
