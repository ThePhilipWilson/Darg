# WOR-0221: A card agent cut off mid-turn resumes when Darg reopens

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-26 10:39:35

## Description

> Cards sitting in the In Progress column that previously had a running agent are stopping, and they do not seem to restart correctly afterwards. Investigate why the agent stops and why resuming or restarting it on these cards fails or does nothing.

(The card as first written, 2026-11-25. Philip's pick on 2026-11-25: resume after a restart.)

When Darg closes, crashes or ⟳ Updates while a work or rework agent is mid-turn on a card, the agent should pick up where it was once Darg opens again, on its own session, with no click. Today it comes back `Saved`, WOR-0183's `TaskOver` counts it as stopped and forgets it at once, and the card sits in In Progress with no agent. ▶ Work on then starts a new agent that remembers nothing of the turn that was cut off.

**Where:** startup and the Agents list.
- `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`: `TaskOver` / `Stopped` / `IsTask` (they forget a mid-turn task agent), `FinishedTurn` (tells a cut-off turn from a finished one), `AgentViewModel`'s load from its record (sets `State = Saved`; the usage-limit `ContinueAt` / `WaitFor` path there already carries a saved agent on by itself, so copy that), `Begin(…, resume: true)`, `CarryOnMessage`.
- `src/Darg.Core/AgentStore.cs`: the saved record, if it needs a flag saying the turn was cut off.
- `tests/Darg.Desktop.Tests/ReplacedAgentTests.cs`: the WOR-0183 cases (line ~97, ~133) that expect a cut-off task agent to go. They change for work and rework agents on a card still in its working column.

**Done when:**
- An integration test saves a work agent mid-turn (fake claude, its transcript stopping after a tool call), reopens the app, and the agent stays in the list and resumes the same session id with a carry-on message, its transcript noting why. The card stays In Progress. Breaking the resume makes the test fail.
- The same for a rework agent started by ↩ Send back.
- A cut-off agent whose card has left the working column since (QA, done, attention), or that never got past its prompt with no session, is still forgotten as WOR-0183 says.
- An agent that is cut off again on its resumed turn is resumed once more at most: a second restart leaves it listed as saved, so a crash loop can't keep restarting it.
- `pwsh -File tools/test.ps1` passes.

**Questions left:**
- QA agents cut off mid-turn: resume them too, or leave them forgotten (Run QA starts a fresh one cheaply)? Default here: resume only work and rework.
- AGE-0002 (a resume Claude refuses starts fresh from the transcript) covers the case where the session can't be resumed; until it lands, a refused resume shows as failed and stays listed.

## How to check

**What changed** (commit `b3d3b8a` on `android`, pushed): when Darg opens, a work or rework agent whose saved transcript stops mid-turn, with a session, on a card still in In Progress or needs more work, now resumes its own session by itself. Its transcript gets "Cut off mid-turn when Darg last closed: resuming its session to carry on." and a "↻ Carry on (Darg closed mid-turn)" message. Darg resumes it once per cut-off: if it is cut off again on that resumed turn, the next start leaves it listed as saved, with a note saying it wasn't resumed a second time. Sending it a message carries it on. These still go as WOR-0183 has it: QA and Improve agents (the default the card asked for), one with no session, one closed by Close or Fettle, one whose card has moved on to QA, attention, done or back to To Do, and an older one a newer agent on the card has replaced.

The cause was `TaskOver` (WOR-0183). It counted any stopped task agent as finished, and a reloaded agent is always `Saved`, so `RefreshCards` forgot it as Darg opened. `CutOffAgentTests` reproduced it: with the old load the work and rework agents were gone after a reopen.

**Try it** (the tests used fake claude; a real session killed mid-turn hasn't been tried):
1. Publish or run a build with `b3d3b8a`. On a throwaway card in To Do, press ▶ Work on with a task that takes a few minutes (e.g. "read every file under src/Darg.Core and list the public classes").
2. Once it has made a few tool calls, close Darg (or end it in Task Manager to play a crash), then open it again.
3. Look for: the agent still in the Agents list, working, on the same chat (its earlier transcript above the "↻ Carry on" line). The card stays in In Progress and the Home tile counts 1 running.
4. To see the crash-loop guard: close Darg again while that resumed turn is running, then reopen. The agent should be listed, saved, with "Cut off mid-turn again…", and it should not have started.

`pwsh -File tools/test.ps1 -Integration -Filter CutOffAgentTests` replays all of it with fake claude; `.shots/integration/A_work_agent_cut_off_mid_turn_resumes_its_own_session_when_Darg_reopens/resumed.png` is the window after the reopen.
