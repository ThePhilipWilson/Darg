# WOR-0220: Queues need a visible bounding box, queue order and their own controls

- **Status:** QA Ready
- **Priority:**
- **Type:** Story
- **Labels:** queue
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 14:44:36

## Description

Queues are hard to read as queues.

- Draw a bounding box around only the cards that are in the queue, so it is obvious they belong together (currently, with the by-state grouping on or off, nothing shows they are queued).
- Show the cards in queue order, not board or state order.
- Make the queue itself interactive: rename it; delete it, both keeping the cards and deleting the cards with it; drag it to reorder it among queues.

Done when a queue reads as one boxed, ordered group on the board and can be renamed, deleted (with or without its cards) and reordered from the UI.

## How to check

**What changed** (commit `d0c0f11` on `android`, pushed): each queue is now a boxed lane above the board's columns (Philip's pick: a queue lane, with the cards leaving their columns while queued). Its cards show there as the board's own tiles, in queue order, each with its place, board and column, and ← → ✕ (✕ puts the card back in its list). By state on or off, queued cards stay in the lane. The lane's ⠿ grip drags it up or down among the queues. Double-click its name, or use ⋯ → Rename, to rename it. ⋯ also has "Delete queue, keep its cards…" and "Delete queue and its unfinished cards…": unfinished cards go to their board's `.deleted` folder and done ones stay. Both ask first. ▾ folds a lane to one line of ids in order. New API commands: `queue-rename`, `queue-move`, and `queue-delete` with `cards: true`.

**Try it:** update Darg (⟳ menu), or run the debug build from `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe` on a throwaway instance (`DARG_HOME=.shots/home`). On a board with a few cards in different columns, use + Queue, or "New queue from this card" from a card's menu, then add more cards.

**Look for:**
- the queued cards gone from their columns and boxed together in queue order;
- the same with By state ticked;
- dragging a lane by ⠿ with a real mouse (the tests drive the drag headless only);
- rename, both deletes, and a card's tile in the lane still opening its panel and taking Work on and its menu.

Screenshots from the integration tests:

![the lane](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0220/lane.png)
![by state](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0220/lane-by-state.png)
![delete with cards](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0220/delete-with-cards.png)

**Worth knowing:**
- Work All and Pick all no longer count queued cards, since those belong to the queue.
- A queue's done cards stay in its lane, faded, rather than going back to the done column.
- Deleting cards with a queue leaves any agent at work on them running.
