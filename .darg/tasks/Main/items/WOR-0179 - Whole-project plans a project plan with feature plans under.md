# WOR-0179: Whole-project plans: a project plan with feature plans under it

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning, ui
- **Blocked by:** WOR-0175, WOR-0176
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-22 20:43:23

## Description

The Design section also holds a plan for a whole project, so a new project starts from its goals and a list of features rather than one board of guesses. Darg's own docs/PLAN.md is the model: the ask, the big calls, then slices. Design: docs/FEATURE-PLANNING.md, section Whole projects.

A plan with `kind: project` has Goals, Not doing, Questions and Design as a feature plan does, and a Features part in place of Cards: a line per feature saying what it is, and a link to its feature plan once there is one. Making features drafts a feature plan for each ticked line (through the planner), with `parent:` set.

**Where:**
- `src/Darg.Core/Plans.cs` (`kind: project`, Features), `Planner.Prompt` (the project kind), and the Design page's tree and Features part.

**Done when:**
- A project plan with two features shows them under it in the tree, with their progress rolled up from their cards.
- Making features from a ticked line starts the planner on it, and the new plan's `parent:` is the project plan; integration test with fake claude.

## Decision answered 2026-11-22 20:43

![the Features part while one planner drafts](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0179/.shots/integration/Probe_features_through_the_view/probe-drafting.png)

**Question:** QA passed. Do you keep the three choices the agent made for you on Draft feature plans?
**Asked by:** agent 342fe093c4 (QA), 2026-11-22
- **Keep all three** (recommended) → done: open questions need a second press, drafting sets the project plan to agreed, and the link is written when the planner starts, as Make cards already does.
- **Change one** → back: it goes back for more work with your note saying which choice and how.
- **Look later** → stay: it stays here for now.

**Context:** The card's How to check lists these as "Choices I made for you (say if any is wrong)", so they're yours to settle. Everything else passed: the full suite and a probe of the rendered view (picture above, detail in QA results below).

**Answer:** Keep all three (2026-11-22 20:43)

## QA results (2026-11-22, agent 342fe093c4)

Checked on `android` at 24454a8 (contains 3fd8f28), in a worktree at `.shots/qa-0179`.

- **Full suite** (`pwsh -File tools/test.ps1`): unit 822/822, integration 150/150. Nothing failed.
- **Card's tests**: `ProjectPlanTests` (integration) and the two new `PlansTests` passed in that run. Between them they cover both Done when lines: two features under the project in the tree with 1/2, 0/2 and 1/4 rolled up; the planner started with `parent: proj`, the plan written with that parent and shown under the project; the API's `plan-features`.
- **Extra probe** (`tests/Darg.Desktop.Tests/QaProbe0179Tests.cs`, only in the QA worktree, not committed), driving the rendered view's own controls:
  - The checkboxes ("Plan Notes") and the button ("Draft feature plans") are found in the window. Unticking one changes the label from 3 to 2. With no open questions, one press starts the planners.
  - Each link is written the shape its line was in (`- **[Search](search.md)**: …`, `- [Voice](voice.md): …`), and an unticked line is left as it was.
  - While a planner waits on a question, its row reads `drafting`. A planner that ends without writing its file puts its row back to `proposed` with the tick still on. The untick on Notes survives reloads.
  - Pressing again starts a fresh planner only on that row; Search's planner, still at work, gets no second one.
  - API: open questions without `confirm` are refused ("still open", the plan left alone); a feature plan is refused; an unknown feature title is refused.
  - crash.log empty.
- **Small things seen, not failures:**
  - The retry writes `voice-2.md`, not `voice.md`: the first Voice planner (finished, no file written) still holds the name in `Planner.FileFor`. The link moves to `voice-2.md` correctly. But a message later sent to that old planner could write a `voice.md` that sits under the project as a second Voice plan.
  - The status line still reads "Drafting 2 feature plans: search.md, voice.md" after the Voice planner ended without writing its file.
- **Not checked live:** I didn't run the installed copy or a real Claude planner. The planner's real output, a project plan typed into + Plan with Project picked, is only exercised with fake claude.

## How to check

**What changed** (commit `3fd8f28` on `android`, merged from `wor-0179`; https://github.com/ThePhilipWilson/Darg/commit/3fd8f28):
- A project plan's Features rows show each feature's own plan: its status and cards done (e.g. `agreed · 1/2 cards done`, click opens it), `drafting` while a planner writes it, else a tick.
- **Draft N feature plans** under the rows (and the API's `plan-features`) starts a planner per ticked feature, `parent:` set to the project plan, told to read the project plan first. The feature's title in the project plan is linked to the file Darg named straight away (`- [Search](search.md): …`), so it lands under the project in the tree whatever the planner titles it. The page stays on the project.
- The tree also puts a plan the project links to under it even with no `parent:`. The project's tree count and page header add up its features' cards ("3 of 4 features planned · 1 of 5 cards done").

**Choices I made for you** (say if any is wrong):
- Open questions in the project plan need a second press, as Make cards does.
- Drafting features sets the project plan to `agreed`, as Make cards does.
- The link is written as soon as the planner starts, not when it finishes. If a planner ends without writing its file, the row goes back to a tick and pressing again starts a fresh one.

**To try it:** after updating (⟳), open Design in a project with a docs folder. Type a line, pick **Project** and press + Plan, or hand-write a `kind: project` plan in `docs/design/` with a `## Features` list. Untick one feature, press **Draft N feature plans** (twice if questions are open), and watch:
- each ticked row says `drafting`;
- the feature's line in the project plan file gains a link;
- when each planner's turn ends, its plan shows under the project in the tree;
- the row then shows the plan's status and cards.

Screenshots from the integration test: ![before](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0179/project-plan.png) ![after drafting](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0179/project-plan-drafted.png)

Tests: `pwsh -File tools/test.ps1 -Filter "ProjectPlan|PlansTests|PlannerTests"`.
