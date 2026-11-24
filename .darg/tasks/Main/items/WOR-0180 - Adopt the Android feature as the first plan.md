# WOR-0180: Adopt the Android feature as the first plan

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** planning, android
- **Blocked by:** WOR-0172, WOR-0175
- **Created:** 2026-11-20
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 04:31:33

## Description

Bring the Android work into the Design section as its first real plan, so the design is tried on a feature already under way. Design: docs/FEATURE-PLANNING.md, section Android replayed, which gives what the plan says and the two cards the board never had.

**Where:**
- New `docs/design/android.md` from `docs/ANDROID.md` (which then gets a history line pointing at it) and the Android board (`.darg/tasks/Android/`).
- The Android board: Blocked by lines on AND-0004, AND-0005, AND-0007, AND-0008 and AND-0009, and cards for the two rows the board lacks (the Needs you screen; pairing and TLS in the phone app).

**Done when:**
- The Design section opens the Android plan with every AND card in its Cards part showing its live column.
- The Android board shows the blockers and has the two missing cards.
- DocsTests passes with the new doc listed in docs/README.md.

## How to check

Round 2 (fixes the three QA notes of 2026-11-21), branch `android`, commit 24454a8 (pushed), on top of round 1's aaada54:
- The four answers agents chose (AND-0001, AND-0002, AND-0003 twice) now read `(decided by agent, AND-000n, 2026-11-20…)`, so `ByAgent` is true on those four and false on ntfy (Philip's). The Design page's Keep/Change panel is bound to `ByAgent` (`DesignView.axaml:153`).
- AND-0011's blockers: the board card now says `AND-0002, AND-0003`, as the plan does (pairing in the app needs AND-0002's pairing endpoint). Every card's blockers in the plan now match the board's card files (`.darg/tasks/Android`, not committed).
- AND-0001's Where is `docs/ANDROID.md`, not a raw link.
- `DocsTests`: the Android test asserts the four by-agent cards, ntfy not by agent, the five picks, and no `](` in any card row (checked failing on the old plan). Its `Source` helper no longer walks into `bin`/`obj`: the first full run failed `Every_environment_variable_the_docs_name_is_read_somewhere` with access denied on a temp folder a ProjectDataTests test makes and deletes under bin at the same time. That's a race, not caused by this card's change; the second full run passed.

Try: open Darg, project Darg, Design section, pick Android. In Questions, the Tailscale, Thin client, `D:\Android` and app id questions should show "Decided by AND-000n's agent" with Keep and Change; ntfy should not. AND-0001's row should read `docs/ANDROID.md`. On the Android board AND-0011 should show Blocked by AND-0002, AND-0003 (both clear, since they're in QA or done, so no chips). Don't press Change unless you mean it: it sends that AND card back.
`pwsh -File tools/test.ps1 -Filter android_plan` runs the plan test.

## QA notes

### 2026-11-21 08:23

QA by an agent, on branch android at 61352e5 (holds aaada54), in a worktree of its own.

Passed:
- Full suite `pwsh -File tools/test.ps1`: unit 807/807, integration 149/149, nothing failing.
- `DocsTests` (`The_android_plan_parses_with_every_card_and_its_blockers` and the index test) pass in that run.
- The Design page, driven headless: a scratch integration test (not committed) copied the real `docs/design/android.md` and the real Android board into a throwaway instance, opened Design and picked Android. All 11 AND cards are rows with their live column (QA Ready, In Progress, Backlog); AND-0009 shows "waits on AND-0004, waits on AND-0005"; the page draws Goals, Not doing, Questions, Design, Cards and Log with no crash. Screenshots: ![Design page, Android](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0180/.shots/integration/Qa_android_plan_on_the_design_page/design-android-tall.png)
- The Android board: Blocked by lines on AND-0004, 0005, 0007, 0008 (AND-0003) and AND-0009 (AND-0004, AND-0005); AND-0010 and AND-0011 in Backlog. The `cards` API reports the same blockers; the board shows chips on AND-0009 only, which is right since AND-0003 is in QA and so clears. ![Android board](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0180/.shots/integration/Qa_android_plan_on_the_design_page/board-android.png)

To fix:
1. **The four answers an agent chose aren't recognised as an agent's choice.** They read `(agent for AND-0001, 2026-11-20, on Philip's behalf; keep or change)`, but WOR-0178 (landed after this commit) made the page recognise only `decided by agent, CARD, date` (`PlanRules.DecidedBy`). So all five questions show `ByAgent = false`: the page gives them a plain Change button and no Keep, which is the "keep or change" the text asks for. Rewrite the four answers (AND-0001, AND-0002, AND-0003 twice) as e.g. `**Answer:** Tailscale (decided by agent, AND-0002, 2026-11-20; not installed, so only the home network works for now)`, then check the page shows Keep and Change on those four and not on the ntfy one. The DocsTests test could assert that count (4 by agent, 1 by Philip).
2. **The plan and the board disagree on AND-0011's blockers.** The plan says `Blocked by: AND-0002, AND-0003`; the card file says `AND-0003` only. The commit message says the blockers match the board's, but the test only checks the plan against hard-coded values and never reads the board, which isn't committed. Pick one (AND-0002 is in QA, so it clears either way) and make the other match.
3. Small: AND-0001's Where row shows the raw `[ANDROID.md](../ANDROID.md).` because card rows strip backticks but not links. Write it as `docs/ANDROID.md`, as the other cards do.

Noted, not a fault: `parent: darg` names a plan that doesn't exist, so Android sits at the top of the tree. That's harmless until a project plan exists.

### 2026-11-22

QA round 2 by an agent on branch android at 24454a8 (the checked-out folder). Passed: full suite tools/test.ps1 unit 822/822, integration 150/150; DocsTests assert four by-agent answers (AND-0001, 0002, 0003 x2), ntfy not by agent, no links in card rows. Plan blockers match all 11 board card files (AND-0011 = AND-0002, AND-0003); AND-0001 Where is docs/ANDROID.md; docs/README.md lists design/android.md; ANDROID.md has the history line. DesignView's Keep panel is bound to ByAgent. Not re-run: a headless page screenshot this round (round 1's covered the page; ByAgent is unit-asserted).
