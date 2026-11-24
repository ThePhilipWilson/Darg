# WOR-0149: Board: replace the board dropdown with tabs

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** board, ui
- **Created:** 2026-11-19
- **Done:** 2026-11-19
- **Moved:** 2026-11-19 23:46:09

## Description

Turn the board selector dropdown (Main / Android) into tabs. Ctrl+click on a tab adds or removes it so more than one board shows at once. Done when: each board has a tab, a plain click shows only that board, and Ctrl+click shows several side by side.

## How to check

Changed: the board dropdown and the ⧉ Boards flyout are replaced by a tab per board beside + Board. Plain click shows only that board; Ctrl+click adds it to the boards showing (a row each) or takes it off, never the last one. Taking off the first (picked) board promotes the next. Lit tabs are the boards showing.

Branch `wor-0149` (merged and pushed to `android`), commit ae36fce.

Try: `pwsh -File tools/publish.ps1` (or ⟳ Update), open a project with 2+ boards (Main, Android). Click a tab, then Ctrl+click another: both show as rows. Ctrl+click one again to drop it. Look for: lit styling reads clearly, Ctrl held on a real mouse press registers (the test sets the Ctrl flag directly; a real pointer press wasn't exercised), several tabs fit the header.

Screenshot: from the integration test (`pwsh -File tools/test.ps1 -Integration -Filter BoardTabs`, picture in `.shots/integration`).

## QA 2026-11-19

Full suite passed (unit 617, integration 63). A scratch headless test using real pointer presses with the Ctrl modifier (MouseDown/MouseUp on the tab buttons) passed: plain click shows one board, Ctrl+click adds and removes, a plain click afterwards resets. The scratch test was deleted afterwards. Screenshot shows the lit tab clearly against the unlit one.
