# WOR-0125: Work All and QA Run All bulk buttons

- **Status:** Done
- **Priority:**
- **Type:** Feature
- **Labels:** board, agents
- **Created:** 2026-11-17
- **Done:** 2026-11-18

## Description

Add two bulk actions to the board so Philip doesn't have to start each card by hand.

**Work All**
- A button at the top of the Backlog column and at the top of the In Progress column.
- Starts work on every card that isn't already in progress (no running agent on it), across Backlog and In Progress.
- Cards that already have an agent working are skipped, so pressing it twice doesn't double up.

**QA Run All**
- A button at the top of the QA Ready column.
- Begins QA for every card in that column that isn't already being QA'd.
- Cards with a QA agent already running are skipped.

Things to settle in the implementation:

- Each button should say how many cards it will start (e.g. "Work All (5)") and be disabled at zero.
- Starting agents costs sessions, so confirm with a count before launching when it's more than a few, or cap concurrency and queue the rest.
- Decide what "already in progress" means precisely: column is In Progress, or an agent is attached to the card. Prefer the agent check, since a card can sit in In Progress with nothing running.
- Work All from the Backlog should move started cards into In Progress, as starting one by hand does today.
- Show progress or failures (some cards fail to start) without a modal per card.

## How to check

Nothing to check by hand. The earlier QA pass passed everything automated (tools/test.ps1: unit 566/566, integration 19/19; tools/bulk-start.ps1: 14 ok). The failed-start status line is covered by a unit test (`BulkStartTests`, Summary with a failed card). The three taste questions are settled: the inline confirm above 3 stays, the count follows what is showing (as Work on N does), and the plain bar stays. Commits e850f9d and f45ded6, on main; no new commits this round.

## QA notes

2026-11-17, QA pass: all automated checks passed (tools/test.ps1: unit 566/566, integration 19/19; tools/bulk-start.ps1: 14 ok, 0 failed, covering both buttons, the count, disabled at 0, asking above 3, backlog cards moving to In Progress, no doubling on a second press). bulk-ask.png shows a plain full-width bar under each column name.

Needs Philip's judgement (taste and product choices, so not automatable):
- Whether the button looks right (plain grey bar, small text) and sits right.
- Whether the inline confirm above 3 beats the card's alternative of cap-and-queue.
- Whether the count should follow the filter (now counts only cards showing).

Not checked: the status line naming a card that failed to start (no test forces a start failure that I ran or read).

### 2026-11-18 00:06

this shouldn't be in for human attention
