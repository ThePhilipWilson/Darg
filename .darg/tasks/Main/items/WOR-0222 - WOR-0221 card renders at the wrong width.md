# WOR-0222: WOR-0221 card renders at the wrong width

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-27 12:37:10

## Description

On the Main board, the card WOR-0221 was shown at the wrong width compared with the other cards. Creating this ticket appears to have fixed it, so the cause is unknown; find out why that one card differed (long title, labels, an agent badge, blocked-by chips, or something in its file) so it can't recur.

Also: the "improve work on bug" text on the card is split over two lines. It should show on one line. Check whether the two issues share a cause (the width and the wrapping look related).

## Decision answered 2026-11-27 12:04

![a thin Story card: the Story pill drops under the id](D:/GitHub/ThePhilipWilson/Darg/.shots/qa-wor0222/story-header.png)

**Question:** The width fix and the Bug pill both pass QA, but a thin Feature or Story card's pill still drops to a second line. Send it back to fit those too, or take it as done?
**Asked by:** agent (QA of WOR-0222), 2026-11-27
- **Send back for Feature and Story** (recommended) → back: the rework makes the header fit every type's pill on a thin card. Story misses by 2 px and Feature by 12 px, so it's the same bug for the other types.
- **Done as it is** → done: the case you reported (Bug) is fixed. Feature and Story keep wrapping.
- **Done, with a new card for the rest**: the card goes to done and a new backlog card is made for the Feature and Story pills.

**Context:** In a 290 px column the header has 183 px beside ▶ Work on. Id, star and ✦ Improve take 147 px with gaps, leaving 36 px. Bug is 31 px, Story 38 px and Feature 48 px.

**Answer:** Send back for Feature and Story (2026-11-27 12:04)

## How to check

**This round** (QA sent it back: a thin Feature or Story card's pill still dropped under the id). Commit `245977d` on `android`, on top of round one's `10f2c30` and `b408034`.

**What it was.** In a 290 px column the header has 187 px beside ▶ Work on. Id (52), star (14) and ✦ Improve (63) with three 6 px gaps left 36 px for the pill. Story needs 38 and Feature 48.

**What changed:**
- `src/Darg.Desktop/Views/BoardView.axaml`: the tile header's items sit 4 px apart (were 6), the star loses its 2 px right margin, the header's buttons (✦ Improve, Run QA, ▶ Work on) take 4 px padding a side (were 6), and its type and priority pills take 5 (were 6). Feature now fits with 6 px to spare. Only the tile header changed. The card panel's buttons are as they were.
- `src/Darg.Desktop/Views/TileHead.cs`: Run QA and Work on go under the id when any header item that can't trim (✦ Improve, a pill) would be cut beside them, not only the id. Without this, the slimmer buttons let the id alone fit beside them in a By state sub-column, and ✦ Improve was cut to "✦ Impro" in a strip as wide as the id. `AllBoardTests`' sub-column test caught it.
- `tests/Darg.Desktop.Tests/TileWidthTests.cs`: the pill test runs for Bug, Story and Feature. Before the fix Story and Feature failed and Bug passed.

**Try it.** Restart onto the new build (⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal), then on the Main board:
1. Look at a thin Feature or Story card in a column at its usual width (one showing ★, ✦ Improve and ▶ Work on; set a card's Type to Feature in its panel if none is thin). Id, star, ✦ Improve, the pill and ▶ Work on should all sit on one line.
2. Check the header doesn't look cramped to you: the gaps are 4 px now, not 6.
3. Turn By state on. In a narrow sub-column (QA ready, with Run QA and Work on), the buttons go under the id and ✦ Improve shows whole.
4. Round one's fixes should still hold: every tile fills its column's width.

Screenshots from the tests: ![a thin Feature card's header on one line](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0222/feature-header.png) ![a QA ready sub-column: buttons under the id, Improve whole](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0222/narrow-sub-column.png)

**Not covered.** A type longer than Feature (a custom one such as "Improvement") still wraps under the id on a thin card. Only Task, Bug and Story are built in.

## QA notes

**2026-11-27, QA agent.** Checked on branch `android` at a80b868, which holds 10f2c30 and b408034.

- **Passed:**
  - **Whole suite** (`pwsh -File tools/test.ps1`): 986 of 986 unit tests and 303 of 304 integration tests passed, including both TileWidthTests and AllBoardTests' sub-column stacking (Try it, step 3).
  - **Each test fails without its fix.** I checked this in a throwaway worktree. With the `<Panel>` change reverted, only `Every_tile_fills_its_column…` fails ("T-0002 is 265 px wide in a column with room for 272"). With the header ItemSpacing reverted, only `A_thin_bugs_pill…` fails (the Bug pill wants 37 px and goes under the id).
  - **Screenshots** from the run (tiles.png, bug-header.png): a lone short card fills Backlog edge to edge, and id ★ ✦ Improve Bug ▶ Work on sit on one line.
- **The one failing test:** `DecisionTests.A_lowercase_decision_heading_is_answered_like_any_other` failed once in the full run. The card's Status read "For Human Attention" instead of "Done" straight after `decide`. It is intermittent and not caused by this card's work. All 21 DecisionTests passed 3 runs out of 3 at HEAD, and 3 out of 3 with this card's BoardView.axaml and TileHead.cs reverted. It looks like a timing race under the full suite's load and is worth a card of its own. An earlier QA run of this card failed a different test once, `PhoneBoardTests.A_tap_on_a_tile_opens_it_and_a_long_press_puts_up_its_move_sheet`.
- **Same bug, other types:** a scratch copy of the pill test with type Feature and with type Story (not committed) fails for both. Story's pill is 38 px against the 36 px left, and Feature's is 48 px. The card's "Not covered" note says about 12 px for both, but for Story it is only 2 px. Screenshots: ![Feature](D:/GitHub/ThePhilipWilson/Darg/.shots/qa-wor0222/feature-header.png) ![Story](D:/GitHub/ThePhilipWilson/Darg/.shots/qa-wor0222/story-header.png)
- **Not checked:** the live app on the real Main board (Try it, steps 1 to 3 by hand). The headless tests draw the real MainWindow on a throwaway instance, so the same layout is covered there.

### 2026-11-27 12:04

The width fix and the Bug pill both pass QA, but a thin Feature or Story card's pill still drops to a second line. Send it back to fit those too, or take it as done? I picked: Send back for Feature and Story.
