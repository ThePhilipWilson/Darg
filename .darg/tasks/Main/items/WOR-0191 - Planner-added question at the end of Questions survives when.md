# WOR-0191: Planner-added question at the end of Questions survives when the page answers the last question

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** planning, agents
- **Created:** 2026-11-22
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 23:59:32

## Description

> Found in QA of WOR-0176 round 3. With the planner held to one part, if it adds a question at the end of Questions while the person answers the last question on the page, the added question stays in the plan and Finish() returns nothing, so the status says nothing was put back. Cause: PlanStore.Answer writes the answer line at the end of that question, the same spot the planner's lines went in, so diff3 treats them as one changed stretch and the page's write takes it whole. The same happens with a planner deletion next to an answer combined with an added question, and with Make cards writing an id into a card heading while the planner rewords the line just under it. Wanted: the added question goes back and the status line names Questions. Add a test that fails today.

Wanted: when the planner is held to one part and adds a question at the end of Questions while the person answers the last question on the page, the planner's addition goes back and the status line names Questions. Same for a planner deletion next to an answer plus an added question, and for Make cards writing an id into a card heading while the planner rewords the line just under it. Follows WOR-0176 (round 3 QA); no other card covers it.

**Where:** `src/Darg.Core/Planner.cs`: `Rebase` (line ~240) and `Merge3` (~272) treat the page's answer line and the planner's added lines, which sit at the same spot, as one changed stretch and take the page's whole; `KeepOnly` (~194) and `Finish` (~382) then see nothing to put back. The page's write comes from `PlanStore.Answer` (and Make cards' heading ids) in Core. Tests in `tests/Darg.Core.Tests/PlannerTests.cs`.

**Done when:**
- A unit test in `PlannerTests.cs` (planner added a question after the last one while the page answers the last one) fails on today's code and passes after the fix: the added question is gone and `Finish()` reports Questions.
- Tests for the other two cases (a planner deletion beside an answer plus an added question; Make cards' heading id with the planner rewording the line under it) likewise fail before and pass after.
- The page's own answer or id still stands in each case; existing `Merge3` and Rebase tests still pass.
- `pwsh -File tools/test.ps1` is green.
