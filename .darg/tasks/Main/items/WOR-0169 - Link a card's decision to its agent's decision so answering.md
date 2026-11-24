# WOR-0169: Link a card's decision to its agent's decision so answering one closes the other

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:** 2026-11-21
- **Moved:** 2026-11-21 02:05:00

## Description

When a card has a decision (For Human Attention), the agent responsible for it should show that same decision too, and the two should be linked. Answering or closing the decision on the card should close it on the agent, and vice versa. Raised while looking at WOR-0156.

## QA notes

### 2026-11-21 (QA agent, at d475dfe in worktree .shots/qa-0169)

- **The agent's row keeps saying "WOR-xxxx waits on your decision: …" after the card is answered anywhere but on the agent.** `AgentViewModel.CloseDecision` (src/Darg.Desktop/ViewModels/AgentsViewModel.cs) closes the item but never resets `Activity`, which `ShowDecision` set. Repro (probe test `Probe_activity_after_answered_on_the_card`, left untracked in `.shots/qa-0169/tests/Darg.Desktop.Tests/Qa0169ProbeTests.cs`): the card agent finishes, writes a Decision, the card goes to For Human Attention and the decision shows on the agent; then the API's `decide` with "Works" moves the card to Done and closes the box, but `agent.Activity` still reads "T-0001 waits on your decision: Does dictation work…". By the same code path the stale line is left when the card is moved out of attention by hand, when its Decision is deleted, and on the old agent when the decision moves to a newer one ("Now shown on the card's newer agent"). Fix: when an item closes from outside, set Activity to its outcome (as `Pick` does) if Activity is still the "waits on your decision" line; add an integration test that the row stops saying it waits.
- Everything else passed: the card's 4 DecisionTests, plus probes for Asked by winning over a newer agent and no Asked by going to the newest (the old one closes), a rewritten Decision swapping in place (one item, new question), an own typed answer going to the asking agent with the card to In Progress, a Decision deleted by hand closing with "no open decision", and a card whose Asked-by agent doesn't exist (no crash, nothing shown). The screenshot of the box reads as clearly as the card panel's: recommended first, each option saying what it does.
- Full suite (`tools/test.ps1`): integration 130/131, the one failure being the probe above. Unit 764/765: `UpdaterTests.The_helper_copies_the_new_build_in_and_starts_the_app_again` failed once ("the app wasn't started again", a 10 s wait for a pwsh it starts) and then passed twice when run alone. This card changes no Core file, so this work doesn't cause it; it looks sensitive to machine load.

### 2026-11-21 round 2 (QA agent, at 013786e in worktree .shots/qa-0169-r2): passed

- Round 1's note is fixed. The card's two DecisionTests pass and fail with `AgentsViewModel.cs` reverted to 56e2b42.
- Probes (`.shots/qa-0169-r2/tests/Darg.Desktop.Tests/Qa0169R2ProbeTests.cs`, not committed): approved on the card reads "T-0001: settled on the card: approved, moved to done"; when the decision moves to a newer agent, the old row reads "now shown on the card's newer agent" and the new row waits on the new question; a newer activity line is left alone; a pick on the agent reads "you picked Works". The first two fail without the fix. When the Decision is deleted, Darg's nudge starts a turn first, so the row moves on to real work and the guard rightly leaves it.
- Full suite: unit 790/790, integration 140/141. The one failure is `DesignPageTests.The_design_page_shows_a_plan_in_its_parts_takes_answers_and_links_its_cards` (CRLF vs LF in its Goals assertion). It fails the same way with this card's fix reverted: it's WOR-0175's, which is in QA.

## How to check

**This round (rework of the QA note):** the agent's row in the agent list no longer keeps saying "WOR-xxxx waits on your decision: ..." once the card's decision is closed from outside. `CardDecisionItem.Close` (src/Darg.Desktop/ViewModels/AgentsViewModel.cs) now sets the row to how it closed, e.g. "WOR-0169: settled on the card: sent back", but only while the row still says the card waits (so a newer activity line is left alone). That covers answering on the card (panel, API `decide`, phone), moving the card out of attention by hand, deleting its Decision, and the old agent when the decision moves to a newer one.

**Commit:** 013786e on `android` (on top of the first round's 87cf5f6 and d475dfe).

**Tests:** `pwsh -File tools/test.ps1 -Integration -Filter DecisionTests`. `Answering_the_cards_decision_closes_it_on_the_agent` and `A_card_moved_out_of_attention_closes_its_decision_on_the_agent` now check the row; both fail with the fix stubbed out.

**To try it in the app** (after a Update): have a card agent finish, write a `## Decision` into its card (Asked by: agent <its id>) and move it to For Human Attention. In Agents, its row reads "... waits on your decision". Answer from the card panel instead of the agent: the row should change to "WOR-xxxx: settled on the card: ...", and the box under its last reply shows the same outcome.

**What the box looks like** (first round's shot):
![decision on the agent](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0169/decision-on-agent.png)
