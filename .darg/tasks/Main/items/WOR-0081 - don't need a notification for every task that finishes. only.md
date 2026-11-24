# WOR-0081: don't need a notification for every task that finishes. only need it f

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

From a note on the wall (20261117-000357-ad02).

## How to check

- **What changed:** an agent's "finished" notification (popup and Notifications-tab entry) now only fires when its turn took 2 minutes or more. Errors, questions, permission asks, usage-limit and flow finished/stopped notifications are unchanged. The 2 minutes is `AgentsViewModel.LongTurn`, my pick since the note didn't give a number.
- **Commit:** 27870ba on main (branch wor-0081, merged and pushed).
- **Try it:** publish/build and run Darg, start an agent with a quick prompt ("say hi") while looking at another tab: no notification. Then one that runs over 2 minutes: you get "finished".
- **Not exercised live:** it builds clean, but I didn't run the app.

## Automated QA

Verified:
- Commit 27870ba exists and is on main. Its diff is one file, `AgentsViewModel.cs`: `LongTurn` (2 min), `_turnStart` set in `TurnBegins`, and the "finished" `Notify` call is skipped for turns under `LongTurn`. Failed turns still notify.
- The work is superseded by a later commit. fb9717c (WOR-0078) replaced it with `Decisions.LongTurn` (15 min) and `Decisions.TellTurnEnded`, and `AgentsViewModel.LongTurn` no longer exists. The 2 minute figure in "How to check" is out of date; a quick turn gives no notification, which is what this card asked for.
- `dotnet test tests/Darg.Core.Tests`: 526 of 527 pass. The one failure is `TicketTitleTests.Retitling_a_short_note_keeps_its_words_at_the_top_of_the_description` (WOR-0082's test): it expects file `T-0001 - ...` and gets `T-0002 - ...`. It's in note-ticket code this card doesn't touch, and it fails at HEAD, so it needs its own look.

Could not verify: running the app and seeing a quick versus long turn notify or not. That needs the live app.
