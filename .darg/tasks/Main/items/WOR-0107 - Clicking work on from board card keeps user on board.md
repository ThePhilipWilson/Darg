# WOR-0107: Clicking work on from board card keeps user on board

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

clicking work on from board card shouldn't take user away from board screen

From a note on the wall (20261117-150230-4378).

## How to check

Changed: `BoardViewModel.StartWork(CardViewModel)` now starts the agent without switching to the Agents section. Commit 093d644 on main (branch wor-0107, merged and pushed).

Try: `pwsh -File tools/publish.ps1` (or ⟳ Update), open the Board, right-click a card > Work on this with an agent, and also the card panel's ▶ Work on this. You should stay on the Board, and the card should move to In Progress and show its agent. Not run live; it builds clean. The API's work-on command now also leaves the section alone.

## QA notes (2026-11-17)

- Read commit 093d644: the board card path now passes show: false, and the work-on API shares it. The delegate only switches section when show is true. Build clean; 537 of 538 tests pass.
- Not run live: the How to check steps (right-click Work on, panel Work on) need a published copy and eyes.
- One failing test, TicketTitleTests (expected T-0001, got T-0002), is in code this change does not touch.
