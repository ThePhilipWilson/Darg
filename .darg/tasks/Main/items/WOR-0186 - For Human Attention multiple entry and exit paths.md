# WOR-0186: For Human Attention: multiple entry and exit paths

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-22
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 22:13:34

## Description

For Human Attention should be a general waiting-for-a-response stage, not a single path. Any ticket that has finished a stage and needs a response should be able to enter it: from In Progress, QA or Needs More Work (rework). Once Philip has answered, the card should be able to go to any other stage, including back to the backlog, In Progress, QA, rework, done or any other column, rather than a fixed outcome.

## How to check

Changed: a decision option's arrow can name any column (`→ move to QA`, `→ backlog`, `→ In Progress`, `→ move to Needs More Work`), so answering sends the card there; the card panel's For Human Attention button now shows for cards in In Progress and needs more work as well as QA; the `to-attention` API takes any card.

- Branch `wor-0186`, commit `f8d2ac0` (pushed to origin; NOT yet merged into `android`, because the main checkout has someone's uncommitted edit to `BoardViewModel.cs`: merge with `git merge --ff-only wor-0186` once that is committed).
- Try it: open a card in In Progress, press For Human Attention, then give it (via the API `to-attention` or by editing the card's `## Decision`) options like `- **Test it** (recommended) → move to QA: ...` and `- **Park it** → backlog: ...`. Pick each; the card should land in that column.
- Look for: a column the board lacks (e.g. `→ move to Nowhere`) leaves the card where it is with a message, not a crash.
- Tests: `pwsh -File tools/test.ps1` passes, unit 832, integration 163.
