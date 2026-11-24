# WOR-0088: QA Ready cards get a short report too

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

Today a card agent writes a report only when it moves a card to Done (`ReportStore.Rules`, WOR-0061). Work it couldn't verify goes to QA Ready with no report, so there's no entry in Reports, no unread dot and no rail count, and Philip has to open each card to judge it.

Wanted: a QA Ready handoff also writes a short report to `<instance>/projects/<project>/reports/`, as its last step: what changed, what to look at, what was and wasn't checked, the commit. The agent still stays alive, as now. Reports becomes the one place to triage from.

Where: `ReportStore.Rules` in Core (and the rework preface, so a second round writes a fresh report). Check `AgentsViewModel.Reported`, which forgets the agent once a report is written: a QA agent must stay.

Done when: a card moved to QA Ready produces a report that shows unread in Reports, and its agent is still there.

## How to check

What changed: `ReportStore.Rules` now tells a card agent going to QA to write a QA report as its last step, `<stamp>-<card>-qa.md` with `status: qa` and sections Changed, Look at, Checked. The rework preface asks for a fresh one each round. `AgentsViewModel.Reported` skips QA reports, so the agent stays, and the Reports list shows "to check" in the row's grey line. Project CLAUDE.md updated.

Commit: `bc6c4b9` on main (pushed). Branch `wor-0088` merged and deleted.

Checked already: Core tests pass (483). On a throwaway instance (`.shots/home`, debug build), a QA report naming a finished agent showed unread, marked "to check", with the rail count, and the agent stayed. A done report for the same agent then made it go.

Not checked: a real card agent following the new rules end to end. The rules are prompt text, and this card's own agent was started on the old rules.

To try it:
1. Update your copy: ⟳ Update in the header, or `pwsh -File tools/publish.ps1` from a terminal.
2. Start Work on any small card whose work you'd want to check, or one that will end in QA anyway.
3. When it moves to QA Ready, look at Reports. Expect a new unread row "WOR-xxxx: title" with "to check · date · 1 commit" under it, Changed / Look at / Checked in the body, and the agent still in the Agents list.
4. Optional: ↩ Send back that card and check that the rework agent writes a second `-qa.md` report rather than editing the first.

## Automated QA

Verified: bc6c4b9 is on main. Read the diff: `ReportStore.Rules` adds the `-qa.md` report with `status: qa` and Changed / Look at / Checked sections, and the rework preface asks for a fresh one. `AgentReport.Qa` is parsed from `status: qa`, `AgentsViewModel.Reported` skips QA reports so the agent stays, and the Reports row shows "to check". The ReportStore tests pass (13) and the Desktop project builds with 0 errors.

Full Core run: 526 of 527 pass. The one failure, `TicketTitleTests.Retitling_a_short_note_keeps_its_words_at_the_top_of_the_description` (WOR-0082's test, file name T-0002 vs T-0001), fails the same way on its own and touches none of this commit's files, so I'm treating it as unrelated. It may need its own card.

Could not check: the live Reports UI (unread dot, rail count, agent still listed), a real agent following the new prompt, and the Send back round.
