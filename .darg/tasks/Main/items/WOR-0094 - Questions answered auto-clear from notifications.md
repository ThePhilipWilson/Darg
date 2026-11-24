# WOR-0094: Questions answered auto-clear from notifications

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

questions that have been answered should automatically remove themselves from notifications

From a note on the wall (20261117-102923-bb65).

## How to check

Changed: when an agent has no question or permission ask left unanswered, its "has a question" and "wants to use ..." entries are removed from the Notifications list (Fettle), and the tab closes if the list is empty. Commit b1a76cf on main (branch wor-0094, merged and pushed). Files: MainViewModel.cs (`Settle`, `NotificationEntry.IsAsk`), AgentsViewModel.cs and MascotViewModel.cs (`AsksSettled` event).

To try: build and start the debug exe, have an agent ask a question (AskUserQuestion), open Fettle's Notifications tab so the entry shows, then answer the question in the agent view. The entry should vanish; "finished" and "stopped with an error" entries should stay. Builds clean; not exercised live.
