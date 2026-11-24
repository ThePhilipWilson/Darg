# WOR-0187: Highlight cards needing human attention outside the attention column

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-22
- **Done:**
- **Moved:** 2026-11-22 23:22:29

## Description

> Cards that require human attention but are not in the For Human Attention column should be visually highlighted on the board, so they are obvious at a glance rather than needing a look for the matching text.

**Wanted:** a board tile whose card is waiting on Philip (an open `## Decision`, or its agent asking or failed) while sitting in any column other than For Human Attention or Done gets a clear outline in the theme's Warn colour, so it stands out without reading the tile. Built once (see How to check); the Decision answer asks that the outline fit the colour scheme, so the open work is making it look right in every theme.

**Where:** Board section. `src/Darg.Desktop/Views/BoardView.axaml` (the `card` Border's `waits`, `agentwaits`, `agentfailed` classes and their styles, near the `Warn` styles around line 64), `src/Darg.Desktop/ViewModels/BoardViewModel.cs` (`DecisionOutside`, `InAttentionColumn`, `InDone` on the card view model), theme keys in `Themes` in Core (`src/Darg.Core`). Test: `WaitingCardTests` in `tests/Darg.Desktop.Tests`. Work is on branch `wor-0187` (b6ca01f), not yet merged. Related but separate: WOR-0186 (how cards enter and leave attention).

**Done when:**
- A card in In Progress or QA with an open valid `## Decision` shows the outline; the same for one whose agent needs you or has failed.
- No outline on cards in For Human Attention or Done, or on plain cards.
- The outline colour and thickness are taken from theme keys and look right in Dark, Light and Girlypop (screenshot of each, not just Girlypop).
- `pwsh -File tools/test.ps1 -Filter WaitingCardTests` passes, with a test for the agent-asking and agent-failed cases too.

**Questions left:** none.

## Decision answered 2026-11-22 23:22

**Question:** Is the 2px Accent outline on a waiting card right, given the Done-when asked for thickness from a theme key and it is a fixed 2?
**Asked by:** agent qa, 2026-11-22
![Dark theme](D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0187/.shots/integration/The_outline_follows_the_theme/waiting-dark.png)
![Light theme](D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0187/.shots/integration/The_outline_follows_the_theme/waiting-light.png)
![Girlypop theme](D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0187/.shots/integration/The_outline_follows_the_theme/waiting-girlypop.png)

- **Looks right, fixed 2px is fine** (recommended) → done: the card goes to done; merge wor-0187 yourself after committing BoardViewModel.cs (`git merge --ff-only wor-0187`).
- **Make thickness a theme key** → back: sent back to add a thickness key in Themes (all three themes) and use it.

**Context:** QA passed: full suite green on bdae61e (unit 825, integration 157, includes agent-asking, agent-failed and all-theme tests), outline visible in Dark, Light and Girlypop and absent on the plain tile. Only the fixed thickness departs from the Done-when, and whether it looks right is your call.

**Answer:** Looks right, fixed 2px is fine (2026-11-22 23:22)

## Decision answered 2026-11-22 22:03

**Question:** Does the 2px amber outline on a waiting card look right on the board?
**Asked by:** agent qa, 2026-11-22
![the outlined tile (Girlypop theme)](D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0187/.shots/integration/A_card_with_an_open_decision_outside_attention_is_outlined/waiting.png)

- **Looks right** (recommended) → done: the card goes to done; merge wor-0187 yourself after committing BoardViewModel.cs.
- **Needs a change** → back: sent back with your notes (colour, thickness, themes).

**Context:** Full suite passes (unit 825, integration 153) on b6ca01f. Not automated: the agent-asking and agent-failed outlines (only the decision case has a test), and the Light and Dark themes (only Girlypop seen).

**Answer:** can we make it fit the colour scheme (2026-11-22 22:03)

## How to check

Changed: a board tile is outlined 2px in the theme's Accent when its card waits on you outside For Human Attention or Done: an open valid `## Decision`, or its agent asking or failed. (Accent, not Warn: Warn's amber clashed with the pink themes.) Thickness is a fixed 2, not a theme key.
Branch `wor-0187`, commits 1b8b388 06e88fd bdae61e (pushed; NOT merged into `android`: an uncommitted change to BoardViewModel.cs in the main folder blocks the fast-forward, so after it is committed run `git merge --ff-only wor-0187` there).
Try: `pwsh -File tools/test.ps1 -Integration -Filter WaitingCardTests`, or put a `## Decision` on a card in In Progress and open the Board.
Screenshots, one per theme: D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0187/.shots/integration/The_outline_follows_the_theme/waiting-dark.png, waiting-light.png, waiting-girlypop.png
Look for: a pink outline on the waiting tile in each theme, none on the plain tile.
