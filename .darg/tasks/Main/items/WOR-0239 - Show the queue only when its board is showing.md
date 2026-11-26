# WOR-0239: Show the queue only when its board is showing

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-26
- **Done:**
- **Moved:** 2026-11-26 17:02:08

## Description

The queue should only show up when the board it belongs to is the one showing, or when the "all" view is showing. Philip asked: "queue should only show up when the relevant board is showing", then added "that or all".

## How to check

Changed: a queue's lane (and the whole queue strip) shows only while a board holding one of its cards is showing, or the All board is. An empty queue, or one whose cards no board has, shows everywhere. Branch `android`, commit 4645e1c (branch `wor-0239`, merged fast-forward).

Try it: build (`dotnet build`) and run `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe` on a project with two boards. Queue some cards of board A (card menu, Add to queue, or + Queue). Pick board B's tab: the lane disappears. Pick A: it's back. Pick All: it's there. Test: `pwsh -File tools/test.ps1 -Filter A_queue_shows_only`.

Look at: a queue with cards from several boards shows on each of those boards, and on a ticked extra board in the multi-row view. A queue has no board of its own, so I took "its board" to be any board holding its cards. I didn't see it drawn.
