# WOR-0200: Design: a Needs you strip and a progress line on each plan, counts in the tree

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** design
- **Created:** 2026-11-24
- **Done:**

## Description

> In the Design section it is not obvious where a plan has got to at any point. Add more information on (1) where Philip needs to be involved, e.g. open questions, decisions, approvals waiting on him, and (2) where progress has got to, e.g. plan status (draft/agreed), which feature plans and cards are made, and what the planner agent is doing now. Raised while looking at the Auto-sync plan.

**What's wanted:** opening a plan, or glancing at the tree, answers two things at once: what's waiting on Philip, and how far the plan has got. Most of the facts are already read but are scattered or hidden: the header's one dim `Summary` line ("Feature plan, draft · 3 of 7 cards done · 2 questions open"), the tree's draft mark and `3/7`, the planner's `StateText` only in the chat panel, and the open questions only when you scroll to Questions or to the Make button. Gather them:

1. **Needs you**, at the top of the page, only when something waits: each open question, each answer an agent decided (Keep / Change), each of the plan's made cards in For Human Attention (with its decision) or in QA, and the planner asking a question or permission. Each row jumps to the question or opens the card or the planner's ask. A project plan counts its feature plans' items too.
2. **Progress**, a clearer line under the title: status (draft / agreed / building / built / dropped), features planned / drafting / proposed (project plans), cards made and done, and what the planner is doing now (drafting, working, waiting on you, idle) even when its chat isn't showing.
3. **In the tree**, a needs-you count on a plan's row (a project plan's includes its features'), beside the existing mark and `n/m`, so you can see which plan to open without opening each one.

Progress stays read, never written to the plan (docs/FEATURE-PLANNING.md, "Progress is read, never written").

**Where:** the Design section.
- `src/Darg.Desktop/ViewModels/DesignViewModel.cs`: `PlanPage` (its `Summary` is built in the refresh around line 600; `OpenQuestions` / `HasOpenQuestions`; `PlanCardRow` with each card's column and agent; `PlanFeatureRow` with its drafting state), `PlanNode` (tree row: `Mark`, `Progress`, `Tip`), and `Planner` (the plan's `AgentViewModel` and its `StateText`).
- `src/Darg.Desktop/Views/DesignView.axaml`: the page header `Border` (Title, Summary) and the tree's `TreeDataTemplate`.
- Facts come from: `PlanDoc` in `src/Darg.Core/Plans.cs` (status, `OpenQuestions`, cards, features), `PlanRules.DecidedBy` (agent-decided answers), `BoardColumns.Qa`, the attention column and `CardDecision` (cards waiting), and the planner session's pending ask.
- Colours by theme key only (`Warn` for waiting on you), per CLAUDE.md's theme rule.

**Done when:**
- A plan with an open question, an agent-decided answer and a made card in For Human Attention shows all three under Needs you at the top of the page, and clicking each lands on the question or opens the card. With nothing waiting, no strip shows.
- The progress line shows status, cards made and done, and the planner's state (e.g. "planner: waiting on you") with the chat panel hidden.
- A project plan's tree row shows a needs-you count that includes its feature plans' items, and the count drops when one is answered.
- An integration test in `tests/Darg.Desktop.Tests/DesignPageTests.cs` covers Needs you and the tree count and fails with the strip taken out; `app.Shot` PNGs of a plan with and without items.
- `pwsh -File tools/test.ps1` passes.

**Questions:**
- Should Needs you also feed the project's Home tiles or the phone's Needs you screen (AND-0010), or stay in the Design section for now? This card assumes Design only.

Separate from WOR-0201 (Make cards ordering) and WOR-0199 (committing plan edits), both open beside it.
