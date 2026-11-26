# WOR-0224: Find where agent and Fettle turns spend their time

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:** 2026-11-26
- **Moved:** 2026-11-26 10:40:00

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

## Findings (2026-11-26)

From `pwsh -File tools/perf/agents.ps1` on the real instance's agents.log (25 Nov and the morning of 26 Nov: 84 sessions, 94 finished turns: 58 card agents with a Haiku pick, 17 Fettle chats with 30 turns, 4 work, 1 QA, 1 rework, 1 chat, 3 resumed). Seconds. Darg = model pick and the transcript showing what was read; Claude Code start-up = node, the person's SessionStart hooks and MCP servers before Claude Code's own clock starts; Claude = model (API) time and the rest of its turn (tools, subagents); Person = waiting on a question or permission.

| Turns | Waited | Darg: pick | CC start-up (hooks) | First reply after message | Claude: model | Claude: tools, rest | Person |
|---|---|---|---|---|---|---|---|
| Card agent, first turn (median of 58) | 794 | 5.2 | 1.9 (0.9) | 4.5 | 112 | 667 | |
| Slowest: 90d4c779fd | 3653 | 4.8 | 2.3 (1.1) | 4.6 | 510 | 3136 | 0 |
| e303c3d49d | 3554 | 4.8 | 2.0 (1.1) | 4.3 | 815 | 2732 | 838 |
| 0262808a8d | 3279 | 5.4 | 2.7 (1.4) | 5.1 | 972 | 2299 | 17545 |
| 53af5b7774 | 1743 | 6.9 | 2.7 (1.3) | 5.4 | 74 | 1660 | 0 |
| Four started at once (15:56, bb5ad57c80 and others) | 118 to 1048 | 9.0 to 17.9 | 5.0 to 7.0 (2.8 to 4.2) | 8.7 to 12.4 | 49 to 126 | 45 to 909 | 0 |
| Fettle, new chat's first turn (median of 17) | 14.5 | 0 (rule) | 2.2 (1.1) | 5.6 | 8.6 | 2.7 | |
| Fettle, later turn (median of 12) | 7.9 | 0 | 0 | 2.0 | 6.2 | 1.4 | |

Where a card agent's tools time goes (Claude Code's own transcripts of 25 Nov in Darg's folders, tool call to tool result): `tools/test.ps1` runs took about 62,000 s over some 320 calls, 3 to 5 minutes each against about 45 s for a run alone (they wait for one of the machine's two test slots and the folder's lock), and polling loops (`until`, `sleep`) 16,000 s more, against 11,100 s of model time in all turns. A card agent's turn is mostly the test suite waiting, not Claude or Darg: that is WOR-0226's.

Darg slices over about a second, each with its cause and fix:

1. **The model pick: 5.2 s median per card agent, 9 to 18 s when several start at once.** Cause: the Haiku one-shot (`Haiku.Ask`) was a full `claude -p` reading the person's settings, so it ran their SessionStart and Stop hooks, started plugins and sent their CLAUDE.md files (about 34,000 tokens for a one-line answer), then waited for claude to exit. Fixed here: `--setting-sources ""` and `--output-format json`, the answer read the moment its line lands. Measured 2026-11-26, the same ask 4.2 to 4.6 s before and 2.2 to 2.4 s after (three tries each). What's left goes under start-up: [WOR-0233](card:WOR-0233) starts the session while the pick runs.
2. **Claude Code start-up: 1.9 to 2.2 s for every new session, 5 to 7 s on a bulk start.** Cause: node, the person's five SessionStart hooks (a Windows PowerShell each, about 1 s together) and MCP servers, all before the model is asked. Fixes: [WOR-0233](card:WOR-0233) (overlap it with the pick), [WOR-0235](card:WOR-0235) (a spare Fettle session ready ahead), [WOR-0236](card:WOR-0236) (whether Darg's agents run the person's hooks at all: Philip's call).
3. **A turn's end on the UI thread: 12.1 s held over 44 stalls on 25 and 26 Nov, worst 540 ms.** Cause (`stalls.log`): `Agent event TurnFinished > Board.Reload > Agents.RefreshCards > Board.Reload`, three deep, each reading every board from disk; the "Turn done" line and the rest of the app wait for it. Fix: [WOR-0227](card:WOR-0227) (from WOR-0223), which covers the re-entry too.
4. **Text arrives in bursts.** Cause: Darg starts claude without `--include-partial-messages`, so a reply shows only once its message is whole. Fix: [WOR-0234](card:WOR-0234). The transcript's own wait (event read to shown) is now logged as `first text shown` and `turn n shown (… longest wait)` and read by agents.ps1's Shown column; the installed copy doesn't have it yet, so there are no real figures for it until it is published.
5. **A log artefact, not a slow turn:** a Fettle reply of 8.7 s was logged as 238 s. Claude Code folds a message sent mid-turn into the running turn (one result for both), and AgentTimings counted a "next message's turn" from that result to the person's next message. Fixed in `AgentTimings` (a queued turn starts only when a turn's line is read) and agents.ps1 corrects older logs.

Claude's side: a Fettle reply is almost all the model (Sonnet at medium effort by the `kind: fettle` rule): 6.2 s of a later turn's 7.9 s, first reply 2.0 s after the message. Darg's measured share of a later Fettle turn is about nil; if Fettle should answer faster, the levers are its model, effort and context, not Darg.

`system thinking_tokens`: 4,402 of the log's 7,858 lines. Only a turn's first is kept now, as `thinking` (when thinking began is all the summary reads from them).
