# WOR-0199: Design page commits every plan edit and keeps all its state in the plan file

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-25 00:28:10

## Description

**What's wanted:** every edit the Design page makes to a plan file is committed at once, so the plan's state moves with git as it is. Philip's call, 2026-11-24, reversing his 2026-11-20 one ("the page commits nothing"). Along with that, the Design state that's only in memory today goes into the plan file too: which card and feature rows are unticked (`DesignViewModel._unticked`) and which planner chat belongs to the plan (now only `AgentRecord.Plan` in app data).

**Where:** `PlanStore` in Darg.Core (`src/Darg.Core/Plans.cs`): every write goes through its one write path, which raises `PlanStore.Edited`. Commit there, or on `Edited` in `DesignViewModel`. Covers `SetSection`, `Answer`, Keep/Change, `MakeCards` ids and `SetFeatureLink`. Whether it pushes as well follows the project's commit rule for that repo (`Commits` in Core; Darg's is push). Only the plan file is staged, never other changes in the repo. A planner's own turn is left to the planner (it commits per the repo's rules). Update CLAUDE.md's Design paragraph, which says the page commits nothing.

**Done when:**
- Editing a part, answering a question, Keep, Change, Make cards and Draft feature plans each leave a commit touching only that plan file (and a push where the rule says push). An integration test shows it and fails with the commit taken out.
- Unticking a row survives a restart and is in the plan file.
- A plan's planner chat is found from the plan file on a fresh instance.
- `pwsh -File tools/test.ps1` passes.

## Decision answered 2026-11-25 00:28

**Question:** QA passed. Do the four commit choices the agent made for you stand: `ask` or no rule commits without pushing, every tick is its own commit, a planner closed with × comes back when its plan opens, and KeepOnly's put-back isn't committed?
**Asked by:** agent (QA of WOR-0199), 2026-11-25
- **Keep them as built** (recommended) → done: the card is done as it is.
- **Ticks shouldn't each make a commit** → back: sent back to fold tick changes into the next commit instead of one commit per click.
- **Change another of them**: say which one and how; your answer starts a card agent on it.

**Context:** The automated checks all pass, and the four choices above are listed under "Choices I made for you" in How to check. One thing to know for Darg's own project: its plans live in the main checkout, whose local `android` has diverged from `origin/android`. Every page edit there will commit onto the old-history branch, and the push will fail ("Committed, but the push failed", which the pre-push hook would refuse anyway) until that checkout is moved onto `origin/android`.

**Answer:** Keep them as built (2026-11-25 00:28)

## How to check

**What changed** (commit `42f2285` on `origin/android`, pushed; the same change is `d7b0c90` on the old-history local `android`):
- Every edit the Design page makes is committed straight away as `Design: <file>: <what>`. That covers a part, an answer, Keep, Change, a tick, Make cards' ids and Draft feature plans' links. Only the plan file goes in (`git add` + `git commit --only -- <file>`), so other changed or staged files stay as they were. It runs in the background, in order, and pushes when the project's `commits` rule for that folder is `push`. The status line adds "Committed and pushed." / "Committed." / "Not committed: …". Code: `src/Darg.Core/PlanCommits.cs`, calls in `DesignViewModel`.
- Unticked rows now live in the plan: `unticked: ["heading", …]` for cards and `unticked-features: [...]` for features (`PlanStore.SetList`, read as `PlanDoc.Unticked` / `UntickedFeatures`). `DesignViewModel._unticked` is gone.
- The planner chat's session is written into the plan as `planner: <session id>` when its turn ends. A plan whose `planner:` names a chat no listed agent has gets that chat listed again (`AgentsViewModel.AdoptPlanner`), and a message to it resumes the session (`--resume`). Planners are told to leave those three properties alone.

**Choices I made for you** (say if any are wrong):
- Rule `never` → the page doesn't commit. Rule `ask` or no rule → it commits but doesn't push. Only `push` pushes.
- Every tick toggle makes its own commit. Ticks are page edits, and the card asked for every edit to be committed.
- A planner you forgot with × comes back when you open its plan, because the plan still names its session.
- `KeepOnly`'s put-back after a held planner turn still isn't committed.

**Branches:** local `android` (the main checkout) is on the old history and has diverged from `origin/android` (341 ahead, 531 behind). So, as WOR-0200 did, the commit was cherry-picked onto `origin/android` in a worktree, the suite rerun there, and pushed as a fast-forward (`42f2285`). Local `android` still holds `d7b0c90` and was never pushed.

**Try it:** run `pwsh -File tools/test.ps1 -Filter PlanCommit` (the integration test drives every page action against a docs repo with a bare remote). In the app, from a build of `origin/android` at or after `42f2285`: open Design on Darg, untick a card row on any plan with proposed cards, then run `git log -1 --stat` in the repo. It should show one `Design: <plan>.md: unticked cards: …` commit touching only that file, pushed. Restart Darg: the row is still unticked.

**Tests** (run on both local `android` and `origin/android`): unit 847/847. Integration 214/215: `TerminalReadTests.Terminal_command_returns_the_last_lines_a_console_shows` fails, and fails the same way on the base commit 8df9673 without this work ("marker-omega-9152" not found in the console's lines).

## QA results (2026-11-25)

Checked `42f2285` (`origin/android`) in a fresh worktree, `.shots/qa-0199-r2`.

- **Whole suite** (`pwsh -File tools/test.ps1`): unit 847/847, integration 214/215. The one failure is `TerminalReadTests.Terminal_command_returns_the_last_lines_a_console_shows` ("marker-omega-9152" not found). It fails the same way on the base commit `4072f74` without this work, so it isn't from this card, but it still needs its own ticket.
- **The new tests can fail.** Breaking the code on purpose made them fail: with no commit at all, the edit test times out waiting for a commit; committing everything staged instead of `--only` gives "design/phone.md\nnotes.txt"; with the file's `unticked` ignored, the fresh page shows the row ticked; with `NotePlanner` disabled, the planner test times out; with `AdoptPlanner` unwired, the fresh-instance test gets null. All three pass again with the code back.
- **Live in the real debug app**, on a throwaway instance (`.shots/live-0199`: a docs repo with a bare remote and the rule `push`), driven by `.shots/live-0199.ps1`. UI Automation unticked "Pairing in the app". That gave one commit, `Design: phone.md: unticked cards: Pairing in the app`, touching only `design/phone.md` and pushed (the remote's main matched it). The unrelated `M PLAN.md` was left as it was, and `unticked: ["Pairing in the app"]` was in the file. After closing and relaunching the app, the row was still unticked (screenshot `.shots/qa-0199-r2/.shots/live-0199/check.png`).
- **Not automated:** nothing. What's left is a choice that's Philip's (the Decision above), not a gap in what could be checked.
