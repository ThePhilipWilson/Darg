# WOR-0241: Remove add-by-id from the queue view; check multi-select works

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-26
- **Done:**
- **Moved:** 2026-11-26 15:13:13

## Description

In the queue view, remove the add-cards-by-ID text option. Cards should be added by ctrl-clicking several and dragging them in, or by right-click multi-move. Make sure both of those work. Philip asked: "on the queue view the add cards by id is not wanted, can't see a world where I wouldn't just ctrl click cards and drag them in or right click to multi-move. we want to make sure both of those work and remove the add by id text option".

## How to check

Changed: the lane's "Add cards by id" box is gone. A queue lane is now a drag drop target, and a card's right-click "Add to queue X" acts on every Ctrl+picked card. Branch `android`, commits c7981ad and 94b1018 (pushed).

1. Open the Board, make a queue (+ Queue), Ctrl+click two or three cards in the lists.
2. Drag them onto the queue's lane: they join in order, the lane says "Added ...".
3. Or right-click one of the picked cards, choose "Add to queue X": all picked cards join.

Look for: no text box under the lane. The real mouse drag isn't tested headless, only the drop handler. `QueueTests.Picked_cards_join_a_queue_from_the_menu_or_a_lane_drop_and_there_is_no_add_by_id_box` covers the rest.
