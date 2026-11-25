# WOR-0220: Queues need a visible bounding box, queue order and their own controls

- **Status:** QA Ready
- **Priority:**
- **Type:** Story
- **Labels:** queue
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 15:39:41

## Description

Queues are hard to read as queues.

- Draw a bounding box around only the cards that are in the queue, so it is obvious they belong together (currently, with the by-state grouping on or off, nothing shows they are queued).
- Show the cards in queue order, not board or state order.
- Make the queue itself interactive: rename it; delete it, both keeping the cards and deleting the cards with it; drag it to reorder it among queues.

Done when a queue reads as one boxed, ordered group on the board and can be renamed, deleted (with or without its cards) and reordered from the UI.

## Decision answered 2026-11-25 15:13

**Question:** Everything on the card passed QA. One small flaw turned up: Ctrl-clicking a card in a queue's lane marks it picked ("1 picked") but ▶ Work on N ignores it, so nothing can be done with the pick. Accept the card and ticket that separately, or send it back?
**Asked by:** agent (QA of WOR-0220), 2026-11-25
- **Accept, ticket the pick quirk** (recommended) → done: the card goes to done; the lane-tile pick gets its own small card.
- **Send back to fix the pick** → back: a lane tile either can't be picked or is counted by ▶ Work on N.
- **Leave it for now** → stay: it stays here until you've tried the lanes yourself.

**Context:** The full suite passed (914 unit, 249 integration), and the lane drag worked with a real mouse. Nothing else was wrong; the detail is in QA notes below.

![a lane tile picked: 1 picked, no Work on](D:/GitHub/ThePhilipWilson/Darg/.shots/qa-0220/lane-picked.png)

**Answer:** Send back to fix the pick (2026-11-25 15:13)

## QA notes

- 2026-11-25 (QA agent, commit d2c0e1e in its own worktree):
  - **Full suite** (`pwsh -File tools/test.ps1`): all passed, unit 914, integration 249.
  - **Extra headless tests** (written for QA, not committed: `.shots/qa-0220/QaLaneTests.cs`, all 4 passed):
    - A lane tile's click opens its panel.
    - Its menu has "⏵ Take out of queue Q" and the Move to entries. Move to QA Ready moves it and the lane's label updates while it stays in the lane.
    - Work on from the lane tile starts an agent and moves the card to In Progress, and it stays in the lane.
    - A queued card from a board that isn't showing gets a plain tile labelled "Android · Backlog", and clicking it opens that card.
    - On the All board, queued cards are out of every merged list and both lane cards draw as real tiles.
    - The API's `queue-delete cards: true` deletes the unfinished card to `.deleted` and keeps the done one, which goes back to its list.
    - `queue-move to: "zero"` and a rename onto another queue's name are both refused. `to: 9` puts the queue last.
  - **Live debug build** (`.shots/qa-0220/lane-drag.ps1`, throwaway instance, fake claude, real mouse through SendInput), 0 fails:
    - Dragging Two's lane by ⠿ above One's moved it on screen and in `queues.json`.
    - Double-clicking the name, typing and pressing Enter renamed it on disk. Double-click then Esc left it unchanged.
    - No crash.log. Shots: `.shots/qa-0220/live-*.png`.
  - **Flaw:** Ctrl-clicking a lane tile shows "1 picked" and frames it, but ▶ Work on N counts 0 (`BoardViewModel.Shown()` covers lists only, and `_picked.IntersectWith(Shown())` drops the pick on the next reload while the tile's `IsPicked` stays true).
  - **Worth a look, not a fault:**
    - Two queues of one row each take about 410 px above the columns on a 929 px window. The lanes cap at 480 px and then scroll.
    - With every card on a board queued, the count reads "0 of 1 cards".

## How to check

**This round (send-back: the lane-tile pick).** Commit `7b1adba` on `android` (branch `wor-0220`). A tile in a queue's lane Ctrl+clicked is now picked like any other: ▶ Work on N and ✦ Improve N count it and start it, Unpick clears it, a drag of picked cards carries it, and its pick survives a board reload (the lane is set before picks are trimmed). Pick all and a column's pick still leave queued cards to their queue, on purpose: the queue runs them.

**Try it:** update Darg (⟳ menu), or run the debug build `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe` with `DARG_HOME=.shots/home`. Queue a card (+ Queue, or a card's menu), then Ctrl+click its tile in the lane.

**Look for:** "1 picked" and the button reading ▶ Work on 1; pressing it starts an agent on that card, which moves to In Progress and stays in the lane. Pick all with a queued card on the board leaves the lane tile unpicked.

Test: `QueueTests.A_lane_tile_picked_with_Ctrl_counts_for_Work_on_N_and_starts` (fails without the fix: Work on 0). Shot from that test:

![a lane tile picked: 1 picked, Work on 1](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0220/lane-tile-picked.png)

The rest of the card (lane, rename, drag, both deletes) passed QA in the round before (QA notes above) and is unchanged.
