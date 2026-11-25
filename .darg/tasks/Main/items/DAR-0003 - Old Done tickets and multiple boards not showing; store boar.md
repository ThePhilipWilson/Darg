# DAR-0003: Old Done tickets and multiple boards not showing; store board data in the repo

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-25 02:30:00

## Description

Philip can no longer see old tickets in the Done column, or multiple boards, as he should. Investigate why completed cards and extra boards are missing from the board view. Also: this project's data (boards, cards) should be saved in the repo (.darg inside the project repo) rather than elsewhere.

## Decision answered 2026-11-25 02:16

**Question:** Does the installed copy, after ⟳ Update, show the Board with no "Couldn't read the board" line and the old WOR cards in Done?
**Asked by:** agent QA (DAR-0003), 2026-11-25
- **Yes, it shows them** (recommended) → done: the fix works on the real board; card goes to done.
- **No, still wrong** → back: sent back with your notes on what the installed copy shows.
**Context:** Automated checks passed, but an agent can't publish the installed copy, so only you can confirm the real board with its ~200 cards on it.

**Answer:** have we pushed this change live, I won't be able to pull it down from git on the other machine until that is done (2026-11-25 02:16)

## Findings

Could not reproduce a rendering fault: a headless test with a Done column card, a Completed card (behind Show completed) and a second board shows all of them. The cause is where the data lives: `.darg/` is git-excluded (WOR-0144), and the old WOR tickets and story boards were in the Windows vault/instance, so a clone or another machine has only what was made locally (this Mac's Darg project has just DAR-0001..3). The old tickets can't be recovered from this repo.

## Findings (2026-11-25, Windows)

Reproduced on the installed copy: the board said "Couldn't read the board: An item with the same key has already been added. Key: WOR-0011". Main had two files each for WOR-0011 and WOR-0198. The name scrub (2dbf652) renamed them in git, but the checkout left the old-named files behind, because `.darg/` was git-ignored. `MovedWatch.Read` keyed a dictionary by card id, so every read of the board threw after loading and before redrawing. Main froze with stale lists, Done included, and with the error in place of its rows. Earlier, the 13:05 cherry-pick of 54ada8c had replaced this machine's Main/board.json with the other machine's DAR one (found by agent 6735f032fb).

## How to check

- **What changed:**
  - 9ad8890 on `android`: a board with two card files on one id reads instead of throwing. Both cards show, and the notice line names the files ("WOR-0011 is in 2 files on Main: ... Delete the stale copy in its items folder").
  - 11449b8 and 8d2496d (agent 6735f032fb): a root `.gitignore` keeps `.darg/tasks` tracked, so new cards are no longer ignored, and this machine's board.json and new cards are committed.
  - The two stale copies were moved out of the board to `D:/GitHub/ThePhilipWilson/Darg/.shots/dar-0003/stale-cards/`.
  - 2026-11-25 02:30 (agent f0b8314a27, after your question "have we pushed this live"): all of the above was already on GitHub, but only on `origin/android`; `origin/main` had 4 card files and none of the fix. With your OK, GitHub's `main` was fast-forwarded to `android` (b8961f4..08d3779, 144 commits, no force; checked first for the names never to publish). Pulling `main` on the other machine now brings the fix and the 221 card files. Full suite on 08d3779 (what main now holds): unit 856/856, integration 219/220; the one failure is again `TerminalReadTests.Terminal_command_returns_the_last_lines_a_console_shows`, which fails before this card's commits too and has no card of its own yet.
- **Tests:**
  - Command: `pwsh -File tools/test.ps1 -Filter "Two_files_with_one_id|A_card_in_two_files"`. Both tests fail without the fix: the unit one with the live error, the integration one because the board stayed frozen.
  - The notice with two boards showing: ![duplicate id notice](D:/GitHub/ThePhilipWilson/Darg/.shots/dar-0003/duplicate-id.png)
- **To try:**
  0. On the other machine: `git pull` on `main` (or `git fetch && git checkout android`); `git log -1 --oneline` should show 08d3779 or later. That machine's DAR-0001..3 were committed from it (ea09c45), so the pull should merge cleanly; if git refuses over local changes in `.darg/tasks`, commit or stash them first.
  1. Run ⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal, so the installed copy has 9ad8890.
  2. Open Darg's Board. There should be no "Couldn't read the board" line. Scroll right: Done holds the old WOR cards (about 200).
  3. Ctrl+click Android: both boards show as rows (⧉ Boards' show-all does the same).
  4. Optional: copy any card file in `.darg/tasks/Main/items` to a second name. The notice names both files and the board keeps working. Delete the copy afterwards.
- **Not checked by me:** the installed copy with the fix in it (an agent can't publish), and the other machine's clone picking up the cards.

## QA (2026-11-25, agent)

- Branch `android` at 86f1141 (the card's commits are in it). Full suite `tools/test.ps1`: unit 849 passed; integration 216 passed, 1 failed.
- The failure is `TerminalReadTests.Terminal_command_returns_the_last_lines_a_console_shows` ("marker-omega-9152" not in the console read). It also fails alone and on a worktree at 37132f4 (before this card's commits), so it is not caused by this work, but it is a real failing test and needs its own card.
- `Two_files_with_one_id` (unit) and `A_card_in_two_files...` (integration) pass; the screenshot duplicate-id.png shows the notice naming both files with two boards showing.
- Real data: no id appears in two files in `.darg/tasks/Main/items` (208 files); the two stale copies are in `.shots/dar-0003/stale-cards`; `.gitignore` no longer ignores `.darg/tasks` (git check-ignore finds nothing; 222 tracked files there); tracked cards hold none of the private project names. 202 cards are Done/Completed.
- Not checked: the installed copy (agent can't publish, so the real board's rendering is unseen) and another machine's clone. Cannot be automated here for that reason.
- Note: docs/ (PLAN.md, FEATURE-PLANNING.md, architecture.md) name "Tatty" in committed text, which the project rules list as a name to avoid; predates this card.
