# WOR-0178: Plans stay current as cards land

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning, agents
- **Blocked by:** WOR-0177
- **Created:** 2026-11-20
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 04:26:21

## Description

A plan holds intent and the board holds state, so a plan is never rewritten to say how far along it is (ANDROID.md still said the phone app wasn't built after AND-0003 had built it). What does change a plan is a choice made on the way: the AND-0002 agent picked Tailscale and the AND-0003 agent picked the app id on Philip's behalf, and the plan never heard. Design: docs/FEATURE-PLANNING.md, section Keeping it current.

**Where:**
- The card agent's prompt (Desktop's card prompt, beside `ReportStore.Rules`): a card made from a plan gets the plan's path and a rule: a choice the plan didn't settle goes into its Questions as decided by the agent, with the pick, in the same commit.
- The Design page: a question decided by an agent shows as such, with Keep and Change; Change sends the card back (`send-back`) with the new answer as notes.
- The page's Log part, built from the plan's cards' reports (`ReportStore`) and moves (`ColumnWatch`), newest first, never stored. The plan shows as building, then built once every made card is done.
- `Stories` (WOR-0074): a story doc links the plan its cards came from.

**Done when:**
- A fake card agent that writes a decided-by-agent question shows on the page with Keep and Change; integration test.
- The Log lists a finished card with its report link and nothing is written to the plan file.
- A wrap-up prompt names the plan (unit test on `Stories.Prompt`).

## How to check

Changed (QA rework, round 2): branch `android`, commit d9d6671 (merged and pushed; earlier work in f3e1cd6). Fixes the three QA notes:
1. `PlanRules.DecidedBy` now reads an agent's choice however it is worded (`agent for AND-0002 ... on Philip's behalf`, `decided by AND-0003's agent`, `chosen by an agent`, `by agent AND-0004`, `AND-0005 agent`), taking the first card id in the note. The real android.md's answers read 4 by an agent, 1 by Philip (unit test with its lines).
2. The Change toggle reads Cancel while the options show, so Keep is only the button that puts your name on an agent's answer.
3. The card agent's prompt names the plan relative to its repo (`docs/design/android.md`, "in your own checkout ... your worktree") instead of the main checkout's absolute path.

To try: `pwsh -File tools/test.ps1 -Unit -Filter PlansTests` and `-Integration -Filter DesignPageTests`; then open Design on the Darg project in the app: android.md's Tailscale, app id, thin client and Android folder answers should show the amber "Decided by AND-000x's agent" line with Keep, and Change should turn to Cancel when open. I did not look at the page myself after the change (headless only).

Not done: the Log lists reports only, not moves (ColumnWatch keeps no history, so a move with no report has nowhere to come from without storing it).

## QA notes

### 2026-11-22 04:00

QA by an agent, on branch android at 61352e5 (holds f3e1cd6), in a worktree of its own (`.shots/qa-0178`).

Passed:
- Full suite `pwsh -File tools/test.ps1`: unit 807/807; integration 148/149. The one failure, `DesignPageTests.The_make_button_makes_the_ticked_cards_after_a_second_press_on_open_questions_and_refuses_a_thin_card` (WOR-0177's test, timed out after 30 s on "the page reading the made cards"), passed 3 of 3 when run on its own. It's intermittent under full-suite load. This card's code isn't on that path (Make cards, then the page reloading), and the test only exists from c5ea203 on, after this card, so there's no "without this card" run of it. It wants a ticket of its own.
- How to check 1: `DesignPageTests` (WOR-0178's two tests included), `PlansTests` and `StoriesTests` pass.
- How to check 3: the integration test checks that the work-on prompt names the plan file and gives the "decided by agent, CARD" rule. It passes.
- How to check 2, driven headless (scratch integration test, not committed): a plan with `**Answer:** Tailscale (decided by agent, T-0001, 2026-11-21)` shows the amber "Decided by T-0001's agent" line with Keep, and a plain answer by Philip shows no Keep. ![decided question](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0178/.shots/integration/Qa0178_shots/decided.png)

To fix:
1. **None of the real plan's agent choices are recognised.** `docs/design/android.md` is the only plan there is, and it records the choices made for Philip as `(agent for AND-0002, 2026-11-20, on Philip's behalf; keep or change)`. `PlanRules.DecidedBy` only matches the words `decided by agent`, so a scratch unit test on the real file found **0 of 4** (AND-0001, AND-0002, AND-0003 twice): no Keep, no amber line, and Change sends nothing back. These are the card's own examples (Tailscale, the app id). WOR-0180's QA notes ask for those four lines to be rewritten. This card's side is the dev-wide "defend against the class" rule: agents drift from the shape they're told. Have `DecidedBy` take the variants, such as `agent for AND-0002`, `decided by AND-0002's agent`, `chosen by an agent`, `on Philip's behalf`, and `by agent` with the card id before or after it. Add a test that parses the real android.md (or a copy of its answer lines) and expects 4 decided by an agent and 1 by Philip.
2. **Two buttons called Keep while an answer is being changed.** With Change open, the old Change toggle reads "Keep" (`DesignView.ChangeLabel`: closes the options and writes nothing), right under the new primary Keep (writes Philip's name over the agent's). The same word does two different things. Rename one (for example "Cancel", or have the toggle read "Close" on a question decided by an agent). ![changing an agent's answer](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0178/.shots/integration/Qa0178_shots/decided-changing.png)
3. **The plan path given to a card agent is the main checkout's.** `PlanRules.Prompt(pd.File, ...)` passes the absolute path under the main checkout's `docs/design/`, while `CardBranch` puts the agent in `.claude/worktrees/<card>` and the rule says to write the choice "in the same commit as the work". An agent that edits the path it's given changes the main checkout's copy: it goes uncommitted there and is missing from its branch. Give the path relative to the repo (`docs/design/android.md`, "in your worktree"), as well as or instead of the absolute one.

Not checked: the Log's moves (`ColumnWatch`), which the How to check section says were not done: the Log lists reports only. Done when doesn't ask for moves, so this isn't held against the card, but the Where line does.

### 2026-11-22 QA round 3

QA by an agent on branch android at d9d6671 (the main checkout, already on it). Full suite `pwsh -File tools/test.ps1`: unit 822/822, integration 150/150, no failures (the WOR-0177 test that timed out last round passed).

Rework notes checked: 1) `PlansTests` covers `DecidedBy` across the wordings (agent for AND-0002 on Philip's behalf, decided by AND-0003's agent, chosen by an agent, by agent AND-0004, AND-0005 agent) and Philip's answers returning null; the real android.md now has 4 "decided by agent, AND-000x" answers and 1 by Philip, and all are recognised. 2) `ChangeLabel` reads Cancel while changing, with a test. 3) the card prompt names `docs/design/phone.md` relative to the repo, with a test.

Not checked: how the page looks after the change (headless tests only, no screenshot reviewed by me), and the Log's moves, which were declared not done. Neither is in Done when.
