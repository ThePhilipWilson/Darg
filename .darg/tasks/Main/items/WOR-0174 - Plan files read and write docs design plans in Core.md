# WOR-0174: Plan files: read and write docs/design plans in Core

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 23:06:20

## Description

The file format a feature or project plan is kept in, with no UI yet. Design: docs/FEATURE-PLANNING.md, sections What a plan holds and The plan file.

One markdown file per plan in the project's docs folder under `design/` (beside `Stories/`), with properties `kind` (feature or project), `status` (draft, agreed, dropped), `board` and `parent`, then `## Goals`, `## Not doing`, `## Questions` (a `###` per question with options and an `**Answer:**` line, shaped like CardDecision), `## Design` (free markdown), and `## Cards` (a `###` per card with `**Where:**`, `**Done when:**` and `**Blocked by:**`; a card already made has its id in its heading) or, for a project, `## Features`.

**Where:**
- New `src/Darg.Core/Plans.cs`: `PlanDoc` (parse, forgiving of the shapes agents drift into, as `CardDecision` is) and `PlanStore` (list a project's plans, the tree by `parent`, write one part back without touching the rest).
- `tests/Darg.Core.Tests/PlansTests.cs`.

**Done when:**
- The worked Android plan in docs/FEATURE-PLANNING.md, saved as a file, parses into its goals, questions, cards and blockers.
- Writing an answer or a card id back changes only that part of the file (round-trip test).
- Each proposed card's TicketQuality score is available, so the page and Make cards can refuse one under 3.
