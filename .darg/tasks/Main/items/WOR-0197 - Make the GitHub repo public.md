# WOR-0197: Make the GitHub repo public

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-24 11:57:46

## Description

The repo does not appear to be public on GitHub. Check its visibility, then make it public (README, licence, and a check that no secrets or private paths are in the history first).

## How to check

What changed: the repo is now **public** (https://github.com/ThePhilipWilson/Darg), MIT licensed, with a README. Before that, history was rewritten with git-filter-repo: Alpha, Beta, Gamma, Delta, "Project C" and "Card Roguelike" became Alpha, Beta, Gamma, Delta, Project C and Card Roguelike in every file and commit message. Kept on purpose: the board app, Obsidian, claude-draw, ThePhilipWilson/Site, the D:/Apps and D:/Android paths, the VOT card prefix. The scan found no keys, tokens, private keys or emails other than the commit author.

Branches on origin (force-pushed, new ids): main c78bb71, android 46b6763, wor-0186 e54876e, wor-0187 9fb878d. Old to new commit ids: D:/Temp/darg-commit-map.txt.

**Your local repo still has the OLD history** and must be re-synced before anyone pushes. I did not touch it (another agent has an uncommitted BoardViewModel.cs change in it, and about 10 worktrees use it). To do it: commit or save that change, `git fetch origin`, point main and android at origin/main and origin/android, and for each local-only branch (wor-0083, wor-0093, wor-0171, wor-0176, wor-0183) `git rebase --onto <new> <old> <branch>` using the map. Easier: a fresh `git clone`, then re-attach worktrees.

Look at: `gh repo view ThePhilipWilson/Darg --json visibility,licenseInfo`; open the repo logged out in a browser; search it for the old project names (should find none).

Caution: GitHub may still serve the old private commits by SHA for a while; ask GitHub support to purge if that matters. Tests on the rewritten tree: unit 840/840, integration 208/208 (ProjectDetectTests prefixes updated for the renamed projects).
