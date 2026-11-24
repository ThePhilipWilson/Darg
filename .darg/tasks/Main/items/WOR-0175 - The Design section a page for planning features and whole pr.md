# WOR-0175: The Design section: a page for planning features and whole projects

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning, ui
- **Blocked by:** WOR-0174, WOR-0172
- **Created:** 2026-11-20
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 04:05:41

## Description

Philip, 2026-11-20: "a separate page for design, I don't want to have to shoehorn it into the board just because it's what we have. A bespoke design page for managing anything from features to whole projects." Design: docs/FEATURE-PLANNING.md, section The Design section.

A rail section of its own: on the left the project's plans as a tree (the project plan, its features under it), on the right the picked plan as a page in parts (Goals, Not doing, Questions, Design, Cards or Features, Log), each edited in place. Questions are answered by tapping an option. Made cards show their live column, agent and blockers from the board; nothing about progress is written into the plan file.

**Where:**
- `src/Darg.Desktop/ViewModels/MainViewModel.cs` (`Section` gains Design), a new `DesignViewModel` and `Views/DesignView.axaml`, on `PlanStore` from Core.
- Colours by `{DynamicResource}` theme keys only.

**Done when:**
- The section lists the plans in `docs/design/` as a tree and opens one in its parts; an answer tapped on the page lands in the file as `**Answer:**`.
- A made card's row shows its column and agent and opens the card's panel.
- An integration test (DargApp with a plan file) and a screenshot in `.shots/integration` show the page.

## Decision answered 2026-11-22 03:47

**Question:** The Design page is built and tested; which look do you want for it?
**Asked by:** agent QA, 2026-11-21
- **Keep as is** (recommended) → done: keep the ◇ icon and the page stopping near 980 px; the card goes to done.
- **Fill the width** → back: sent back so the page fills the window instead of leaving an empty strip at wide sizes.
- **Different icon** → back: sent back to change the ◇ rail icon (say which in your notes).

**Context:** At 1400 px the page leaves a wide empty strip on the right. ![the plan page](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0175/design.png) Everything else passed.

**Answer:** Fill the width (2026-11-22 03:47)

## How to check

**This round (Fill the width):** the page's parts were in a StackPanel capped at 980 px and aligned left, which inside a ScrollViewer shrank it to its widest text (about 460 px of a 640 px viewport beside the planner's chat), so the strip showed at every width. Both are gone and the parts stretch to the scrollbar. `DesignPageTests` now checks the parts fill the viewport bar the margins; it failed on the old layout (a 180 px strip). The ◇ icon is unchanged, as the pick left it.

**Branch and commit:** `android`, 4d32cf3 (branch `wor-0175` merged and deleted).

**To try it:** `pwsh -File tools/test.ps1 -Integration -Filter DesignPageTests`; the whole suite ran unit 807/807, integration 149/149.

![the page filling its width](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0175/design-fill.png)

## QA notes

- 2026-11-21 QA (commit 62cdc09 on `android`): **the card's own integration test fails in the main checkout.** `pwsh -File tools/test.ps1`: unit 789/789, integration 132/133. Failing: `DesignPageTests.The_design_page_shows_a_plan_in_its_parts_takes_answers_and_links_its_cards` at `DesignPageTests.cs:140` (expected `anywhere.\n- Never miss`, actual `anywhere.\r\n- Never miss`). Cause: the plan text is a C# raw string, which takes the source file's line endings. This repo has `core.autocrlf=true`, so a checkout gives a CRLF `.cs` file (`git ls-files --eol`: `i/lf w/crlf`) and the plan is written as CRLF. `SetSection` then correctly writes the new Goals as CRLF, but the expected string builds them with a bare `\n`. In a worktree checked out with `core.autocrlf=false` (LF), both Design tests pass. So the product is right, and the test passes only where the file happens to be LF. Fix: make the test independent of checkout line endings, e.g. `ProjectPlan()` and `FeaturePlan()` with `.ReplaceLineEndings("\n")` before writing, or normalise both sides before comparing. Then check it passes in a CRLF checkout. No other test failed.
- 2026-11-21 QA, what passed: in the LF worktree, both `DesignPageTests` pass. The Dark shots show the tree (Proj with Phone under it, 0/2), the parts, the open question with its options recommended first, the made card's column and live agent, the proposed card's star and `waits on T-0001`, the answer landing, and `not committed` in the status line. A scratch integration test (not committed) did the following: loaded the worked Android plan from `docs/FEATURE-PLANNING.md`, saved with **CRLF**; switched Dark, Light and Girlypop through the API's `theme`, shooting each; pressed Change on the answered question and picked the second option; added a Goals line. The file stayed all CRLF (0 bare LF) and held exactly one `**Answer:**` line, `A relay (Philip, 2026-11-21)`, which replaced the old Tailscale one. The page read both changes again, and crash.log stayed empty. Light and Girlypop read cleanly, with no unreadable text.

![light](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0175/qa-light.png)

![girlypop](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0175/qa-girlypop.png)

- For Philip once the test is fixed (taste, from How to check): whether ◇ is the icon he wants, and whether the page should fill the width rather than stop near 980 px (at 1400 px it leaves a wide empty strip on the right).

- 2026-11-21 QA round 2 (f07bf63 on `android`): the CRLF fix holds. Both `DesignPageTests` pass in the CRLF main checkout. Unit 790/790. Integration was 135/136 (`DoneCardTests.A_card_moved_to_done_gets_its_done_report_then_its_finished_agent_goes`), then 135/136 (`GitHubPageTests.A_project_with_two_repos_has_a_tab_for_each`): different tests each time, each passes alone (6/6), and a third full run on 5ec4488 passed 141/141. The commit before this card (aca3e15) passed 131/131 once. Neither failing test touches the Design section; they look like timing flakes under load, but I did not diagnose them and ran the baseline only once, so I cannot say they never fail without this card. Worth a ticket. Left for Philip: the icon and page-width taste questions.

### 2026-11-22 03:47

The Design page is built and tested; which look do you want for it? I picked: Fill the width.
