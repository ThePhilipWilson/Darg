# WOR-0145: Separate page for viewing context, memories, instructions

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-18
- **Done:**

## Description

separate page for viewing context, memories, instructions etc.

From a note on the wall (20261118-111733-4564).

## Decision answered 2026-11-18 14:21

**Question:** Are the Context page's list labels (e.g. "Workbench: CLAUDE.md", "User: CLAUDE.md", folder name as prefix) good enough as they are?
**Asked by:** agent (QA), 2026-11-18
- **Labels are fine** (recommended) → done: the card goes to done as built.
- **Rename them** → back: sent back so the labels can be reworded (say how in your notes).
- **Look at it in the live app first** → stay: the card stays here until you have.
**Context:** All checks that can be automated passed; only how the labels read is a judgement call.

**Answer:** Labels are fine (2026-11-18 14:21)

## How to check

Changed: a new **Context** section in every project's rail (☰, between Mistakes and Code). It lists what a new agent here starts with: the CLAUDE.md / CLAUDE.local.md files (user's, each folder down to the repo, other repos'), the project's Claude memory files (`~/.claude/projects/<folder dashed>/memory/*.md`) and Darg's default context. Pick one to read it as markdown; Open sends the file to its app.

Commit: `817f408` on main (branch wor-0145, merged and pushed).
Try: `pwsh -File tools/publish.ps1` from a terminal (or ⟳ Update), open Darg, click Context. Screenshot of the test run: `D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0145/.shots/integration/The_context_page_lists_the_repos_claude_md_and_shows_its_text/context.png` (worktree since removed; the tests redraw it under `.shots/integration`).
Look for: Workbench's own CLAUDE.md files, the memory files, and Default context all present and readable; whether the list labels ("Workbench: CLAUDE.md" is the folder name) read well. Not covered: global `~/.claude/philip/*` files and skills are not listed.

## QA notes (2026-11-18)

- Full suite `pwsh -File tools/test.ps1` on main (949fbf0, includes 817f408): unit 617/617, integration 61/61, no failures.
- Integration screenshot checked: Context rail section present, instructions list (GitHub, Workbench CLAUDE.md and CLAUDE.local.md, repo CLAUDE.md), Default context, picked file drawn as markdown, Open button, footer count.
- Memory listing and the memory folder name (non-alphanumerics to dashes, matches `D--GitHub-ThePhilipWilson-Workbench`) are covered by unit tests only; the integration screenshot shows 0 memory files, and Philip's real Workbench project has no `memory` folder, so memory in the UI was not seen with real data.
- Not automatable: whether the labels read well (taste). The Open button and the live published app were not driven (publish.ps1 can't run from an agent).
