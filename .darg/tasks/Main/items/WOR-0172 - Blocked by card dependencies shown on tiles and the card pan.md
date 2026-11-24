# WOR-0172: Blocked by: card dependencies shown on tiles and the card panel

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning, board
- **Created:** 2026-11-20
- **Done:** 2026-11-21
- **Moved:** 2026-11-21 00:56:30

## Description

A card can say it waits on other cards, and the board shows it, for every card on every board, not just planned ones (Philip, 2026-11-20: "we need a dependency system for tickets so that we can block on another ticket, and display this correctly"). Design: docs/FEATURE-PLANNING.md, section Blocked by.

A `- **Blocked by:** AND-0003, WOR-0150` line among the card's properties (after Labels; the board app and Obsidian ignore a field they don't know). A blocker counts as cleared once it is in the QA column, done or Completed, since a card agent merges its branch before QA. Ids are looked up across the project's boards (CardIndex), so a card can wait on one on another board. A cycle, or an id no board has, shows as a warning, not dropped.

**Where:**
- `src/Darg.Core/Board.cs` (`Card`: a `BlockedBy` list read in `Parse`), and a new `Blockers` in Core (which of a card's blockers are still open, the reverse Blocks list, cycles).
- The card tile and the card panel in `src/Darg.Desktop/Views/` (BoardView and the panel), with an editable Blocked by row in the panel.
- The API's `boards`, `cards` and `card` answers (`MainViewModel.Api.cs`) gain `blockedBy` and `blocks`, so the phone can show it too.

**Done when:**
- A tile with an open blocker shows a chip naming it (the Warn theme key); the chip goes when the blocker reaches QA or done.
- The panel lists Blocked by and Blocks as card links, and Blocked by can be edited there.
- Unit tests for parsing, clearing and cycles, and an integration test that fails without the chip.

## Decision answered 2026-11-21 00:56

**Question:** Does the amber "waits on WOR-0175" chip, under the title and above the labels, read well on your real board?
**Asked by:** agent (QA of WOR-0172), 2026-11-21

![the real Main board with blocked cards](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0172-qa/realboard.png)

- **Looks right, done** (recommended) → done: the chip and the panel's Blocked by box stay as they are; the card goes to Done.
- **Change how it looks** → back: send it back with your notes on the chip's place, wording or colour.
- **Leave it for now** → stay: it stays here until you've seen it on the board yourself.

**Context:** All the behaviour passed by automation, on test boards and on a copy of your real boards (the screenshot). Only the look is left, and that's your call.

**Answer:** Looks right, done (2026-11-21 00:56)

## How to check

**What changed:** a `- **Blocked by:** …` line on a card is read (`Card.BlockedBy`), looked up across the project's boards (`Blockers` in Core), and shown: a Warn-outlined chip per open blocker on the tile ("waits on WOR-0150"; "ZZ-0001: no such card" and "WOR-0150: a cycle" as warnings), a Blocked by box plus Blocked by / Blocks links in the card panel, and `blockedBy` / `blocked` / `blocks` in the API's `cards`, `card` and `boards`. Commit [66a401f](https://github.com/ThePhilipWilson/Darg/commit/66a401f) on `android` (branch wor-0172, merged and deleted).

**Try it:** in the installed copy after ⟳ Update (or `dotnet build` and run the debug exe on `DARG_HOME=.shots/home`):
1. Open a card in To Do, type the id of a card in In Progress into the new Blocked by box (below Labels), Save. The tile shows an amber "waits on …" chip; hover it for the blocker's title and column.
2. Open the blocker: its panel lists "Blocks …, waiting on this"; clicking that line opens the blocked card.
3. Drag the blocker to QA Ready: the chip goes. Drag it back to In Progress: it comes back.
4. Type a made-up id (e.g. WOR-9999) and Save: a "WOR-9999: no such card" chip shows.

**Look for:** whether the chip's place (under the title, above labels) and wording read well on a real board, and the panel's Blocked by box and links.

Screenshots from the integration tests: ![blocked tile](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0172/blocked.png) ![panel](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0172/panel.png)

## QA check 2026-11-21

Checked on aca3e15 (android, which holds 66a401f and WOR-0173 on top), in a worktree of its own.

- **Whole suite** (`tools/test.ps1`): integration 131/131; unit 787/788. The one failure was `RemoteTests.Plain_http_gets_nothing_and_an_oversized_head_is_refused`, in the phone server's code (AND-0002), which this card doesn't touch. It passed 15/15 alone and in 6/6 further whole-suite runs, both on this commit and on d475dfe (the commit before WOR-0172). So it doesn't come from this card, but it is a real race and needs a ticket: `HttpRequest.Read` stops at `maxHead` and the server answers 431 with most of the 20 KB header still unread. Closing a socket with unread data makes Windows send a reset, which can reach the client before the 431 does.
- **Try it, steps 1 to 4, by automation**: a scratch integration test (not committed; kept at `.shots/wor-0172-qa/QaBlockedBy0172Tests.cs`) does each step. It types an id into the panel's Blocked by box and saves, and the chip shows. The blocker's panel lists Blocks "waiting on this", and that link opens the blocked card. Dropping the blocker on QA Ready (the board's own `Drop`) removes the chip, dropping it on In Progress brings it back, and Done removes it again. A made-up WOR-9999 shows "WOR-9999: no such card". Emptying the box takes the line out of the file and the chips with it. It also checks a two-card cycle ("…: a cycle" on both tiles, ![cycle](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0172-qa/cycle.png)), a Blocked by link opening a card on another board, and that `SetBlockedBy` on a CRLF card keeps CRLF. All 3 pass.
- **Real data**: the debug exe on a throwaway instance (`.shots/qa0172-home`), with a copy of this project's boards whose cards already carry Blocked by lines. Each tile shows only the blockers still open. WOR-0176 waits on 0174 (Done) and 0175 (In Progress) and shows only "waits on WOR-0175". WOR-0175 itself, whose blockers are in QA and Done, shows none. In the panel, the cleared blockers are dimmed with "(Done on Main), cleared" (![panel](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0172-qa/realpanel.png)).
- **Can't be automated**: whether the chip's place and wording read well is a matter of taste (the card's own "Look for"), hence the Decision above.
- **Seen in passing, not this card's**: the footer kept saying "board Android" after Main was picked. Fettle covers the end of the Labels box when the panel is open.
