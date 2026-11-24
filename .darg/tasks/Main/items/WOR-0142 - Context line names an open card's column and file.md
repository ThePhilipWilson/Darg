# WOR-0142: Context line names an open card's column and file

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

From the WOR-0135 investigation (see its Findings section). When a card is open on the board, `[Philip is looking at: ...]` gives its id and title, so Fettle hunts for the file with `ls items | grep`.

**Want**
- `MainViewModel.Where` adds the open card's column and full file path.

**Done when**
- With a card open, the line Fettle is sent carries its column and path (integration test).

Effort: about 15 min.
