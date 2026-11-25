# WOR-0221: A card agent cut off mid-turn resumes when Darg reopens

- **Status:** Backlog
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**

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
