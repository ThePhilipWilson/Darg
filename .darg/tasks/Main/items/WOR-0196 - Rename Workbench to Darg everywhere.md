# WOR-0196: Rename Workbench to Darg everywhere

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-24 11:16:08

## Description

The app is still called Workbench in places rather than Darg. Find and rename every user-facing and project-level occurrence (window title, UI text, docs, package/app name, repo name where sensible).

## How to check

**What changed** (commit `ceaabba` on `android`, https://github.com/ThePhilipWilson/Darg/commit/ceaabba):
- The window, app, csproj names, Android label and GitHub repo already said Darg. What still said Workbench where you'd see it: the project tab (fixed in your `instance.json`: `"name": "Darg"`, id stays `workbench`) and your data folder, still `%APPDATA%\Workbench\home`. It had never moved because `%APPDATA%\Darg\models` (the dictation model) existed first. `MigrateFromWorkbench` now moves `Workbench\home` into the existing Darg folder and leaves a link at the old path.
- `tools/rename-folder.ps1` moves this repo's folder to `D:\GitHub\ThePhilipWilson\Darg` along with everything that names it: worktrees, the instance's paths and saved agents, `~/.claude/projects` sessions and memory, `~/.claude.json`, `D:\Apps\Darg\source.txt` and shortcuts. It does a dry run unless given `-Apply`.
- The perf scripts no longer hardcode the old paths. CLAUDE.md lists what says Workbench on purpose (compatibility, history docs, the `WOR` prefix and `workbench` id).

**To try:**
1. Update Darg (⟳ Update or `pwsh -File tools/publish.ps1` from a terminal). Once it is back: the project tab reads **Darg**, `%APPDATA%\Darg\home\instance.json` exists, and `%APPDATA%\Workbench\home` is a link to it (`dir %APPDATA%\Workbench` shows `<JUNCTION>` or `<SYMLINKD>`). Open an old Fettle chat and send it a line: it should carry on, not start over. If the folder didn't move (something held it open), it moves on the next start.
2. Repo folder (optional): close Darg, open a terminal outside the folder, run `pwsh -File D:\GitHub\ThePhilipWilson\Workbench\tools\rename-folder.ps1` to read the plan, then again with `-Apply`. Start Darg from the Start menu. The project should open from `D:\GitHub\ThePhilipWilson\Darg` with its agents listed, and `git -C D:\GitHub\ThePhilipWilson\Darg worktree list` should show the new paths.
3. The old install `D:\Apps\Workbench` is still there. Delete it if nothing uses it.
