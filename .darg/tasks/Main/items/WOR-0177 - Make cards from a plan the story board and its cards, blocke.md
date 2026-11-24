# WOR-0177: Make cards from a plan: the story board and its cards, blockers included

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning, board
- **Blocked by:** WOR-0172, WOR-0174, WOR-0175
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-22 03:46:59

## Description

The step from an agreed plan to a board. Design: docs/FEATURE-PLANNING.md, section From plan to board.

On the plan page each proposed card has a tick (on by default) and its score, and one button makes the ticked ones. It makes the plan's story board if there is none (`BoardStore.CreateBoard`, the project's columns, a prefix from the plan's name such as AND) or uses the one named in `board:`; makes the cards with `BoardStore.Create` in Blocked by order, turning the plan's references into the new ids; writes each id back into its heading; and sets the plan to agreed. Run again, it makes only rows with no id, and it never deletes a card. Open questions are listed above the button and need a second press.

**Where:**
- `src/Darg.Core/Plans.cs` (`PlanStore.MakeCards`), the Design page, and an API command `plan-cards {project, plan, only?}` (`MainViewModel.Api.cs`).

**Done when:**
- Making from a plan with three cards, one blocked by another, gives a board with three cards whose Blocked by lines hold the new ids, and the plan file holds the ids; a second press makes nothing.
- A card under 3/3 is refused, with its missing parts named.
- A unit test in Core and an integration test through the API.

## Decision answered 2026-11-22 03:46

**Question:** Does the Design page's tick beside each proposed card and the Make button look right?
**Asked by:** agent QA, 2026-11-21
- **Looks fine** (recommended) → done: the card goes to done.
- **Needs spacing fixes** → back: it is sent back with your notes on what to change.

**Context:** All automated checks passed (unit 807/807, integration 149/149, including the plan-cards API, the 3-card/blocker case, the second press making nothing, the under-3/3 refusal and the open-question press). Only the look is left, which can't be judged by automation. The tick sits left of the star and lines up in the shot below; the Make button is below the fold in that shot.

![the design page](D:/GitHub/ThePhilipWilson/Workbench/.shots/integration/The_design_page_shows_a_plan_in_its_parts_takes_answers_and_links_its_cards/design.png)

**Answer:** Looks fine (2026-11-22 03:46)

## How to check

**What changed:** `PlanStore.MakeCards` (Core, `Plans.cs`), a tick per proposed card and a Make button on the Design page (`DesignViewModel`, `DesignView.axaml`), and the API command `plan-cards`. Branch `wor-0177`, merged fast-forward into `android` as c5ea203.

**Try it:** copy a plan with 3 proposed cards (one with a Blocked by naming another's heading, each 3/3) into a project's `docs/design/`, open Design, pick it. Each proposed card shows a tick (ticked) beside its star; the Make N cards button sits under the list, with any open questions listed above it. Press it. Or without the UI: `POST /api/<token>/plan-cards {"project": "...", "plan": "<file name or title>"}`.

**Look for:**
- A board named for the plan appears (prefix from its name, e.g. AND), with the project's columns; the cards are on it with Blocked by lines holding the new ids; the plan's headings now read `### AND-0001: Title`, `board:` and `status: agreed` are set.
- The button disappears once every card is made; a second API call says every card has been made.
- Unticking a card leaves it out; a card under 3/3 stops the whole press and the footer names what it's missing; open questions need a second press ("Make N anyway").
- Not seen by me: how the tick and the button look (the screenshot the integration test saved went with its worktree). Check the spacing and that the checkbox lines up with the star.

**Choices I made:** all-or-nothing (one thin or mis-blocked card makes nothing, so the planner fixes the plan first); the thin-card refusal comes before the open-questions press; the board's prefix is the plan title's first three letters; the board's columns are copied from the project's first board.
