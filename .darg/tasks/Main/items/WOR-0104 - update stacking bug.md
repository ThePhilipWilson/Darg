# WOR-0104: update stacking bug

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

trigger rebuild and then make change. still says nothing to change, should be able to trigger second rebuild

From a note on the wall (20261117-145834-9b19).

## How to check

**What changed:** once ⟳ Update had built, the menu stopped checking the repo and its button only restarted into that build, so a later change could never be built in. Now opening the menu compares the repo with the staged build (`Updater.StagedBuildIsStale`). If the repo has moved on, the primary button reads **Rebuild** and builds again, the note says the repo changed since the update was built, the header reads `⟳ Update ready •`, and a new **Use last build** button still restarts into the build you already have.

**Commit:** `4502296` on main (branch `wor-0104`, merged and deleted). Files: `src/Darg.Core/Updater.cs`, `src/Darg.Desktop/ViewModels/RestartViewModel.cs`, `src/Darg.Desktop/MainWindow.axaml`, `tests/Darg.Core.Tests/UpdaterTests.cs`.

**To try it:** your installed copy predates the fix, so first get the fix in with ⟳ → Update and restart (or `pwsh -File tools/publish.ps1` from a terminal). Then:
1. ⟳ → Update and restart. Wait for "Built. Restart into it when you're ready" and don't restart.
2. Save any change under `src/` (or have an agent commit one).
3. Open ⟳ again. Expect the note "…has changed since the update was built. Rebuild to take those changes in too, or restart into the build you have", three buttons (Restart · Use last build · **Rebuild**) and `⟳ Update ready •` in the header.
4. Click Rebuild. It should build again and come back to "Restart into the new build" with no dot.

**What to look for:** that the three buttons fit across the 360-wide flyout without clipping. I couldn't see the flyout on screen.
