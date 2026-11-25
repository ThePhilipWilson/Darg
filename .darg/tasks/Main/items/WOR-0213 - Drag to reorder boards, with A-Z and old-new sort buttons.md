# WOR-0213: Drag to reorder boards, with A-Z and old-new sort buttons

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 12:03:41

## Description

Philip wants to reorder boards by dragging them, and to have a button to sort boards A-Z and another to sort old-new (by creation date). Open points: where the order is saved so it survives restarts, and whether a sort button replaces the manual order or just applies once.

Decided with Philip (2026-11-25): a sort button sorts once and that becomes the saved order; the order is kept in this instance.

## Decision answered 2026-11-25 11:49

**Question:** Do the 🔤 → and 🕓 → sort buttons beside + Board now look right, with the arrows you asked for?
**Asked by:** agent QA, 2026-11-25
- **Looks fine** (recommended) → done: keep the icons with arrows as built.
- **Different arrow** → back: sent back; say which arrow (↓, ⇅, one on each side) or other change.
**Context:** Round 3 QA: full suite green (897 unit + 243 integration, 0 failed); the fresh integration shot shows the abc icon and the clock each with an arrow, drawn, in the tab row. Only how it looks is yours to judge. ![sort icons with arrows](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0213/board-tabs-arrows.png)

**Answer:** the up down arrow (2026-11-25 11:49)

**Answer:** Different arrow (picked in the QA agent's question, which gave no arrow; ask which: down, up-down, or one on each side) (2026-11-25)

## Decision answered 2026-11-25 11:25

**Question:** Do the icon buttons beside + Board look right in this fresh screenshot (taken 11:22 from the current code)?
**Asked by:** agent 23b9d3b915, 2026-11-25
- **Looks fine** (recommended) → done: keep the icons as built.
- **Icons need work** → back: sent back; say what to change.
**Context:** The old text you saw is the installed copy (or the earlier 10:21 screenshot) from before commit 041d6ac; the code on `android` has the icons, and the new shot shows an abc icon (A to Z) and a clock (old-new) drawing fine. Update Darg (⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal) to see them live. ![icons](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0213/board-tab-icons.png)

**Answer:** only 1 of the screenshots showed the icons, still needs the arrows next to it (2026-11-25 11:25)

## Decision answered 2026-11-25 11:22

**Question:** Do the 🔤 (A to Z) and 🕓 (old-new) icon buttons look right beside + Board?
**Asked by:** agent QA, 2026-11-25
- **Looks fine** (recommended) → done: keep the icons as built.
- **Icons need work** → back: sent back; say in your notes what to change (different icons, drawn shapes instead of emoji, spacing).
**Context:** Round 2 QA: full suite green (874 unit + 240 integration, 0 failed); BoardView.axaml has both icon buttons with the sort commands and automation names, and the sort tests pass. Not checked: whether the emoji actually draw (not empty boxes) on screen, because headless Skia shots and WebView-free UI can't be judged for glyph fallback by automation; it needs one look at the live app.

**Answer:** not sure if the screenshot is the correct one, still seing the old text (2026-11-25 11:22)

## Decision answered 2026-11-25 10:46

**Question:** The A–Z and Old–new buttons take about 120 px of the board tab row; keep them as two buttons or fold them into one ⇅ menu button?
**Asked by:** agent QA, 2026-11-25
- **Keep two buttons** (recommended) → done: as built; the card goes to done.
- **One ⇅ menu button** → back: the card is sent back to replace the two buttons with one menu button.
**Context:** QA passed (full suite 869 unit + 237 integration, all green; the drag/sort tests cover order kept, sort once, new boards at the end). Only the width/look is yours to judge. ![dragging](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0213/board-tab-dragging.png)

**Answer:** can we not do icons for both? (2026-11-25 10:46)

## How to check

**What changed:** board tabs drag along the row into a new order (the tab follows the pointer, the others slide aside), and two buttons beside + Board, **A–Z** and **Old–new**, sort them once. The order is kept per project in the instance (`.darg/board-order`, or `<instance>/projects/<id>/board-order`, beside `shown-boards`); new boards go at the end; with several boards showing, the rows below the picked one follow the tab order. Old–new goes by when each board's folder was made. The inbox (where new cards land) is still worked out A to Z, so reordering never changes it.

**Commits** on `android`: 6f737f1 (the work and tests), 0797323 (CLAUDE.md line).

**Try it:** update Darg (⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal), open Darg's own Board section, and:
1. Drag a tab (say Main) to another place in the row and let go: it lands where the others made room, and what shows doesn't change (a drag isn't a click). A plain click still picks a tab; Ctrl+click still adds one.
2. Close and reopen Darg: the order is as you left it.
3. Press **Old–new**: Main and Android first (made 2026-11-20), then today's plan boards in the order they were made. Press **A–Z**: back to alphabetical. Drag again afterwards: it carries on from the sort.

**What to look for:** the two buttons take about 120 px of the tab row, so on a narrow window one or two fewer tabs fit before the ▾ n more menu (the overflow test had to go from 1280 to 1400 px wide to keep two long-named tabs in the row). If that's too much, they could become one ⇅ button with a menu. Dragging only reaches the tabs in the row, not those in ▾ more.

Screenshot from the integration test, mid-drag: ![dragging](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0213/board-tab-dragging.png)

## How to check (round 2, icons)

Per your answer the two sort buttons are now icons: 🔤 (A to Z) and 🕓 (old-new), tooltips as before. Commit 041d6ac on `android`. Update Darg, open the Board section, check both icons draw (not empty boxes), fit beside + Board, and still sort once on a press. Not seen on screen: headless test shots may not draw emoji.

## How to check (round 3, arrows)

Per your answer each sort icon now has an arrow beside it: 🔤 → (A to Z) and 🕓 → (oldest to newest). Commit 9b21921 on `android` (pushed). Full suite green (897 unit, 242 integration). Screenshot from the integration test: ![sort icons with arrows](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0213/board-tabs-arrows.png). Update Darg and look at the Board tab row. If you meant a different arrow (↓, ⇅, or one on each side), say which.

## How to check (round 4, up-down arrow)

Per your pick both sort icons now take the up-down arrow: 🔤 ⇅ (A to Z) and 🕓 ⇅ (old-new). Commit 80d7e88 on `android` (pushed); the 6 Tabs_ integration tests pass. Screenshot: ![sort icons, up-down arrow](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0213/board-tabs-updown.png). Update Darg and look at the Board tab row.

## How to check (round 5, same as round 4)

Nothing new was built this round: the up-down arrow (80d7e88 on `android`, pushed) is what you picked. Re-ran the whole suite: 897 unit pass, 242 of 243 integration pass; the one failure, `BoardSwitchTests.A_big_board_draws_only_the_cards_in_view_and_switching_stays_quick`, is a switch-time check (one switch took 1413 ms) that failed while other agents' suites shared the machine, and passes alone. Look at the Board tab row in an updated Darg: ![sort icons, up-down arrow](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0213/board-tabs-updown.png)
