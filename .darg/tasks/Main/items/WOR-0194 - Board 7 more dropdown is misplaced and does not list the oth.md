# WOR-0194: Board "7 more" dropdown is misplaced and does not list the other boards

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-24
- **Done:** 2026-11-24
- **Moved:** 2026-11-24 02:16:32

## Description

On the Board section, the "7 more" dropdown is in the wrong position and does not open to show the other boards. Expected: it sits correctly in the board tab bar and opens a list of the remaining boards to pick from.

## How to check

Changed: `OverflowRow` in `src/Darg.Desktop/Views/OverflowTabs.cs` now fills the ▾ n more menu as the row is measured (the flyout built its list before the Opening handler ran, so it opened empty), and the button's chip margin is 0 like the tabs (it sat about 3px above them). Commit 7241536 on `android` (pushed).

Try: `pwsh -File tools/publish.ps1` (or ⟳ Update), or `dotnet build` and run the debug exe; open the Board section with more boards than fit the row (narrow the window if needed).

Look for: the ▾ n more button level with the board tabs; clicking it drops a list of exactly the boards not in the row; picking one shows it alone, lit in the row. I saw it only headless (the popup is not in screenshots), so the real window is the part to eyeball.

## QA notes

2026-11-24, QA agent: passed.

- Whole suite on 7241536 (its own worktree): unit 838/838, integration 206/206.
- The new test can fail: with the old OverflowTabs.cs it fails on the level check; with only the measure-time FillMenu() taken out it fails on the list (expected 12, got 0).
- Real window (debug build, throwaway instance with Main and 13 long-named boards, 1400x900, driven by UI Automation Invoke): the button shows "▾ 12 more", same top (103) and height (26) as the tabs, 6px after the last tab. Opened, it is a popup window right under the button (its top is the button's bottom), centred on it, listing exactly the 12 boards not in the row, in board order. Picking board 13 put it in the row lit beside Main, closed the list, and the reopened list held the other 12 without board 13. No crash.log.
- Captures: `D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-wor-0194/` (row-crop.png, popup.png, picked-crop.png, and the scripts more-menu.ps1 and popup-shot.ps1).
