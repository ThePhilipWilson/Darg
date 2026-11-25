# WOR-0214: All board and a toggle to split columns by state

- **Status:** QA Ready
- **Priority:**
- **Type:** Story
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 12:19:12

## Description

Philip wants an "All" board showing every ticket across the project, plus a toggle available on any board in any project that splits each column into sub-columns, one per distinct state. Backlog, In Progress, QA, For Human Attention and Needs More Work would each become several narrow columns (e.g. ready to go, blocked, in progress, QA ready, running QA), as if each column held multiple columns. Normal view stays as it is; the granular view is opt-in. Open points: how a card's state is worked out (blocked-by, live agent, QA agent running, waiting on a decision), whether the toggle is remembered per board, and whether the All board is built in or a normal board.

## Decided

Asked 2026-11-25, all three recommended options picked: the All board is built in (a tab, nothing on disk); one set of states for every column, a sub-column only for states that have cards; the By state switch is remembered per board (and for the All board).

## How to check

### Round 3 (rework of the round 2 QA note): commit `8c47ca5` on `android`, pushed

**What changed:** with All showing, the API's `work-on`, `improve-card`, `send-back` and `run-flow` acted on the board picked underneath, not the card's own. They now act on the card's own board. The list forms (`work-on` / `improve-card` with `cards`) now find each card on whichever board showing holds it, instead of the picked board only. Only the API commands changed (`MainViewModel.Api.cs`, `BoardViewModel.WorkOnCards` / `ImproveCards`); the tiles' own buttons already used the card's board.

**Try it:** after ⟳ Update (or `pwsh -File tools/publish.ps1`), open Darg's own project with Board → All showing and Main picked underneath. From Fettle or the phone, start work on a card from another board (one of the story boards). It should move to that board's In Progress, with no "Couldn't move" in the status line, and its agent's prompt should name a card file under that board's `items` folder.

**Tests:** `AllBoardTests.With_All_showing_the_APIs_card_commands_act_on_each_cards_own_board`. It covers `work-on` for one card, the `cards` list across two boards, and `send-back`. With the old `MainViewModel.Api.cs` it fails ("Expected In Progress, Actual Backlog"), and with the old `BoardViewModel.cs` it fails on the list form. Whole suite before the rebase: 897 unit and 244 integration, all passed. After rebasing onto the new WOR-0212/0213 commits, AllBoard, Queue, CardWork and BulkStart tests passed again (31 unit, 20 integration).

**Still for you to judge** (from round 1, never looked at by you): whether the sub-columns read well at your real card counts, and whether the state names and colours make sense.

### Round 2 (rework of the 2026-11-25 QA notes): commit `4c6e8d7` on `android`, pushed

**What changed:**
- A tile's top line is a new `TileHead` panel (`src/Darg.Desktop/Views/TileHead.cs`). Run QA and Work on sit beside the id while the id fits whole; in a narrower tile (a state sub-column) they go on a line of their own under the id. Ids no longer read `WOR-0:`. Wide columns look as before.
- The board name on an All tile trims with an ellipsis ("Agents and Fettle ch…"); hovering it shows the whole name.
- The footer says "· all boards" on All, and now follows the board picked. Before, it stayed on whatever board was picked when the project opened.
- Not done here: the 1.4 to 2.2 s freeze with ten boards showing. QA traced it to showing many rows, with or without All, so it needs a card of its own.

**Try it:** run `pwsh -File tools/publish.ps1` (or ⟳ Update) and open Darg's own project. Board → All → tick By state. Look at QA Ready: each tile's id should show whole, with 🧪 Run QA and ▶ Work on on the line below it. Tiles from long-named boards should end in "…". The footer should read "… · all boards". Click a board tab and the footer should change to "· board <name>".

Headless screenshot from the new test:
![narrow sub-column](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0214/narrow-sub-column.png)

**Tests:** `AllBoardTests.In_a_narrow_sub_column_a_tiles_id_shows_whole_and_the_footer_says_all_boards`. Against the old tile layout it fails with "T-0001 is cut: 35 of 39". Whole suite on the change: 874 unit and 241 integration, all passed.

### Round 1

