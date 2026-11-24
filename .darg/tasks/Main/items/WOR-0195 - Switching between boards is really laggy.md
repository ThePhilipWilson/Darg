# WOR-0195: Switching between boards is really laggy

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-24
- **Done:** 2026-11-24
- **Moved:** 2026-11-24 03:05:00

## Description

Jumping between boards in the Board section is really laggy. Find out why switching is slow and make it feel instant.

## How to check

Changed: round 1 (c4da99d) virtualized each column's cards so switching boards is quick (about 0.4 s for 150 cards, was 1.5 to 2.5 s). Round 2 (ca0b262, branch `android`, wor-0195 merged) fixes the QA failure: `DragLift.Follow` now works out "before" from the list's items when the pointer is past the last card drawn, so a drop on the lower half of a cut-off bottom card lands right after it.

Try it: `pwsh -File tools/test.ps1 -Integration -Filter Dropping_on_the_cut_off` (fails without the change: 59 instead of 32), or open a project with a long column, scroll so the bottom card is half cut off, drag a card onto its lower half: it goes right after it; the drop line and the result agree.

Not changed: no auto-scroll while dragging (never existed); the last card in a column has 7 px more space under it.

## QA notes

### 2026-11-24 03:05

- **Fails: a drop near the bottom of a scrolled long column lands at the end of the column.** Scroll To Do (60 cards) so the bottom card shows its top and middle but its bottom edge is cut off, drag a card onto that card's lower half: it goes to position 59 of 60 (the very end) instead of 32 (right after the card you dropped on). On the commit before c4da99d the same drag lands at 32. Why: `DragLift.Follow` picks "before" from the card borders that exist; with virtualization the next card below the view isn't realized, so `_before` is null and `Drop` puts it last. The drop line shows under the card, so the person sees one place and gets another. The fix needs to work out "before" from the list's items (`itemsOf`) rather than the drawn borders, or have the panel realize one card past the view.
- Passed: whole suite `pwsh -File tools/test.ps1` on c4da99d in a worktree: unit 838/838, integration 207/207. `BoardSwitchTests` fails without the change (150 of 150 cards drawn on the old XAML) and passes with it; switching took 181 to 242 ms in it.
- Passed, by a QA test written for this (`QaWor0195DragTests`, in `.shots/qa-wor0195`, not committed): after scrolling a long column up and down (recycling containers), drawn cards are in on-screen order, a drop on the upper half of a card mid-view lands just before it, a drag from the scrolled column to In Progress moves the card, no card is left dimmed after the drags, and the gap between cards is 7.0 px.
- Not a regression but worth knowing: there is no auto-scroll while dragging (none in `DragLift` or `BoardView`, before or after), though How to check asks to look for it, so a card can't be dragged past what the column shows.
- Small: the cards' 7 px now comes from a bottom margin on each, so the last card in a column has 7 px more space under it than before (StackPanel Spacing put none after the last).

### 2026-11-24 QA round 2

- Passed on ca0b262 (worktree): whole suite unit 838/838, integration 208/208. `Dropping_on_the_cut_off` passes, and fails (59 instead of 32) with DragLift.cs put back to before the fix, so the test can fail. Round 1's drop failure is fixed. No auto-scroll while dragging remains as before (never existed).
