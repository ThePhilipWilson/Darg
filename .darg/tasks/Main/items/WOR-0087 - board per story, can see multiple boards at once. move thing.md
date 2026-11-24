# WOR-0087: board per story, can see multiple boards at once. move things into sto

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

From a note on the wall (20261117-002552-ed64): "board per story, can see multiple boards at once. move things into stories automatically"

Philip's picks (asked 2026-11-17): a story is its own board; several boards show as stacked rows; each new card is placed by Haiku.

## How to check

What changed (main, commit 7ff9878, branch wor-0087 merged and deleted):
- **Inbox:** a project's inbox is the board named Inbox, Main or Backlog (else the first). A note's ticket now goes there, not to whichever board is showing. + Card made on the inbox goes there too.
- **Haiku places new cards:** after the ticket's retitle, Haiku reads the card and the other boards' names and newest titles, and the card moves to the one it names. No good fit, or the card is picked up meanwhile: it stays on the inbox. The status line says "Haiku put WOR-xxxx on <board>".
- **⧉ Boards** (beside + Board, shown once there's more than one board): tick other boards to show under the picked one, a row each. Up to three share the height; more scroll. Kept per project in `%APPDATA%\Workbench\home\projects\workbench\shown-boards`. A row's ✕ hides it.
- **Drag between rows** moves a card to that board (same id). A card with a live agent stays put.
- The filter, label chips, Show completed and Pick all / Work on N act on every row.
- + Board's "Sort into boards" agent now sorts by story and keeps the inbox.

Try it (Workbench has only Main today, so nothing changes until there's a second board):
1. Update Darg (header ⟳, or `pwsh -File tools/publish.ps1` from a terminal outside Darg).
2. Board → + Board → make a story board, e.g. "Notes wall", and drag or "Move to board" a couple of post-it cards onto it. Or run ✨ Sort into boards and let the agent propose stories.
3. ⧉ Boards → tick it. Rows should read clearly, with the board name above each.
4. Make a ticket from a post-it about notes. Within about 20 s it should be retitled, then moved to Notes wall, with the notice in the status line.
5. Drag a card from one row to another; check its file moved to that board's items folder.

Screenshots from the throwaway check: `D:\GitHub\ThePhilipWilson\Workbench\.shots\wor-0087\rows2.png` (three rows), `rows3.png` (after a cross-board drag), `rows4.png` (Haiku put WOR-0091 on Notes wall).

Not tried live: a post-it ticket being placed (same code path as + Card, after the retitle), and a row's ✕. Choices I made for you: the inbox naming rule (Inbox > Main > Backlog > first board), the 320 px minimum row height with three rows on screen, and that + Card on a story board stays where it's made.
