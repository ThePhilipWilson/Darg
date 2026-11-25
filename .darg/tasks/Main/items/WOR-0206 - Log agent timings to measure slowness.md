# WOR-0206: Log agent timings to measure slowness

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-25
- **Done:**

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
