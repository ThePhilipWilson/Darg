# DAR-0003: Old Done tickets and multiple boards not showing; store board data in the repo

- **Status:** In Progress
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-24 12:30:26

## Description

Philip can no longer see old tickets in the Done column, or multiple boards, as he should. Investigate why completed cards and extra boards are missing from the board view. Also: this project's data (boards, cards) should be saved in the repo (.darg inside the project repo) rather than elsewhere.

## Findings

Could not reproduce a rendering fault: a headless test with a Done column card, a Completed card (behind Show completed) and a second board shows all of them. The cause is where the data lives: `.darg/` is git-excluded (WOR-0144), and the old WOR tickets and story boards were in the Windows vault/instance, so a clone or another machine has only what was made locally (this Mac's Darg project has just DAR-0001..3). The old tickets can't be recovered from this repo.

## How to check

- What changed: `.darg/tasks` (this project's boards and cards) is now committed in the repo, force-added past the `.darg/` exclude, so the board travels with a clone. CLAUDE.md says so. New test `OldDoneAndBoardsTests` guards that Done cards and every boards-folder board show (it passes without the change: it found no rendering fault).
- Branch `dar-0003` (merged ff into mac-path-fixes); see `git log -3`.
- Try: `git ls-files .darg` lists the board files; clone the repo elsewhere and open Darg on it: the board has the cards.
- Decide: if the old WOR boards still exist on the Windows machine, copy them into `.darg/tasks/<Board>/` there and commit (I can't see them from here).
