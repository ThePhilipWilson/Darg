# WOR-0095: Cards in QA Ready showing agents as done need a QA button

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

cards in qa ready that show agents as done need a button on the card to begin the qa process, either moving to needs phil attention, done or needs more work

From a note on the wall (20261117-103218-443b).

## How to check

- Changed: card panel (open a card) in a QA Ready column with no agent running now shows "QA: where does this card go?" with Needs my attention (moves to Needs Phil Attention; Darg writes its decision report), Done (to the done column), and the existing Send back with notes for needs more work. Commit f3cff42 on main (branch wor-0095, merged).
- Try: dotnet build, run the exe, open any QA Ready card with no live agent. Look for the three routes working and the card landing in the right column.
- Not run live; only built.
