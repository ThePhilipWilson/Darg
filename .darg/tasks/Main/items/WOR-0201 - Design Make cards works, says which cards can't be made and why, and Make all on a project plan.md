# WOR-0201: Design: Make cards works, says which cards can't be made and why, and Make all on a project plan

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:** design
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-25 01:49:52

## Description

> Pressing the "Make N cards" button at the bottom of a plan page currently makes none of them (seen on Seamless machine switching and its feature plans, where every plan has cards marked Blocked by: none yet nothing is made).
>
> Wanted:
> 1. Make cards works out the order the cards must be made in (within the plan, and across sibling plans where a Blocked by names a card in another plan) and creates them in that order so each one unblocks the next.
> 2. If some cards cannot be made (e.g. they wait on a card in another plan that has no id yet), highlight which ones and why, and offer "Make just the ones that can be made".
> 3. On the project plan's index page, an option to make all cards for all its feature plans (and the new story board) in one go, sorting out the cross-plan order so every card can be created, and writing the new ids into the Blocked by lines.
>
> Raised while looking at Agents and Fettle chats travel. Related: plan-cards and plan-features API commands.

Make cards on a plan page should make the cards. Where one can't be made, the page should mark that row and say why, and offer to make the rest. A Blocked by line may name a card in a sibling feature plan. On a `kind: project` plan, one Make all button makes every feature plan's cards, each on that feature's own board as today (Philip, 2026-11-24), in an order that works across plans, with the new ids written into every Blocked by.

**Likely cause of "makes none"** (from reading the code, not run): `PlanDoc` splits a Blocked by line on commas, semicolons and " and " (`Plans.cs:227-229`, `AndRx`). So a blocker named by a heading that contains any of those ("Spike script: make a session, copy it under another path, resume it", "Put .darg on a darg branch and bring today's files in") turns into fragments that match no card. `MakeCards` is all or nothing (`Plans.cs:501`): one such row stops the whole plan. The reason goes to the section's status line, which is cut off with an ellipsis (`DesignView.axaml:342`). A plan with open questions also makes nothing on the first press ("Make N anyway" is needed). Confirm this first, then fix the reference format; don't just loosen the split.

**Where:**
- Design section, a plan's page: the Cards part and its Make button, and the Features part of a project plan (`src/Darg.Desktop/Views/DesignView.axaml`, `PlanPage` and `PlanCardRow` in `src/Darg.Desktop/ViewModels/DesignViewModel.cs`, `DesignViewModel.Make` / `MakeFeatures`).
- `src/Darg.Core/Plans.cs`: `PlanDoc.Parse` (Blocked by), `PlanDoc.Find`, `UnknownBlockers`, `PlanStore.MakeCards`, `PlanStore.FeaturePlan`.
- The API's `plan-cards` and `plan-features` (`src/Darg.Desktop/ViewModels/MainViewModel.Api.cs`). Add a make-all command beside them.
- `Planner.Prompt` (it should write cross-plan Blocked by in the new form, not the prose notes in `docs/design/auto-sync.md:121` and `darg-on-its-own-branch.md:137`). Update `docs/FEATURE-PLANNING.md` and the Design part of CLAUDE.md.

**Done when:**
- On a copy of `docs/design/resume-spike.md` in a test instance, Make 4 cards makes all four in Blocked by order, each blocked by the one before by id.
- A Blocked by naming a heading that has commas or " and " in it resolves to that card. A heading in a sibling plan under the same project resolves there too; one that matches nothing, or matches cards in two plans, is flagged.
- A row that can't be made is marked on the page with its reason (unknown blocker, waits on an unmade card in another plan, under 3/3). The full reason is readable, not cut off. "Make the N that can be made" makes only those.
- The project plan's page has Make all. On a copy of the Seamless machine switching plans it makes every feature's cards on their own boards, cross-plan blockers first, with real ids in every Blocked by. A cycle across plans makes nothing and names the loop.
- `plan-cards` reports which rows can't be made and why, and the new make-all command does what the button does.
- Core tests cover the parsing and cross-plan order. An integration test presses Make all and fails without the change. `pwsh -File tools/test.ps1` passes.

**Questions left:**
- How is a card in another plan named in Blocked by: its bare heading (found across the project's plans), or `Plan title: heading`? Bare heading is the simplest, as long as clashes are flagged.
- Overlaps WOR-0200 (showing where a plan is and what waits on Philip). Unmakeable rows are this card; the wider progress view is that one.

## How to check

Changed: Blocked by keeps a heading whole (commas and " and " no longer split it) and finds cards in sibling plans (`src/Darg.Core/PlanRefs.cs`, `Plans.cs`); the Design page marks rows that can't be made with the full reason and offers "Make the N that can be made"; a project plan has Make all; API `plan-cards` reports the unmakeable rows and `plan-make-all` does what Make all does.

Branch `android` (merged from `wor-0201`), commits 6049bd2 and 82cc618.

Try: `pwsh -File tools/test.ps1 -Filter PlanMaking` and `-Filter MakeAll`; or in the app open Design on a project plan with feature plans (the Seamless plans, copied as in `tests/Darg.Core.Tests/PlanData/seamless`) and press Make all. Look for: ids in every Blocked by, cards on each feature's own board, a marked row with its reason when one can't be made, a named loop on a cycle.

Full suite passes except `TerminalReadTests.Terminal_command_returns_the_last_lines_a_console_shows`, which fails the same on the untouched `android` too (not from this card).
