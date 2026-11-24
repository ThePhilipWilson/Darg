# WOR-0166: Fix overlapping UI on board card header

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** ui, board
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 22:44:47

## Description

Screenshot (D:/Pictures/Screenshots/Screenshot 2026-11-20 014734.png) shows a board card (AND-0001, Android: decide the approach and scope) whose header row is cramped: the priority star and number run into the Story type badge, and the Run QA / Work on buttons sit tight against it, so the row looks overlapped. Make the header lay out cleanly at narrow card widths: nothing overlapping, wrap or shrink instead. Check all three themes and the narrowest board column width. Done when ID, priority, type badge and action buttons are fully readable with clear gaps.

## How to check

Changed: the card header in `BoardView.axaml` was a DockPanel that docked the type badge, priority and buttons to the right, so at a narrow column they landed on the id and star. Now the buttons keep their own right column and id, star, Improve, type and priority are WrapPanel children, so what doesn't fit drops to a second row.

Branch `android`, commits a466f2d and 5a2e7c3 (wor-0166 merged and removed).

Try: build and run Darg from this branch, open a board with a card in QA Ready that has a type and priority, at the narrowest column width, in Dark, Light and Girlypop (Settings). Look for: id, star, type badge, priority, Run QA and Work on all fully readable with gaps and none on another; the badges drop to a second row when tight.

Pictures: `pwsh -File tools/test.ps1 -Integration -Filter CardHeader` writes the three themes to `.shots/integration/A_full_card_header_has_no_overlap_in_any_theme/header-<theme>.png`.
