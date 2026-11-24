# WOR-0193: Board tabs that don't fit go in a more menu

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-23
- **Done:**
- **Moved:** 2026-11-23 12:20:58

## Description

> Projects with many boards (Alpha has lots) run past the edge of the window and the extra boards stop being viewable. Needs some UX to fix it, e.g. a scrollable or wrapping board switcher, an overflow menu, a dropdown/search for boards, or collapsing less-used boards. Raised from Alpha's Board section.

The board tabs (WOR-0149) sit in one row that grows as wide as it needs to, so a project with 14 boards (Alpha, many with long names) pushes boards past the window's edge, along with the header's other controls. The fix keeps the tabs in one row: the tabs that fit show, and a **▾ n more** button at the end of the row lists the rest. Picking a board from that list does what clicking its tab does, and it then shows as a tab in the row. Ctrl+click still works on the tabs in the row (Philip's pick, 2026-11-23, over wrapping the tabs or a searchable dropdown).

**Where:**
- The Board section's header row: `src/Darg.Desktop/Views/BoardView.axaml`, the `BoardTabs` ItemsControl (a horizontal `StackPanel` docked left, between + Board and the filter box).
- `src/Darg.Desktop/ViewModels/BoardViewModel.cs`: `Tabs`, kept up to date around line 242, and `BoardTab` near the end of the file (`Pick`, `TabCtrl`, `PickBoard`).
- Tests: `tests/Darg.Desktop.Tests/BoardTabsTests.cs`.

**Done when:**
- A board with 14 boards with long names, in a 1280 px wide window, shows: the tabs that fit, then ▾ n more. Every board can be reached from the row or the menu, and + Card, Show completed and the filter box stay on screen.
- Picking a board from the menu shows only that board, and it then shows as a lit tab in the row. Ctrl+click on a row tab still adds or removes it.
- Boards that are showing (lit) never hide in the menu while the row has room for them.
- With few boards, nothing changes: there's no ▾ button and the row looks as it does now.
- Making the window narrower or wider moves tabs between the row and the menu.
- An integration test in `BoardTabsTests` with many long-named boards checks the above and fails on today's code. Its screenshot is in `.shots/integration`.

**Questions left:**
- Does the menu need a way to Ctrl-add a board (to show several at once), or is picking a board, then Ctrl+clicking others in the row, enough?

## How to check

Changed: the board tabs row is now `OverflowRow` / `OverflowTabsPanel` (`src/Darg.Desktop/Views/OverflowTabs.cs`, used in `BoardView.axaml`). Tabs that fit show (lit ones first, then in order); a **▾ n more** button lists the rest; picking one from it is a plain tab click. The row keeps 200 px free for the filter box.

Branch/commit: `android`, 153485d (branch wor-0193 merged and pushed; the worktree is removed).

Try it: run the Debug build on Alpha (14 boards) or any project with many boards, open Board, resize the window. Or open `.shots/integration/Tabs_that_dont_fit_go_in_a_more_menu/board-tabs-overflow.png` (in the repo) for the 1280 px case. Test: `pwsh -File tools/test.ps1 -Filter BoardTabs`.

Look for: ▾ n more opens a menu of the hidden boards (I couldn't click the real popup in the headless test; the test calls the same PickCommand the menu items use); the picked board becomes a lit tab; + Card, Show completed and the filter stay on screen; with few boards no button.

Not decided: the open question (Ctrl-add from the menu). I left it out: pick from the menu, then Ctrl+click others in the row.

## QA notes

- 2026-11-23 QA (153485d, worktree `.shots/qa-0193`): the full suite `pwsh -File tools/test.ps1` passes (unit 834, integration 186, BoardTabs included). These check out: the row, the ▾ n more label matching what's hidden, + Card / Show completed / filter staying on screen, resizing, and Ctrl+click adding and removing with a real headless mouse. Three things don't:
  - **The ▾ n more menu opens empty, so the hidden boards can't be reached (fails "Every board can be reached").** In the real Debug app on a throwaway instance with 14 boards at 1280 px, a real mouse click on ▾ 12 more shows only its tooltip, and UI Automation finds no MenuItem in the process (![more clicked](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0193-evidence/real-app-more-clicked.png)). Headless shows the same: the flyout's `Items` holds the 11 hidden boards, but the open `MenuFlyoutPresenter` has `ItemCount` 0. The cause, checked in `QaFlyoutProbeTests.cs`: in Avalonia 12.1.6, a `MenuFlyout` filled in its own `Opening` event opens with 0 items. That holds for Items and ItemsSource, on the first open and later ones. A flyout filled before it opens shows its items. `OverflowRow` fills `_menu` in `Opening`. Fill it before it opens instead, for example when `Hidden` changes in measure, or by binding ItemsSource to a list kept up to date. BoardTabsTests missed this because it calls `PickCommand` directly. The test should open the real flyout and check its presenter's items, or click one in the popup.
  - **A lit tab with a long name shrinks to a blank pill at about 900 px or less.** After picking "Zz last one with a long name too" at 900 px, the row shows a blank pink pill and ▾ 13 more (![900 px](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0193-evidence/w900-lit-tab-blob.png)). At 700 px, and at 800 px with many boards lit, it looks the same (rows at 700 / 1280 / 800 / 1600 px: ![rows](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0193-evidence/tab-rows-700-1280-800-1600.png)). The lit tab is "taken first, clipped if need be", but clipping the button drops its text instead of trimming it. Half a 1920 px screen is 960 px, so this width is realistic. Trim the name with an ellipsis, or cap the tab's width, so the lit board is still named.
  - Minor: pressing Tab in the row moves focus on to the hidden tabs parked at x = -99843, so focus lands on buttons you can't see. Set `IsTabStop` or `Focusable` to false on the hidden ones, as well as `IsHitTestVisible`.
  - Not checked: whether the new test fails on the old code. It uses `OverflowTabsPanel`, so on the old code it doesn't compile, which proves nothing.
  - The scratch tests and UI Automation scripts are in `D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0193-evidence/`. `QaOverflowTests.cs` checks the real mouse, the counts and the lit tabs at 1280, 900, 700, 1600 and 2400 px. `QaFlyoutProbeTests.cs` is the probe of how the flyout gets filled. `uia.ps1` and `click.ps1` are the real-app check. Verdict: needs more work.
