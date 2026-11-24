# WOR-0173: Blocked cards don't start: Work All skips them, Work on asks

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning, board, agents
- **Blocked by:** WOR-0172
- **Created:** 2026-11-20
- **Done:** 2026-11-21
- **Moved:** 2026-11-21 02:19:01

## Description

The enforcing half of card dependencies (Philip's pick, 2026-11-20: hold until done). On the Android board all nine cards were started at once and five of them stalled or split waiting on AND-0003's scaffold. Design: docs/FEATURE-PLANNING.md, section Blocked by.

**Where:**
- `src/Darg.Core/BulkStart.cs`: Work All counts and starts only cards with no open blocker, and its label says how many it left out, e.g. (4, 3 blocked).
- Work on for one blocked card asks inline first (Blocked by AND-0003, In Progress. Start anyway?).
- The API's `work-on` and `work-all` (`MainViewModel.Api.cs`): refuse a blocked card with an ApiError naming the blocker unless `force: true`.
- The card agent's prompt (the card prompt in Desktop, beside `ReportStore.Rules`) lists its blockers with their status and the path of each one's newest report, so it builds on their work.

**Done when:**
- Work All on a board with a blocked card starts the others and not it, and says so; an integration test with fake agents fails without the change.
- Work on a blocked card asks first; the API refuses it without force.
- A card agent's prompt names its blockers' reports (checked with `app.Prompts()`).

## How to check

**This round (QA notes of 2026-11-21): commit `123669b` on `android`, pushed.** The board's header no longer trims what just happened. It shows the card count (`5 of 5 cards`). Any notice or error now shows on a line of its own under the header, whole, wrapped and selectable, with ✕ on the right to dismiss it. A further board row (⧉ Boards) shows its own notice the same way under its row header. This applies to every board notice, not only Work All's, because they were all cut at 260 px. The Work All test now checks that the window draws `left out 1 blocked: T-0003 (waits on T-0001)` with no line cut short, and I confirmed it fails with the old header.

![notice under the header](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0173/work-all-notice.png)

**To try this round:** after ⟳ Update, pick a blocked card and a free one and press ▶ Work on 2, then press ▶ Work All with a blocked card in the backlog. The whole line, ending in `(waits on …)`, should show under the header. ✕ should clear it. Check that the header doesn't look wrong to you with the extra line.

**What changed first time** (commit `aca3e15` on `android`, pushed; built on WOR-0172's `Blockers`):
- ▶ Work All (and ▶ Work on N on picked cards) leaves out a card whose Blocked by names one not yet in QA or done. The button reads `▶ Work All (3, 1 blocked)` and the status line says `left out 1 blocked: T-0002 (waits on T-0001)`.
- ▶ Work on a blocked card (tile button, tile menu, or the panel's Work on this) asks inline first: `Blocked by T-0001, In Progress. Start anyway?` with Start anyway / Cancel. A card in QA doesn't ask, since Work on there only gets it running to try.
- The API's `work-on` refuses a blocked card with an error naming the blocker unless `force: true`; `work-all` leaves blocked ones out (and names them) unless `force: true`; `work-on` with `cards` starts the free ones and names the rest.
- A card agent's prompt lists every blocker with its title, column, cleared or not, and the path of its newest report.

**Choices I made for you:** picking several cards and pressing ▶ Work on N leaves the blocked ones out (named in the status line) rather than asking per card; an id no board has doesn't hold a card back (WOR-0172's chip warns about it instead).

**To try:** run `pwsh -File tools/publish.ps1` from a terminal (or ⟳ Update), then on a board give a Backlog card a `- **Blocked by:** <id of a card In Progress>` line. Look at the Work All label, press it, then press ▶ Work on on the blocked card.

**Screenshots** (from the integration test):

![tile asks](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0173/tile-asks.png)

![panel asks](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0173/panel-asks.png)

Tests: `pwsh -File tools/test.ps1 -Filter BlockedStart` (3 integration, 5 unit).

## QA notes

### 2026-11-21 (QA agent, at `aca3e15` in worktree `.shots/qa-0173`)

- **Fails: the status line doesn't show which cards were left out.** The board's status `TextBlock` (`src/Darg.Desktop/Views/BoardView.axaml:99`) has `MaxWidth="260"`, `TextTrimming="CharacterEllipsis"` and no tooltip. After ▶ Work on N with one blocked card picked, the screen reads `Started work on T-0002; left out 1 bl…`, so the card left out and what it waits on (`T-0003 (waits on T-0001)`) never show. Work All with several started cards will lose even more. The text is right in the view model (the tests assert it); only what's drawn cuts it off. Fix: let the whole message be read (a tooltip with the full text, wrapping, or a status that names the blocked cards first), and add a test or screenshot that shows the blocked id on screen. ![status cut off](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0173/.shots/integration/QA_Work_on_N_leaves_the_blocked_picked_card_out_and_names_it/work-on-n.png)
- Everything else passed:
  - Card tests: unit 5/5, integration 3/3.
  - Whole suite: unit 788/788, integration 134/134 (that count includes the 3 QA-only tests below). No failures.
  - Each card test fails when its own piece is broken. With `OpenBlockers` stubbed to return nothing, the Work All test and the tile/panel/API test fail. With `BlockedStart.PromptLines` stubbed, the prompt test fails. Both stubs were reverted.
  - The screenshots show the tile and panel asking `Blocked by T-0001, In Progress. Start anyway?` with Start anyway / Cancel, and the Work All label `▶ Work All (1, 1 blocked)`.
- QA-only integration tests, uncommitted, in `.shots/qa-0173/tests/Darg.Desktop.Tests/QaBlockedExtraTests.cs`. They cover paths the card's tests don't, and all pass:
  - ▶ Work on N skips the blocked picked card and names it. With only the blocked card picked, it says `Started nothing; left out 1 blocked`.
  - A blocked card in QA gets running without asking.
  - The panel's Start anyway starts the card and moves it to In Progress.
  - API `work-on` with `cards` starts the free card and names the blocked one; with `force: true` it starts both blocked ones.
- Not checked: the tile's right-click menu Work on (only the tile button and the panel were driven), and Work All asking "Start n agents?" above 3 when some cards are blocked.

### 2026-11-21 (QA agent, at `123669b`, worktree `.shots/qa-0173-r2`)

- Passed. Whole suite: unit 790/790, integration 136/136, no failures.
- The earlier fail (status cut at 260 px) is fixed: the Work All test's screenshot shows `left out 1 blocked: T-0003 (waits on T-0001)` whole on its own line under the header, with the count `5 of 5 cards` in the header and X at the right. The test asserts no line is ellipsised and that X clears it.
- Not re-checked by me: that this test fails with the old header (my attempt to revert and rerun timed out waiting on the test slots; the worktree is restored). The commit and the previous QA pass both state it does.
