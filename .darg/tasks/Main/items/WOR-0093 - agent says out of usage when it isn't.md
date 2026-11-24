# WOR-0093: agent says out of usage when it isn't

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

Philip's words: "sometimes get a agent out usage comment when agent not out of usage"

From a note on the wall (20261117-101139-3ba3).

**What's wanted:** an agent shows "out of usage" (and waits to carry on) only when it really is out of usage. Today a normal reply that mentions a usage limit can trigger it.

**Where:**
- `src/Darg.Desktop/ViewModels/AgentsViewModel.cs:871`: `TurnFinished f when IsLimit(f)` sends any finished turn to `OutOfUsage`.
- `src/Darg.Desktop/ViewModels/AgentsViewModel.cs:904`: `IsLimit` is true whenever the turn's own text matches, not only when the turn errored.
- `src/Darg.Core/UsageLimit.cs:72`: the `LimitText` regex (`hit your … limit`, `usage limit`, `limit reached`, `out of usage`).
- `src/Darg.Core/Agent.cs:193-198`: the `rate_limit_event` becomes `UsageLimited`; `AgentsViewModel.cs:868` keeps the `Rejected` one as the trusted signal.

**Chosen reading:** only real limits count: a `rate_limit_event` with status `rejected`, or an errored turn whose text says the limit was hit. Text on a turn that finished normally doesn't count.

**Done when:**
- A turn that finishes normally, with a reply mentioning "usage limit", shows no out-of-usage note, no Continue button and no carry-on timer. A Core test on `UsageLimit`/`IsLimit` covers it.
- A turn after a `rate_limit_event` with status `rejected` still shows out of usage with its reset time.
- An errored turn whose text says "You've hit your 5-hour limit" still shows out of usage.
- Run an agent, ask for a normal reply that quotes "usage limit": it stays Idle with no out-of-usage line.

**Questions left:**
- Which screen did Philip see it on? Assumed: an agent's transcript. If not, the fix changes.
- The cause is read from the code, not reproduced. A transcript from the bad case would confirm it.
- Should a `rejected` event with utilisation under 1 still count? The code trusts it today; not checked.

## How to check

Changed: `UsageLimit.StoppedByLimit` (Core) now requires the turn to have errored; `AgentsViewModel.IsLimit` uses it. Before, any finished turn whose own text matched "usage limit" / "hit your … limit" went to out-of-usage. Commit 2bd447a on main (branch wor-0093, merged).

Try: build and run Darg (`dotnet build`, then the exe), start an agent and ask "Reply with exactly: the usage limit resets at 5pm". Look for: agent stays Idle, no out-of-usage note, no Continue button or timer. Tests: `dotnet test tests/Darg.Core.Tests --filter UsageLimit` (21 pass).

Not exercised live: a real rejected rate_limit_event / real limit error (covered by unit tests only). A `rejected` event with a non-errored turn no longer counts (it never did apart from the text match).

## QA notes

- 2026-11-17 QA: build OK; `dotnet test --filter UsageLimit` 21 pass; code read matches the fix. Live run (agent quoting "usage limit" stays Idle) not done by QA, so that is for Philip to try.
- 2026-11-17 QA (second pass): build 0 errors; UsageLimit tests 21 pass; IsLimit/StoppedByLimit code read, _rejected is reset each turn. Live run on the throwaway instance: agent asked to reply 'the usage limit resets at 5pm' showed done/Idle, no out-of-usage note, no Continue or timer. Not exercised live: a real rejected event or limit error (unit tests only). Verdict: Done.
