# WOR-0209: Close an idle agent's claude process and resume it on its next message

- **Status:** Done
- **Priority:**
- **Type:** Story
- **Labels:** memory
- **Created:** 2026-11-25
- **Done:** 2026-11-25
- **Moved:** 2026-11-25 04:22:47

## Description

An agent Darg lists keeps its claude process, and that process's MCP servers, alive between turns for as long as Darg runs: about 0.5 GB for claude plus ~1 GB of MCP servers each (WOR-0207). On 2026-11-25 an agent left idle since 01:52 (a chat) still held its whole tree an hour later, and agents waiting in QA do the same.

Darg already resumes saved agents with --resume <session> (AgentsViewModel.Begin), so an idle one can give its process back and come back on the next message.

- After a turn ends and the agent sits idle (no pending question or permission ask, not waiting on a usage reset) for a set time (say 10 minutes, a setting), end its session as Shut/End do but keep it listed, in a state that reads as resting, not finished.
- The next message, Work on, Send back or a decision answer resumes it with --resume, as a saved agent is resumed after a restart.
- Never for Fettle's live chat while the person is typing in it.

**Where:** src/Darg.Desktop/ViewModels/AgentsViewModel.cs (End, Begin, State), src/Darg.Core/Agent.cs.

**Done when:** an agent idle past the limit has no claude process (process listing), still shows in the Agents list with its transcript, and a message to it resumes the same session (same session id, earlier context remembered); an integration test with fake claude covers end-after-idle and resume-on-message, with the idle limit shortened.