**What changed** (commit `df4f56f` on `android`, pushed; branch `wor-0214` merged and deleted):
- **All tab**, before the board tabs once a project has two boards or more. It shows every card of the project on one board. Its columns are every board's merged by name (a column only one board has sits between its neighbours there). Each tile names its board. Dragging a card to an All column moves it within its own board; a card whose board lacks that column stays put and the status line names it. All columns can't be renamed, removed, minimized or bulk-started (no Work All / QA Run All there). Any board tab, + Board, or Design's link to a board leaves All. It's remembered: Darg reopens to All.
- **By state** checkbox, at the right of the label-chip row (or in each row's header when several boards show). It splits every column except the done column and Completed into narrow sub-columns, one per state its cards are in: Blocked (open Blocked by), Ready to go (QA ready in the QA column), Agent working, Running QA, Needs you (agent asking, or the card is in For Human Attention), Agent failed. Cards move between sub-columns live as their agents change state. Dropping on a sub-column drops on its column. Kept per board, and separately for All, in `board-views` beside `shown-boards` in the project's `.darg`.
- When the tabs don't fit, All goes into the "▾ n more" menu with them, unless All is showing.

**Try it:** ask Philip to run `pwsh -File tools/publish.ps1` (or ⟳ Update), or launch a debug build of `android`. On Darg's own project:
1. Board → click **All**. Every board's cards are on one board, each tile showing its board's name; the count reads "n of m cards on k boards".
2. Tick **By state**. In Progress should split into Agent working / Needs you / Ready to go, depending on which cards have agents; a card blocked by an open card shows under Blocked in its column.
3. Start an agent on a card, or wait for one to ask a question: its tile should move to Agent working, then to Needs you, without a reload.
4. Click a board tab: that board shows unsplit unless you ticked By state on it. Tick it there, switch away and back: it is kept per board.
5. Drag a card on All to another column and check its own board's file moved.

**Look for:** whether the sub-columns read well at your real card counts (each is 214 px wide, so a column with five states is wide), whether the state names and colours make sense, and whether By state's place under the header suits you. It moved there because in the header it pushed the tabs out at narrow widths.

Screenshots from the integration tests (headless, so not a live window):
![All board](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0214/all-board.png)
![By state on Main](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0214/split-by-state.png)
![All board split by state](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0214/all-split.png)
![All tab in a narrow window](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0214/all-tab-narrow.png)

**Tests:** `pwsh -File tools/test.ps1 -Filter "AllBoardTests|CardStatesTests"`. The whole suite passed on the rebased commit: 874 unit, 240 integration. `BoardTabsTests.Tabs_that_dont_fit_go_in_a_more_menu` now expects the All entry in the ▾ menu.

## QA notes

### 2026-11-25 round 2 (QA agent, on 4c6e8d7 in a worktree of its own)

Checked: the whole suite (`tools/test.ps1` on 4c6e8d7: 897 unit, 243 integration, all passed). The debug build on the same throwaway copy of the 10 real boards with fake agents, driven through UI Automation (`.shots/qa-0214-live/drive-r2.ps1`). The round 1 notes are fixed: in All split by state, every id in the QA ready sub-column shows whole, with Run QA and Work on on their own line beneath; long board names end in "…" ("Agents and Fettle ch…"); and the footer reads "· all boards" on All, then "· board Main" and "· board Android" as those tabs are clicked. In a wide unsplit column the buttons stay beside the id. No crash.log entries. Screenshots: ![All split](D:/GitHub/ThePhilipWilson/Darg/.shots/qa-0214-live/r2-2-all-split-agent.png) ![Main unsplit](D:/GitHub/ThePhilipWilson/Darg/.shots/qa-0214-live/r2-3-main-unsplit.png)

- **With All showing, the API's `work-on` starts the agent on the wrong board for any card not on the board picked underneath.** I called `work-on {project: proj, card: MER-0001, force: true}` with All showing ("Agents and Fettle chats travel" picked underneath). The status line said "Couldn't move MER-0001: No card MER-0001 on this board.", the card stayed in Backlog, and the agent's prompt read `Card file: …\proj\tasks\Agents and Fettle chats travel\items\MER-0001 - ….md`, a file that doesn't exist. With All off, the same call on MER-0002 moved it to In Progress and gave the right path (`…\Merge-safe boards\items\…`). Round 1's AUT-0001 also stayed in Backlog the same way. The cause: `MainViewModel.Api.cs` `WorkOn` gets the card's row from `p.Board.Show(id)`, but then calls `p.Board.StartWork(card, …)` on the root board, whose `Folder` and `_board` are the picked board. Without All, `Show` makes the card's board the picked one, so this worked. With All on, every board is a row, so `Show` leaves the pick alone. Use `card.Board.StartWork(…)`. The same root-board pattern is in `ImproveCard` (`p.Board.StartImprove(card)`) and `SendBack` (`p.Board.SendBack(card, …)`), so check those too, and anything else that calls `Show(id)` and then acts on `p.Board`. The phone, Fettle and the queue runner all start cards through these routes, and with All remembered as the opening view this is the common case. It needs an integration test: with All showing, `work-on` a card on a board other than the picked one, then assert it moved and the agent's card path is right.
- Not re-checked: the round 2 test failing against the old tile layout (the card says it does: "T-0001 is cut: 35 of 39").

### 2026-11-25 (QA agent, on 041d6ac in a worktree of its own)

Checked: the whole suite (`tools/test.ps1`: 874 unit, 240 integration, all passed). The debug build, run on a throwaway copy of this repo's 10 real boards (297 cards) with fake agents, clicked through UI Automation (`.shots/qa-0214-live/drive.ps1`). All shows "296 of 297 cards on 10 boards", every tile names its board, columns are merged by name. By state splits Backlog into Blocked / Ready to go and leaves Done whole. All and the split come back after a restart (`board-views`: `all`, `split *`). Two agents started through the API moved their tiles to Agent working without a reload. Drops, per-board memory and the overflow menu are covered by the passing `AllBoardTests`.

- **Card ids are cut off in a narrow sub-column.** In QA Ready split by state, the tile's id reads `WOR-0:` because Run QA and Work on take most of the 214 px, and the id can't wrap. The board name on All tiles is cut too ("Agents and Fettle chat"). The id is what tells cards apart, so it has to show whole at sub-column width: put the buttons on their own line when there isn't room, or narrow them there. Screenshot: ![QA ready sub-column, ids cut](D:/GitHub/ThePhilipWilson/Darg/.shots/qa-0214-live/4-all-split-working.png)
- Not this card's fault, but All makes it common: with all 10 boards showing, the UI freezes 1.7 s (`stalls.log`: Board.Reload x20, Apply x200) when two agent turns end, and 1.4 s on opening to All. Showing the same 10 boards as rows without All freezes 2.2 s, so the cost comes from showing many rows, not from All. Worth a card of its own.
- A small one: on All, the footer still says "board Agents and Fettle chats travel", the board picked underneath.
- Not checked: a real mouse drag onto a sub-column (it would move Philip's own cursor). The XAML makes each sub-column a `list` drop target tagged with its list's key, and the drop code is covered at view-model level.
