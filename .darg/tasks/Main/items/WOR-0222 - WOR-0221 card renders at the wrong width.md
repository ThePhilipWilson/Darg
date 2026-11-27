# WOR-0222: WOR-0221 card renders at the wrong width

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-27 03:06:57

## Description

On the Main board, the card WOR-0221 was shown at the wrong width compared with the other cards. Creating this ticket appears to have fixed it, so the cause is unknown; find out why that one card differed (long title, labels, an agent badge, blocked-by chips, or something in its file) so it can't recur.

Also: the "improve work on bug" text on the card is split over two lines. It should show on one line. Check whether the two issues share a cause (the width and the wrapping look related).

## How to check

**What it was.** Both halves had a cause in the tile layout, and they were related:

- **Width.** Each column's DockPanel fills its *last* child. Since WOR-0214 (2026-11-25) that is the By state list, which is hidden unless By state is on. The plain card list sat before it, docked left, so it was only as wide as its widest card. A column whose cards all had short titles and headers drew its tiles narrower than the other columns' (223 px against 272 in a test). A card with a longer title landing in that column widened them all again. WOR-0222's own title did that when the ticket was made, which is why making it seemed to fix WOR-0221.
- **Wrap.** Every item in the tile's header row (id, star, ✦ Improve, type, priority) carried a 6 px right margin, the last one included. So id, star, Improve and Bug needed 184 px of the 183 px left beside ▶ Work on, and Bug dropped to a second line. A wrapped header also made the card's content narrower, which fed the width bug.

**What changed** (branch `android`, commits `10f2c30` and `b408034`):
- `src/Darg.Desktop/Views/BoardView.axaml`: the card list and the By state list share the DockPanel's last place inside a `Panel`, so whichever shows fills the column. The header's WrapPanel uses `ItemSpacing="6"` instead of right margins.
- `src/Darg.Desktop/Views/TileHead.cs`: counts that spacing after the id, so a By state sub-column still puts Run QA and Work on under the id as before.
- `tests/Darg.Desktop.Tests/TileWidthTests.cs`: two tests, each checked to fail without its half of the change.

**Try it.** Restart onto the new build (⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal). Then on the Main board:
1. Find a column holding only short cards (or turn Show completed off so a column has one or two). Its tiles should be as wide as every other column's, filling the column to its edges.
2. Find a thin Bug card that shows ★, ✦ Improve, Bug and ▶ Work on (a new Bug card does). All four should sit on one line with the id.
3. Turn By state on: in a narrow sub-column, Run QA and Work on still go under the id.

Screenshots from the tests: ![a lone short card fills Backlog](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0222/tiles.png) ![thin Bug card header on one line](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0222/bug-header.png)

**Not covered.** Feature and Story pills are wider than Bug. On a thin card beside ✦ Improve they still drop to a second line in a 290 px column, because the line is about 12 px too short for them.
