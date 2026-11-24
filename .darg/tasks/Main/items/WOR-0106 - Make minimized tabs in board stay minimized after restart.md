# WOR-0106: Make minimized tabs in board stay minimized after restart

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** cnr
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

minimized tabs in board stay minimized on restart

currently not working.

From a note on the wall (20261117-150023-6982).

## Could not reproduce

Tried on current main (f96caff, the same minimize code as the copy installed at 17:44), with a debug build and a throwaway instance (one board: Backlog, To Do, In Progress, Done). UI Automation pressed each list's – button, then the app was closed and started again:

- Minimize To Do, restart: To Do comes back as a strip; board.json has `"minimized": ["c1"]`.
- Minimize Backlog, restart, then minimize Done, restart: all three come back as strips; board.json has `"minimized": ["c1", "backlog", "c3"]`.

So the setting does survive a restart. The minimize code hasn't changed since WOR-0006 (8ed2cba) and WOR-0076 (b7132bf); BoardTests.Minimized_lists_are_kept_in_board_json covers saving it.

What happened to the real board: this morning's agent transcripts show the Workbench board.json with `"minimized": ["c1"]` (To Do) at 10:23. By 10:28 the To Do column (c1) was gone. Removing a column also clears its minimized entry, and nothing else has been minimized since. No board in any project has a `minimized` key now. Every other program that writes board.json (Darg's own BoardStore, agents' Edit calls, agents' Python read-modify-write scripts) keeps keys it doesn't know. The only way I found to lose the key is a write that happens in the same instant as another write.

If a list opens again after a restart, open board.json straight away and check whether `minimized` is still there. That tells us whether the app or a writer is at fault.
