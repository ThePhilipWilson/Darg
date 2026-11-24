# WOR-0128: Drag to the bar doesn't move the window

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-18

## Description

can't drag to bar to move window, bug

From a note on the wall (20261117-223651-0132).



## How to check

Changed: the top bar's project-tabs ListBox filled all the free space in the bar and is a `User` element for Avalonia's window chrome, so a drag on the empty bar never counted as a title-bar drag. It now sits left, covering only its tabs; the bar also got a transparent background so it is hit-testable. Commit 2ccc011 on main (branch wor-0128, merged and pushed). I couldn't drag a real window from here; the integration test hit-tests the bar's middle and fails without the fix.

Try: `pwsh -File tools/publish.ps1` (from a terminal) or ⟳ Update, then drag the empty stretch of the top bar (between the tabs and 🔧 Tools). The window should move; double-click should maximise/restore. Tabs, buttons and the usage bar should still click as before.
