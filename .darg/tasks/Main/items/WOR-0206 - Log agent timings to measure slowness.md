# WOR-0206: Log agent timings to measure slowness

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-25
- **Done:** 2026-11-25
- **Moved:** 2026-11-25 02:56:18

## Description

> Philip feels Darg agents are slower than the plain CLI. A speed-test agent could not measure the Darg side (CLI one-shot ran about 14 s). Have AgentSession log timestamps for process start, system/init, first assistant message, first tool call and result, so time-to-first-tool and turn time can be seen per agent and compared with the CLI. Suspects to check with it: the Haiku model-picking step before a session starts, slow or failing MCP servers at startup (tabby and unity-mcp refuse connections), permission prompts left unanswered, and the extra briefings and add-dir folders. Done when each agent shows its startup and turn timings.

Make each agent record how long its startup and turns take (including the model-picking wait before the process starts), so Darg's speed can be compared with the plain CLI's ~14 s one-shot and the suspects checked. This is separate from WOR-0161 (app-wide CPU/UI profiling, done); this is per agent.

**Where:** `AgentSession` and `StreamParser` in `src/Darg.Core/Agent.cs` (the stream-json events: `system`/`init` is parsed near line 153; start the clock where the process is started). The model pick is `ModelProfiles` / `Haiku.Ask` in Core (the "Picking a model…" wait). Write to a log beside `perf.log` in the instance folder (follow `PerfLog` in `src/Darg.Core/Perf.cs`), one line per milestone with agent id, and show the numbers in the agent's transcript (as the profile note is) or the Agents view.

**Done when:**
- Every agent logs: model pick start and end, process start, `system`/`init`, first assistant message, first tool call and its result, and turn end, each with elapsed time from start.
- Per agent, time-to-first-tool and turn time can be read off the log or the transcript without a debugger.
- A unit or integration test (fake claude, `FAKE_LOG`) checks the lines appear in order for a short turn; it fails without the change.
- One real agent and one `claude -p` one-shot of the same prompt are compared, and the gap and its biggest slice are noted on the card.

## Questions

- Should timings show in the UI, or only in the log file? (Default: log file, plus one summary line in the transcript.)
  **Answer:** the default: `agents.log` beside perf.log, plus the sum on each "Turn done" line (decided by agent, WOR-0206, 2026-11-25)

## Findings (2026-11-25)

Same prompt ("Read README.md and tell me its first heading, in one line.") in this repo, on Haiku both ways (the profile Darg picked was Quick, haiku/low):

| | first tool | turn done |
|---|---|---|
| `claude -p --model haiku`, stream-json | 5.9 s | 9.8 s |
| Darg agent, from the click | 13.7 s | 15.7 s |
| Darg agent, from its process starting | 4.0 s | 6.0 s |

- **The gap is about 6 s, and its biggest slice is the model pick: 9.65 s** before claude even starts. `Haiku.Ask` runs a whole `claude -p` for one line, so it pays the full start-up: hooks, then every MCP server. Alone, that one-liner takes 7.5 to 7.9 s; with `--strict-mcp-config` (no MCP servers) 4.4 to 4.6 s.
- Claude's own turn inside Darg is no slower than the CLI (6.0 s against 9.8 s here; init came 2.5 s after the message against 4.3 s for the CLI). The briefings and add-dir folders don't show up as a cost.
- MCP at start-up: the init line lists unity-mcp and tabby as failed, and four still pending, but init still came 0.8 s after the hooks ended. Failing servers don't hold up the agent's turn. They are part of what makes the pick slow.
- Permission prompts didn't come up in this run. agents.log now logs each ask and its answer, so a wait on one shows as the gap between them.
- Plain CLI on the default model, for reference: 11.3 to 11.8 s, init at 4.3 s.

Likely fixes, not done here: run the pick lighter (`--strict-mcp-config`, about 3 s off), start the agent's process while the pick runs, or skip Haiku when a rule matches the job.
