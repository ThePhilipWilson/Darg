# WOR-0153: Agents list keeps agents for completed tickets

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 18:31:56

## Description

The Agents list fills up with leftover agents whose tickets are already completed. Agents on done cards should be closed/forgotten automatically (or at least cleaned up in one action) when the card is completed.

## How to check

**Cause (this round):** the done check behind "Complete, pending removal" and Clear finished read only the board shown (`Board.IsDone` reads the picked board). On a project with several boards, a card done on a board that isn't the picked one never counted, so its agent stayed listed as "saved" and Clear finished skipped it. Philip's leftovers were the QA agents on WOR-0123, 0128, 0137 and 0149, which sit on Main while Android is picked. An integration test reproduces this: with the old check it fails ("saved · send to carry on" where "Complete, pending removal" is expected).

**Changed:** `BoardViewModel.DoneCardIds()` returns the done cards of every board in the project, read from the boards on disk (the same reads `DoneCards` and `CardBoards` make). `AgentsViewModel.RefreshCards` takes each agent's done state from it, once per refresh, and `IsCardDone` uses it too. Nothing else changed. The earlier fix (8a83726, `FinishedTurn` reads past a close made after the turn) is still in.

**Commit:** `93bb457` on `android` (`wor-0153` at the same commit, fast-forwarded and pushed). https://github.com/ThePhilipWilson/Darg/commit/93bb457

**To try:**
1. Update the installed copy (the header's ⟳ menu, or `pwsh -File tools/publish.ps1` from a terminal).
2. With Android still the first board, open the Agents section. WOR-0123, 0128, 0137 and 0149 (QA agents on Main's done cards) should read "Complete, pending removal" without clicking the Main tab first. Press Clear finished: they should go.
3. Not changed here: a QA agent that passes its card to Done on a board that isn't shown at all still doesn't leave the list on its own. The automatic forget runs from a shown board's reload. Clear finished and Clean up do cover it.

**Tests:** `pwsh -File tools/test.ps1 -Integration -Filter DoneCardTests`. The new test is `Clear_finished_takes_an_agent_whose_card_is_done_on_another_board`; it fails with the check reverted to the shown board and passes with the fix. Full suite on `93bb457`: unit 705/705, integration 93/93.

## QA notes

### 2026-11-20 (QA agent 83efd6bd18)

- **Passed:** the `FinishedTurn` fix is right and does what it says. Full suite at 63daa07: integration 67/67, unit 617/619 (the 2 failures were `DocsTests`, caught by a stray `docs/zz-qa-probe.md` another session dropped in mid-run; the failures have nothing to do with this card, and once the file was gone, unit passed 622/622 at 788e198). With the fix reverted, both new `DoneCardTests` fail (one assertion fails, one 30 s timeout), so they test the fix. The saved leftovers in the real instance (WOR-0123, 0128, 0137, 0149 QA agents) end exactly `Turn done` → `Session closed by Darg. Send a message to carry on.`, the shape the fix reads past.
- **Failed, step 2 of To try:** I loaded copies of the real saved Workbench agents and boards into the debug build on a throwaway instance (`.shots/qa0153/check.ps1`, fake claude) and pressed Clear finished through UI Automation. **None of the four went**, and all read "saved · send to carry on". They go only after the board section's **Main** tab is clicked first (`check.ps1 -PickMain`): then Clear finished takes exactly those four.
- **Cause:** `CardDone` comes from `Board.IsDone` (BoardViewModel.cs:800), which reads only `_board`, the root/picked board. On load that is always `Boards.FirstOrDefault()` (BoardViewModel.cs:389), which here is **Android**, and the four cards are on **Main**. Putting Main in `shown-boards` as a second row doesn't help either (tried): rows don't count. So on Philip's setup, after the update restarts the app, step 2 fails until he clicks the Main tab. The same applies to any agent whose card is done on a board other than the picked one.
- **Step 3 (automatic forget) has a narrower version of the gap:** `EnteredDone` fires from each shown row's reload (`NoticeAttention`), so it works on any shown board but not on one that isn't shown at all. The integration tests use a single board, so neither gap is covered.
- **Fix suggestion:** have `Agents.CardIsDone` ask across every board in the project (as `CardBoards` / `DoneCards` already do), not only `_board`. Add an integration test with two boards where the leftover's card is done on the non-picked one.

### 2026-11-20 (WOR-0153 agent, round 2)

- Reproduced the cause from the QA notes with a test that fails without the fix, and fixed it in `93bb457` as suggested (all boards, not the picked one). The automatic forget on a board that isn't shown is still open (To try, step 3).

### 2026-11-20 (QA round 4, Haiku QA agent)

- **Passed:** `93bb457` builds. Full suite on it: unit 705/705, integration 93/93 (`pwsh -File tools/test.ps1`, exit 0). `Clear_finished_takes_an_agent_whose_card_is_done_on_another_board` fails with the shown-board-only check ("saved · send to carry on" where "Complete, pending removal" is expected) and passes with the fix.
- **Passed:** Step 1's build is the card's commit; the `wor-0153` branch point is an ancestor of `android`.
- **Could not check, step 2 on real data:** the four leftover QA agents (WOR-0123, 0128, 0137, 0149) are no longer in Philip's saved agents (31 agents now, none for those cards), so Clear finished on a copy of them had nothing to clear. The UI check ran on copies of the real agents and boards: Clear finished left the 31 saved agents as they were, none of which are on done cards. The case the step describes is covered by the integration test above, which uses the same shape (a card done on the non-picked board, its agent closed after its turn).
- **Not changed (as the card says):** step 3, a QA agent passing its card to Done on a board that isn't shown, still waits for a shown board's reload. Clear finished and Clean up do cover it.
- Moved to Done: the card's own step 2 has nothing left to check on Philip's real saved agents, and the step-3 gap is the card's own stated scope.
