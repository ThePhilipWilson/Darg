# WOR-0234: Show agent and Fettle replies as they are written

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

## Description

From WOR-0224: Darg starts claude without `--include-partial-messages`, so stream-json gives each assistant message only once it is whole. A paragraph, or a long reply, appears in the transcript all at once after the model has finished writing it, which reads as text arriving in bursts. agents.log can't show this: its "first reply" line is the first whole message.

**Where:** `AgentSession.Start` (`src/Darg.Core/Agent.cs`) adds the flag; `StreamParser` reads the `stream_event` lines (`content_block_delta` with `text_delta`) into a new event; `AgentViewModel.On` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs`) grows the last `TextItem` instead of adding one, and the whole message replaces it when it lands. Fettle's chats use the same view model. Keep UI updates batched (one per frame or so) so the UI thread isn't posted to per token; `stalls.log` shows if it is.

**Done when:**
- A reply's text shows in the transcript as it is written, in the agents view and in Fettle, and the finished message replaces the partial one without a duplicate.
- The saved transcript (`AgentStore`) holds only whole messages.
- A unit test of `StreamParser` on recorded `stream_event` lines, and an integration test with fake claude sending deltas, which fails without the change.
- `pwsh -File tools/perf/load.ps1 -Cards 5 -StallMs 100` shows no new stalls from the deltas.
