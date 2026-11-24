# WOR-0171: Design feature planning: from a plan doc to a story board

- **Status:** Done
- **Priority:**
- **Type:** Story
- **Labels:**
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 22:12:41

## Description

> The Android board was thrown together with no planning. Darg should do feature planning itself: a feature designer, which we need to scope and design first (ironically). To work out: what a feature plan is (goals, scope, design, cards produced), where it lives in the app (section, flow or Fettle), how it turns into board cards, and how it is kept up to date as work lands. Output is a design doc plus follow-up cards, not code.

Design, on paper, how Darg helps plan a feature before its cards exist, so a new story board starts from a plan rather than a pile of guessed cards. The deliverable is a design doc and the cards to build it; no code changes.

**Where:**
- New doc `docs/FEATURE-PLANNING.md`, listed in `docs/README.md` (follow "How these docs are written" there; `DocsTests` checks it).
- Worked example: the Android feature, which went from `docs/ANDROID.md` (AND-0001) straight to the hand-made Android board (`.darg/tasks/Android/`, AND-0002 to AND-0009). Older precedent: `docs/PHONE-PLAN.md`.
- Code the design builds on, to read and not change: `StoryBoards.cs` (a board per story, Haiku placing cards), `FlowGenerator.cs` / `Flow.cs` and `FlowsView` (flows), `MascotViewModel` (Fettle), `BoardStore.Create` and the API's `new-card` (`Api.cs`, `MainViewModel.Api.cs`), `TicketQuality` (the score each generated card must pass), `CardDecision` (asking Philip), `ColumnWatch` / `ReportFlow` / `ReportStore` (what Darg sees as work lands). All under `src/Darg.Core/` or `src/Darg.Desktop/`.

**Done when:**
- `docs/FEATURE-PLANNING.md` answers the card's four questions, each with a recommendation and the alternatives turned down: what a plan holds; where it lives (its own section, a flow, Fettle, or the Docs section); how it becomes a story board and cards; how it stays current as cards finish.
- It replays the Android feature through the design: what the plan would have said and which cards it would have made, beside what AND-0001 to AND-0009 actually are.
- Follow-up cards for building it are on the Main board, each scoring 3/3 (Where and Done when).
- Anything Philip must choose is asked through AskUserQuestion, or left as a `## Decision` on a card in For Human Attention, not settled quietly.
- `pwsh -File tools/test.ps1 -Unit -Filter DocsTests` passes, and `git diff --stat` shows only `docs/`.

**Questions left:** none blocking. Whether a plan is a doc in `docs/` or a story card at the top of its board is for the design to recommend.

## Decision answered 2026-11-20 22:00

**Question:** You picked "Hold until done" for blocked cards, but the design clears a blocker once it reaches QA, because card agents merge before QA. Which should WOR-0172 build?
**Asked by:** agent (QA of WOR-0171), 2026-11-20
- **Clear at QA** (recommended) → back: keep the design as written; the rework only fixes the two out-of-date facts in QA notes below.
- **Clear only at done** → back: change the design and WOR-0172/WOR-0173 to hold until done, as you picked, and fix the two facts.
- **Accept as it is** → done: keep clearing at QA and leave the two small factual slips in the Why section.

**Context:** Clearing at QA lets dependent cards start while QA waits on you; the risk is that they build on work QA then sends back. Everything else checks out and the full suite passes (unit 762/762, integration 113/113).

**Answer:** Clear at QA (2026-11-20 22:00)

## How to check

**Round 2 (after QA):** commit `e1f4dd7` on `android`, pushed: https://github.com/ThePhilipWilson/Darg/commit/e1f4dd7. Only `docs/FEATURE-PLANNING.md` changed (3 lines).

- **Blocked by, Cleared when:** now says clearing at QA is Philip's call (2026-11-20), with the cost (a dependent card may build on work QA sends back) and clearing only at done turned down. WOR-0172 already says QA, so no card changed.
- **Why, No order:** six of the nine Moved lines read 01:09:51; AND-0002, AND-0003 and AND-0006 moved again later.
- **Why, The doc went stale:** past tense; ANDROID.md kept the line until WOR-0154 removed it.

Round 1 was `24f84a7` (the doc itself and cards WOR-0172 to WOR-0180).

## QA notes

**2026-11-20, QA agent:** checked on `android` at 24f84a7 (main checkout, pushed to origin/android).

Passed:
- `pwsh -File tools/test.ps1` (whole suite): build 0 errors, unit 762/762 (DocsTests included: links, anchors, index, format), integration 113/113, exit 0.
- `git show --stat 24f84a7`: only `docs/FEATURE-PLANNING.md` and `docs/README.md`.
- The doc answers all four questions, each with a recommendation and the alternatives turned down, and it replays Android (questions table and cards table).
- WOR-0172 to WOR-0180 are on Main's backlog in board.json. Checked against `TicketQuality`'s rules, each scores 3/3: a `**Where:**` label, a `**Done when:**` label, and 142 to 264 words. Their Blocked by lines match the doc's table. WOR-0172 and WOR-0174 have no line at all rather than "none", which is fine.
- Philip's two answers came through AskUserQuestion (agent 0605828e6e's transcript), and the doc quotes them faithfully.
- The code the doc names exists: `BoardStore.CreateBoard` and `Create` (Board.cs), `Stories` (Stories.cs), and FlowGenerator's "No stage can wait for me mid-flow" rule. The Android claims hold: AND-0004's WIP commit b110c6b, AND-0009's Blocked section, AND-0002's Tailscale choice, and AND-0003's "paired route isn't wired into the app yet".

Wrong or out of date:
1. **The Why section says ANDROID.md "still says the phone app isn't built yet".** It no longer does. 7b4569b had that line, and 6b4a750 (WOR-0154, 13 minutes before this commit) removed it, so the stale-doc example needs rewording to the past tense.
2. **"their Moved: lines all read 2026-11-20 01:09:51"** is true of six of the nine cards only. AND-0002, AND-0003 and AND-0006 now read 02:07 to 02:31, because they moved again later. The claim that all nine were started together still stands. Only the parenthetical overstates it.
3. **Blocker clearing at QA departs from Philip's literal pick ("Hold until done").** The card lists it openly under Choices I made for you, so it isn't hidden. It is still his call, so it is asked as the Decision above.

Not checked: nothing that is code. This card is a design, so how the Design section would look and feel can't be checked until WOR-0175 builds it.

### 2026-11-20 22:00

You picked "Hold until done" for blocked cards, but the design clears a blocker once it reaches QA, because card agents merge before QA. Which should WOR-0172 build? I picked: Clear at QA.
