# WOR-0234: Show agent and Fettle replies as they are written

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-26 19:49:09

## Description

From WOR-0224: Darg starts claude without `--include-partial-messages`, so stream-json gives each assistant message only once it is whole. A paragraph, or a long reply, appears in the transcript all at once after the model has finished writing it, which reads as text arriving in bursts. agents.log can't show this: its "first reply" line is the first whole message.

**Where:** `AgentSession.Start` (`src/Darg.Core/Agent.cs`) adds the flag; `StreamParser` reads the `stream_event` lines (`content_block_delta` with `text_delta`) into a new event; `AgentViewModel.On` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs`) grows the last `TextItem` instead of adding one, and the whole message replaces it when it lands. Fettle's chats use the same view model. Keep UI updates batched (one per frame or so) so the UI thread isn't posted to per token; `stalls.log` shows if it is.

**Done when:**
- A reply's text shows in the transcript as it is written, in the agents view and in Fettle, and the finished message replaces the partial one without a duplicate.
- The saved transcript (`AgentStore`) holds only whole messages.
- A unit test of `StreamParser` on recorded `stream_event` lines, and an integration test with fake claude sending deltas, which fails without the change.
- `pwsh -File tools/perf/load.ps1 -Cards 5 -StallMs 100` shows no new stalls from the deltas.

## How to check

**What changed** (commit [4cd1023](https://github.com/ThePhilipWilson/Darg/commit/4cd1023) on `android`, branch `wor-0234` merged and deleted):
- Agents start with `--include-partial-messages`. `StreamParser` turns `text_delta` stream events into `AssistantDelta` (a subagent's are left out). `StreamInbox` (new, Core) carries every event to the UI thread in order and posts streamed text at most every 40 ms, merged; a long reply waits longer, up to 250 ms, because `MarkdownView` redraws the whole reply each time.
- `AgentViewModel` grows a partial `TextItem` per message block, found by message id and block, and the whole message replaces it in place. Partial items aren't saved. One still open when its turn ends (cut off by Stop) is kept as it stands.
- The API marks a reply still being written (`done: false`, Fettle's `writing`), so the phone reads it again. agents.log gets a `first text streamed` line.
- Fake claude streams deltas when given the flag; `FAKE_STREAM <file>` holds a reply half-written, `FAKE_STREAM_OFF=1` and `FAKE_REPLY` are there for load comparisons.

**Try it** (needs a build with this commit: ⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal):
1. Start an agent, or ask Fettle, with something that takes a long reply, e.g. "Explain in four paragraphs how the board files are laid out. No tools."
2. Watch the transcript: the text should appear a few words at a time as it's written, not a paragraph at once. Fettle's box should do the same.
3. When it finishes, the reply should show once, with no repeat below it and no leftover fragment. Close and reopen Darg: the saved transcript has the reply once.
4. Optional: press Stop halfway through a long reply. What was written stays, and is still there after reopening.
5. Optional: `agents.log` beside instance.json has a `first text streamed` line ahead of `first reply` for each turn.

What it looks like half-written (fake claude, integration test): ![half-written](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0234/half-written.png)

**Checked:** full suite passed (981 unit, 292 integration) on the base before WOR-0227 and WOR-0226 landed. After rebasing onto those, the streaming, phone, Fettle, transcript, timings, cut-off and leak tests passed (29 unit, 33 integration). The two new integration tests fail without the flag. The parser test uses lines recorded from the real CLI (2.1.326). `load.ps1 -Cards 5 -StallMs 100` (Board and Agents sections, and with 6,000-character replies) showed the same stalls with streaming on as off: start-up and turn-end board reloads, nothing during the streaming.
