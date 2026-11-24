# WOR-0146: board column sort by, a-z, newest, longest in state

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-18
- **Done:**

## Decision answered 2026-11-18 14:31

**Question:** The sort menu and the ordering pass every automated check; is the menu's look and feel (check marks, Reverse order item, footer refusal note) acceptable as it is?
**Asked by:** agent QA for WOR-0146, 2026-11-18
- **Looks fine** (recommended) → done: the card goes to Done as it stands.
- **Needs changes** → back: sent back with your notes on how the menu should look or work.
- **Look later** → stay: stays here until you have opened the menu.
**Context:** Full suite passed (unit 617/617, integration 62/62) and the sort order, Moved stamp, per-column persistence and drag refusal are covered by tests; the menu's appearance can't be checked by automation (a popup flyout isn't captured by tools/shot.ps1), and the card itself says it wasn't viewed on screen.

**Answer:** Looks fine (2026-11-18 14:31)

## Description

> board column sort by, a-z, newest, longest in state. ascending and descending

From a note on the wall (20261118-113102-0249).

**Wanted:** each board column can be sorted by title (a-z), newest, or longest in its current column, in either direction. Time in column is tracked properly, not guessed from file dates: a card records when it last entered its column.

**Where:**
- `src/Darg.Desktop/Views/BoardView.axaml` (~line 141): each column's ⋯ menu (`MenuFlyout`) gets the sort choices and an ascending/descending toggle.
- `src/Darg.Desktop/ViewModels/BoardViewModel.cs`: `ListViewModel` (~line 1188) holds the column's `Cards`; `Show()` syncs them via `ListSync`. The sort applies there, as a view order only; the manual drag order in board.json's `order` stays underneath and returns when sort is "Manual".
- `src/Darg.Core/Board.cs`: `BoardStore.Load` (line ~131) orders cards by board.json `order`, then number. Card files are `- **Field:** value` lines (`Created` is a date only, no time). Add a `Moved:` field (date and time), written whenever a card's column (Status) changes, whoever changes it (drag, agent, Send back, `BoardStore` moves). Cards with no `Moved:` fall back to Created. the board app and Obsidian read the same files, so an unknown field must not break them.
- Keep the pick per column, kept beside the board (like `ShownBoards`), not in board.json order.

**Done when:**
- A column's ⋯ menu offers Manual, A-Z, Newest, Longest in column, with ascending/descending; the column reorders at once.
- Longest in column puts the card that has sat there longest first (and reverses).
- Moving a card to another column stamps `Moved:`; the card's time in column restarts.
- The sort survives a reload and restart; dragging within a sorted column either re-orders Manual or is refused clearly.
- Integration test (fails without the change) checks the order for each sort and the stamp on a move; `pwsh -File tools/test.ps1` passes.

**Questions left:**
- Is the sort remembered per column or per board? (Assumed per column.)
- Should existing cards without `Moved:` be back-filled from file modified time once, or fall back to Created?

## How to check

Branch `wor-0146`, merged to main as commit 949fbf0 (pushed).

**What changed:** each board column's menu (the ⋯ button) now has Manual, A-Z, Newest, Longest in column, and Reverse order (shown once sorted). Natural order is A to Z, newest first, longest in the column first; Reverse flips it. The pick is kept per column in `column-sorts` beside `shown-boards` (not in board.json); Manual brings the drag order back. A card records when it entered its column in a new `- **Moved:** yyyy-MM-dd HH:mm:ss` line (written by drags, Send back, Transfer, column removal, and stamped on the next board read when an agent edits Status itself); cards with none fall back to Created (date only).

**Try it:** build and run Debug, open the Board, pick Longest in column in the In Progress menu, then drag a card into another column and see its Moved line appear in the card file. Drag a card within a sorted column: it is refused with a note in the footer.

**Look at:** the menu's look and the check marks; the reorder is immediate; the board app/Obsidian still open the card with the extra line. I chose: sort per column, no back-fill (fall back to Created), Reverse as one toggle rather than ascending/descending items.

Tests: `tests/Darg.Core.Tests/ColumnSortTests.cs`, `tests/Darg.Desktop.Tests/ColumnSortTests.cs`; `pwsh -File tools/test.ps1` passed (unit 615, integration 58). I did not look at the menu on screen.
