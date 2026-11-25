# DAR-0003: Old Done tickets and multiple boards not showing; store board data in the repo

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-25 01:23:50

## Description

Philip can no longer see old tickets in the Done column, or multiple boards, as he should. Investigate why completed cards and extra boards are missing from the board view. Also: this project's data (boards, cards) should be saved in the repo (.darg inside the project repo) rather than elsewhere.

## Findings

Could not reproduce a rendering fault: a headless test with a Done column card, a Completed card (behind Show completed) and a second board shows all of them. The cause is where the data lives: `.darg/` is git-excluded (WOR-0144), and the old WOR tickets and story boards were in the Windows vault/instance, so a clone or another machine has only what was made locally (this Mac's Darg project has just DAR-0001..3). The old tickets can't be recovered from this repo.

## Findings (2026-11-25, Windows)

Reproduced on the installed copy: the board said "Couldn't read the board: An item with the same key has already been added. Key: WOR-0011". Main had two files each for WOR-0011 and WOR-0198. The name scrub (2dbf652) renamed them in git, but the checkout left the old-named files behind, because `.darg/` was git-ignored. `MovedWatch.Read` keyed a dictionary by card id, so every read of the board threw after loading and before redrawing. Main froze with stale lists, Done included, and with the error in place of its rows. Earlier, the 13:05 cherry-pick of 54ada8c had replaced this machine's Main/board.json with the other machine's DAR one (found by agent 6735f032fb).

## How to check

- **What changed:**
  - 9ad8890 on `android`: a board with two card files on one id reads instead of throwing. Both cards show, and the notice line names the files ("WOR-0011 is in 2 files on Main: ... Delete the stale copy in its items folder").
  - 11449b8 and 8d2496d (agent 6735f032fb): a root `.gitignore` keeps `.darg/tasks` tracked, so new cards are no longer ignored, and this machine's board.json and new cards are committed.
  - The two stale copies were moved out of the board to `D:/GitHub/ThePhilipWilson/Darg/.shots/dar-0003/stale-cards/`.
- **Tests:**
  - Command: `pwsh -File tools/test.ps1 -Filter "Two_files_with_one_id|A_card_in_two_files"`. Both tests fail without the fix: the unit one with the live error, the integration one because the board stayed frozen.
  - The notice with two boards showing: ![duplicate id notice](D:/GitHub/ThePhilipWilson/Darg/.shots/dar-0003/duplicate-id.png)
- **To try:**
  1. Run ⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal, so the installed copy has 9ad8890.
  2. Open Darg's Board. There should be no "Couldn't read the board" line. Scroll right: Done holds the old WOR cards (about 200).
  3. Ctrl+click Android: both boards show as rows (⧉ Boards' show-all does the same).
  4. Optional: copy any card file in `.darg/tasks/Main/items` to a second name. The notice names both files and the board keeps working. Delete the copy afterwards.
- **Not checked by me:** the installed copy with the fix in it (an agent can't publish), and the other machine's clone picking up the cards.
