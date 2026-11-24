# WOR-0131: Card to Done closes and forgets its agents

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-18

## Description

Moving a card to Done should write the done report, close the card's agents and forget them. Today it only does the report: `ReportFlow.EnteredDone` archives the QA report and writes the done report, and nothing closes or forgets the card's agents. Result: 55 of Workbench's 79 listed agents sit on Done cards.

**Wanted:** on entering Done, after the report step, close any open agent on that card and forget it (the `close-agents` / `forget-agents` behaviour). Skip an agent still mid-turn (working or needs Philip) and leave it for when it settles; a QA agent that moves the card to Done is itself mid-turn, so it must be forgotten once its turn ends, not killed.

**Where:** `src/Darg.Desktop/ViewModels/MainViewModel.cs` (`Board.EnteredDone`, ~line 503), `src/Darg.Core/ReportFlow.cs`. The agent-forget path is in `MainViewModel.Api.cs` and `Agents.Forgotten` already frees the card on the board.

**Done when**
- Dragging, approving or an agent moving a card to Done leaves no open or listed agent on it once its turn has ended.
- The done report is still written first, and agents that wrote it themselves are unaffected.
- The 55 agents already stranded on Done cards are cleared (a one-off clean-up, e.g. beside the Reports Clean up button).
- Tests cover the Done hook closing and forgetting, and skipping a working agent.

## How to check

**This round** (commit `dc755be` on `main`, pushed; the first round's `289e754` and `5578b47` stand):
- Reports → **Clean up**'s message now stays on screen. Before, when Clean up archived a QA report or wrote a done report (nearly always), the folder watcher's reload replaced it about 0.4 s later. Any message an action puts on the Reports status line now holds for 8 s against reloads, which covers Clean up, Archive, Restore, Approve, Send back and the errors.
- The test leak QA found, fixed as a class: when a test ended, its project stayed alive and work it had queued could start agents during the next test. Those agents logged into the next test's fake-claude log. Closing a project now shuts every agent: none starts again, a model pick that lands later doesn't start it, and a message doesn't resume it. Anything that asks the project for a new agent after that gets one that never runs. The test harness closes the projects (and Fettle's chats) instead of only closing sessions.
- Why it was in needs more work: the first round's QA found that Clean up's message still vanished, which was a claim this card made. The forgetting itself passed every QA check.

**To try:** update the installed copy first (header ⟳ → Update, or `pwsh -File tools/publish.ps1` from a terminal outside Darg). Then, with a done card whose QA report is still in the list (or any done card with an agent on it), press Reports → **Clean up**. The status line should read "Cleaned up: … N agents on done cards forgotten." and stay for about 8 seconds rather than flicking to "n reports, n unread".

**Tests:** `pwsh -File tools/test.ps1 -Filter DoneCardTests` (the Clean up test now archives a QA report and waits past the reload; it fails with the hold taken out, checked) and `-Filter ClosedProjectTests` (fails without the fix: 3 agent starts instead of 1, checked). Full suite 3 runs green: unit 586/586, integration 41/41.

## QA notes

- 2026-11-18 QA: **Clean up's message still vanishes when Clean up did something to the reports.** The card says it now stays on screen, but `ReportsViewModel.CleanUp` only sets it after its own `Reload()`. When Clean up archives a QA report or writes a done report, the reports folder's FileSystemWatcher fires and its 400 ms debounced `Reload()` → `Apply()` overwrites `Status` with "n reports, n unread · folder". Live, debug build at 5578b47, throwaway instance, fake agents, Clean up pressed through UI Automation and the status line polled every 50 ms: `190 ms: Cleaned up: 0 QA reports archived, 1 done report written, 1 agent on done cards forgotten.` then `617 ms: 3 reports, 3 unread · …`. The message stays only when Clean up changed no report file (seen on a run where it only forgot an agent, and on a second press: "Nothing to clean up…"). In Philip's real use Clean up nearly always archives or writes something, so he will mostly not see the line. `Clean_up_forgets_the_agents_left_on_done_cards` misses this because it reads `Status` synchronously after `Execute`; a test for it needs a report for Clean up to write or archive, then a wait past the debounce. Script: `.shots/qa0131/done-agents.ps1` (step 5) in the repo folder.
- 2026-11-18 QA, everything else passed. `tools/test.ps1 -Filter DoneCardTests`: 3/3. Live, same script: a finished agent's card moved to Done (board.json + Status) leaves the list in about 0.55 s and the done report is written; an agent working when its card is moved to Done is still listed and Working 3 s later, then goes when its turn ends; `decide Approve` on a card in For Human Attention moves it to Done and its agent goes; a saved agent (Darg restarted) goes when its card reaches Done, and its saved file is deleted; Clean up forgets the agent left on a Done card and keeps the one on a To Do card. No crash.log.
- 2026-11-18 QA, not caused by this card but on main: the full integration suite fails every time (3 of 3 runs on main, and also at 749aeb6 without this card) on `DecisionTests.An_answer_of_my_own_goes_to_a_new_card_agent_when_the_asker_is_gone`. The rework agent from the test before it (`Picking_a_back_option_sends_it_back_with_the_question_and_notes`) starts its fake-claude process late, so it logs to the next test's `FAKE_LOG` and `Prompts()[0]` is that test's rework prompt (seen in that test's fake-claude.jsonl). This is the same leak that 5578b47 fixed for one test only. The 5578b47 message says integration 27/27, but there are 39 tests on main, so the full suite was not re-run after the rebase onto WOR-0124. Fix the class (e.g. the test teardown waits for every agent's process to have started and stopped, or FAKE_LOG is passed per agent and not read from the process-wide env), not this one test.

### 2026-11-18 02:01

why is this in needs work? looks like it should be in qa

### 2026-11-18 QA, round 2 (dc755be): passed

- Full suite `tools/test.ps1`: unit 586/586, integration 41/41; integration run twice more, 41/41 each. `DecisionTests.An_answer_of_my_own...`, which failed on every full run in round 1, passes now.
- Both "fails without" claims hold, checked in a throwaway worktree: with the hold line removed, `DoneCardTests.Clean_up_forgets...` fails (status sub-string not found); with the `_closed` / `_shut` guards disabled, `ClosedProjectTests` fails with 3 starts instead of 1.
- Live, debug build at dc755be, throwaway instance, fake agents (`.shots/qa0131/done-agents-r2.ps1`, log `r2-live.log`): steps 1 to 4 from round 1 all pass again. Clean up was pressed through UI Automation in the case that failed before (it archived 1 QA report and wrote 1 done report). The line "Cleaned up: 1 QA report archived, 1 done report written, 1 agent on done cards forgotten." held from first read to 10 s with no change. The To Do card kept its agent, and a second press said "Nothing to clean up". No crash.log.
- Nit, not blocking: in `AgentsViewModel.cs` the old `/// <summary>Start (or resume)...` line for `Begin` now sits above `Shut`'s summary, so `Shut` has two summaries and `Begin` has none.
- Not checked: the installed copy (not updated by QA; the debug build is the same commit).
