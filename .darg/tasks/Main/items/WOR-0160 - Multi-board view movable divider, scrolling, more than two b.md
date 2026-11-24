# WOR-0160: Multi-board view: movable divider, scrolling, more than two boards

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-21 00:38:37

## Description

When showing multiple boards at once, the divider between them needs to be draggable so each board can be resized. Each board needs to scroll. Only two fitting on screen is limiting, so it should be possible to make boards bigger (and show more than two at a time).

## How to check

**What changed:** with two or more boards showing, each row has a divider under it (a thin line, pink on hover). Dragging it makes that board taller or shorter (no smaller than 140 px); the height is kept per board (`projects/<project>/board-heights` in the instance), and a double-click puts it back to sharing. Rows you haven't dragged share what's left, down to 180 px each, so three or more fit at once (before, it was at most three and each at least 320, so two on most screens). Once the rows are taller than the screen, the rows scroll, with the wheel too; each column still scrolls its own cards, and each row scrolls sideways.

**Commits:** 4058d12 (the feature), b33d66f (a fix to WOR-0165's test, which left fake turns at 60 s and failed DoneCardTests), on `android`, pushed.

**Try it:** update Darg (⟳ menu) or run `pwsh -File tools/publish.ps1` from a terminal. Open the Board, Ctrl+click two more board tabs so three show.
- All three fit on screen with no scrolling.
- Drag the line under the first board down: it grows, the others shrink to 180 then the whole thing scrolls. Let go, switch section and back: the height stays.
- Double-click the line: that board shares the height again.
- Wheel over a board's empty column scrolls the rows; over a full column it scrolls that column's cards first.

Not done: column width is still fixed at 290 (I read "bigger" as taller); the double-click itself isn't exercised by the test (the headless pointer never counts a second click), only its command.

![three boards](D:/GitHub/ThePhilipWilson/Workbench/.darg/artifacts/f1fbad203a/three-boards.png)
![first board dragged taller](D:/GitHub/ThePhilipWilson/Workbench/.darg/artifacts/f1fbad203a/main-dragged-taller.png)

## QA notes

- 2026-11-20 QA (built d475dfe, which includes 4058d12 and b33d66f, in a worktree of its own). **One thing to fix: a single click on a divider, with no drag, pins that board's height.** The press starts a drag and the release saves `startH + 0`, so the board's current shared height goes into `board-heights` as if it had been dragged there. From then on that row stops sharing when the window is resized or rows come and go, until it is double-clicked. A scratch integration test (press and release on Main's divider in the three-board view) found `Main 258` in `board-heights` afterwards. Fix: save on release only if the pointer moved (a few px), and add an integration test that fails without that. The double-click still works with that fix, since its first click wouldn't pin anything.
- Suite (`pwsh -File tools/test.ps1`): unit 765/765. Integration 124/125 on the first run: **`DoneCardTests.A_card_moved_to_done_gets_its_done_report_then_its_finished_agent_goes` failed** at line 40 (`Assert.False(File.Exists(qa))`: the QA report was not yet archived when the agent left the list). It then passed 3 times on its own (`-Filter DoneCardTests`, 6/6 each time), and a second full integration run was 125/125, so it is intermittent (1 failure in 5 runs). This card's diff doesn't touch report flow or agent clean-up (it touches StoryBoards.cs `BoardRowHeights`, the row sizing in `BoardViewModel` and `BoardView`). I did not run it at a commit before this card, so it is untested whether it also fails without this work. Worth a ticket of its own: it is a race between `ReportFlow`'s archiving and the agent being forgotten.
- Passed:
  - **BoardRowsTests** (the card's own).
  - **Scratch headless tests** (kept at `.shots/qa-0160-QaWor0160Tests.cs.txt`, not committed):
    - The wheel over a full column scrolls that column's cards and leaves the rows where they were.
    - Dragging a divider up 2000 px stops at 140, and the other row takes the rest.
    - Four boards fit at 180 or more each; five scroll.
    - A height saved in `board-heights` is applied when the app starts.
    - Hiding the second row leaves one row filling the space, with no divider showing.
    - Making the window taller keeps the dragged row's height and shares the rest.
  - **Real app, real mouse** (debug exe on a throwaway instance, `.shots/qa-0160-live.ps1`): the divider lights pink on hover. Dragging it 150 px down grew Android, shrank Main and saved `Android 533`. A real double-click emptied `board-heights` and put the divider back where it started. Nothing in crash.log. That covers the double-click, which the card's own test couldn't do.
- For Philip's eye when it comes back (not failures): at the 140 px minimum a row shows its column heads and only a sliver of a card ([screenshot](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0160/.shots/integration/Qa_drag_up_stops_at_min_and_others_take_space/dragged-to-min.png)). The divider sits just under each row's horizontal scrollbar, so there are two thin grey lines together until you hover ([screenshot](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0160-live/2-dragged.png)).

