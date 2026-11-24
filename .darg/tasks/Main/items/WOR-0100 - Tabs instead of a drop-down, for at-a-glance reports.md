# WOR-0100: Tabs instead of a drop-down, for at-a-glance reports

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

reports, would prefer tabs, slightly wider column if needs be, to a drop down

gives much better at a glance information

From a note on the wall (20261117-112534-6301).


## How to check

- Changed: Reports section's kind drop-down is now a tab strip (All, Finished, Investigations, QA reports, each with its count); the list column is 480 wide instead of 340.
- Commit 3162b6b on main (branch wor-0100, merged and pushed). File: src/Darg.Desktop/Views/ReportsView.axaml.
- Try: open Darg, Reports. Click the tabs.
- Look for: counts readable at a glance, tabs fit on one row, list still reads well. Tab text is size 12 and may look small.
