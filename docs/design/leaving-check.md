---
kind: feature
status: agreed
parent: seamless-machine-switching
board: Leaving check
---
# Leaving check

Before you leave a machine, Darg tells you what won't be on the other one: code not committed or not pushed, an agent still working, `.darg` changes not pushed. It pushes what it safely can. It runs when Darg closes and from a button.

## Goals
- Close Darg on the PC, and know within a second whether the Mac will have everything, or exactly what it won't.
- Never find out on the Mac that a branch wasn't pushed or an agent was cut off mid-turn.
- Get what Darg is allowed to push pushed for me, so "leaving" is one action, not a round of `git status` in each repo.
- Run it by hand any time (a button, and a Fettle or phone command) without closing anything.

## Not doing
- Syncing uncommitted code. The check says it is there; git is how code moves (project plan, Not doing).
- Committing code for me. Commits stay under each repo's commit rule (`push`, `commit`, `ask`, `never`), and no rule means no commit.
- Moving an agent mid-turn, or making its conversation travel. That is the agents feature; this only says a turn is still running.
- Doing the `.darg` sync itself. Auto-sync owns committing and pushing the `darg` branch; this asks it to push now and reports the result.
- Checking the other machine. It reports what this machine hasn't sent, not what the other has received.
- A scheduled or recurring check, or a nag while working. It runs on close and on request only.

## Questions

### On close, when something won't reach the other machine, what happens?
- **Show the list with Leave anyway and Stay open** (recommended): closing waits on a short panel listing the problems after Darg has pushed what it can. Nothing to read means it closes at once, as now.
- **Show the list but close anyway**: the same panel as a toast after the window has gone. Never blocks, but too late to act on.
- **Only when I press the button**: closing never checks. Nothing in the way, and nothing to catch a forgotten push.

**Context:** closing today saves agents and exits. A panel that can stop it is a new kind of interruption, so it should only appear when there is something to say.

**Answer:** Show the list with Leave anyway and Stay open (Philip, 2026-11-24)

### A live agent is mid-turn when I leave. What does the check offer?
- **Wait for the turn to end, up to a limit, or Leave anyway** (recommended): the panel shows the agent working and closes by itself once its turn ends. Leaving anyway cuts the turn off, as closing does now, and says so.
- **Only name it**: it is listed with Leave anyway and Stay open, with no waiting.
- **Stop it cleanly, then close**: ends the turn at the next safe point so its conversation is saved whole. Slower, and needs a way to ask an agent to stop that Darg may not have.

**Context:** WOR-0183 forgets a cut-off task agent anyway, and the project plan says an agent belongs to its machine until its turn ends.

**Answer:** Wait for the turn to end, up to a limit, or Leave anyway (Philip, 2026-11-24)

### Should the check commit unsaved work to a branch so it can travel?
- **No: report it only** (recommended): uncommitted files are listed per repo, and commit rules apply only to commits already made.
- **Yes, a WIP commit on a `darg-wip/<branch>` branch, pushed**: nothing is lost if I forget, but it makes commits in code repos I didn't ask for, and half-done code lands on a remote.
- **Ask per repo**: a button on each repo's line makes the WIP commit when I tap it. Nothing happens unseen, with one more tap each time.

**Context:** the project plan says uncommitted code isn't synced. A WIP branch would be a way of syncing it, so this is for me to widen that line or not.

**Answer:** No: report it only (Philip, 2026-11-24)

## Design

### What it builds on
- `Repo.Status` already gives branch, ahead, behind and changed files per repo. Uncommitted is `Changed`, unpushed is `Ahead`, and "no upstream" is a branch with none set (needs one more git call).
- `Commits.For(project, folder)` says what Darg may do per repo: only `push` pushes; `commit`, `ask` and `never` mean report. No rule: report, as Fettle never commits.
- Live agents are known to `AgentsViewModel` (working, waiting on me, set to carry on). Card agents' branches (`CardBranch`) live in worktrees of their own, so the check reads those branches' ahead counts too.
- The window's `Closing` handler in `MainWindow.axaml.cs` currently runs `vm.SaveAll()` and nothing else. The check goes in front of it, and `e.Cancel` keeps the window open while the panel shows.
- Project plan siblings: auto-sync owns the `darg` branch push, and the resume and agent features decide what an agent's record looks like. Until those land, the `.darg` line reads the worktree with plain git (`git -C .darg status`, `rev-list`), and says "not synced yet" when `.darg` isn't a worktree.

### The parts
1. **The report (`LeavingCheck` in Core).** For one project: a list of findings, each with the project, repo, kind, a sentence, and whether it can be fixed by pushing. Kinds: uncommitted files, unpushed commits on the current branch, unpushed card branches, a branch with no upstream, a repo with no remote, a live agent, `.darg` changes uncommitted or unpushed, and a push that failed. It is pure over data (status records, agent snapshots, rules), so it is unit tested without git or a window.
2. **Push what it can.** For each repo whose rule is `push` and which has a remote: push the current branch and any unpushed card branches (never force, never to a branch other than its own upstream). A rejected push, a repo behind its remote and a missing sign-in are findings, not errors. GitHub pushes use the project's account the way other GitHub calls do. For `.darg`, it asks auto-sync for a commit and push now once that exists, and until then does it with git itself. All git runs off the UI thread with a timeout, so a dead network is "offline" in a few seconds, not a hung close.
3. **The panel.** One list for the open project first, then other projects with findings, each line a sentence and what can be done (open the repo's Code section, open the agent, push again). Buttons: Check again, and on close Leave anyway and Stay open. It opens from the header beside the ⟳ menu as "Leaving check", and from the close handler only when there are findings that remain after pushing. The wording says "won't reach the Mac" by naming the other machine from the project plan's machine record, else "the other machine".
4. **Close behaviour.** The close handler runs the check, shows the panel only if something is left, and honours the Question answers for live agents and for what Leave anyway does. A second close request while it is running does nothing. Closing from the OS (shutdown) can't be cancelled, so the check there only pushes and writes the list to the footer line.
5. **Commands.** The API's `leaving-check {project?, push?}` returns the findings (PC-only: it pushes), so Fettle can say "you have three unpushed commits in Site" and a phone can read it through `needs-you` later. Read-only mode (`push: false`) is the safe default for Fettle.

### Risks
- **A slow or failing push blocks closing.** Mitigated by timeouts and by Leave anyway always being there.
- **Pushing to the wrong place.** Only the branch's own upstream, never forced, and a public repo is subject to the names-never-to-commit check before any push, as the project plan requires.
- **Cutting an agent off when closing from the panel.** The panel says which turns will end, and WOR-0183 already treats those as done with.
- **A check that cries wolf** teaches me to click Leave anyway. Anything not about reaching the other machine (a stash, ignored files, a repo with no remote at all and no rule) is left out.

## Cards

### LEA-0001: Leaving check report
A pure `LeavingCheck` in Core that takes a project's repo statuses, commit rules, live agent snapshots and `.darg` state and returns a list of findings (kind, repo, sentence, fixable by push). It covers uncommitted files, unpushed commits, branches with no upstream, repos with no remote, live agents, unpushed card branches and `.darg` changes not pushed. Cover every kind with unit tests.
**Where:** `src/Darg.Core/LeavingCheck.cs`, `src/Darg.Core/Repo.cs`, `tests/Darg.Core.Tests/LeavingCheckTests.cs`
**Done when:** unit tests build each kind of finding from fake data, a clean project returns an empty list, and a repo whose rule is not `push` is reported rather than pushed.
**Blocked by:** none

### LEA-0002: Push what the commit rules allow
Add a push step to the leaving check: for each repo with a `push` rule and a remote, push the current branch and any unpushed card branches to their own upstream, never forcing, off the UI thread with a timeout. A rejected push, a repo behind its remote or a failed sign-in becomes a finding instead of an exception. Use the project's GitHub account for the call.
**Where:** `src/Darg.Core/LeavingCheck.cs`, `src/Darg.Core/Repo.cs`, `tests/Darg.Core.Tests/LeavingPushTests.cs`
**Done when:** a test with a real throwaway repo and a bare remote shows a `push` repo pushed and its finding gone, a `commit` repo left alone and reported, a diverged remote reported as a finding, and a timeout reported as offline.
**Blocked by:** Leaving check report

### LEA-0003: Leaving check for `.darg`
Make the check read the `.darg` state: uncommitted changes and commits not pushed on the `darg` branch, using plain git on the `.darg` worktree, and "not synced yet" when `.darg` is a plain folder. Push it when the project has a remote, and take over to auto-sync's push-now when that exists. Never write a commit message that carries a name from the never-commit list.
**Where:** `src/Darg.Core/LeavingCheck.cs`, `src/Darg.Core/ProjectData.cs`, `tests/Darg.Core.Tests/LeavingDargTests.cs`
**Done when:** tests show a worktree with a change and one unpushed commit reports both and pushes after the step, and a plain `.darg` folder reports "not synced yet" without error.
**Blocked by:** Leaving check report

### LEA-0004: Live agents in the check
List agents that are working, waiting on me or set to carry on after a usage reset in the check, with the repo or card they are on, and add the wait step: hold until the turn ends, up to a limit, if the Question's answer says so. Say plainly that leaving anyway ends a turn.
**Where:** `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `src/Darg.Core/LeavingCheck.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test with fake claude mid-turn shows a finding for the agent, the finding clearing when the turn ends during the wait, and the wait giving up at its limit.
**Blocked by:** Leaving check report

### LEA-0005: The Leaving check panel and button
Add a Leaving check panel to the main window, opened from a header button beside the ⟳ menu, listing each project's findings as sentences with what can be done, a Check again button, and a pushed-just-now note. It uses theme keys, not hex, and a list row says what it is.
**Where:** `src/Darg.Desktop/Views/MainWindow.axaml`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Desktop/ViewModels/LeavingCheckViewModel.cs`, `src/Darg.Desktop/Views/LeavingCheckView.axaml`
**Done when:** an integration test opens the panel on a throwaway project with an unpushed commit, sees the finding, pushes, presses Check again, sees it clear, and leaves a screenshot.
**Blocked by:** Push what the commit rules allow, Live agents in the check

### LEA-0006: Check on close
Run the check from the window's Closing handler before `SaveAll`: push what it can, then if findings remain keep the window open (`e.Cancel`) and show the panel with Leave anyway and Stay open, else close as now. A second close request while it runs does nothing, and an OS shutdown only pushes and writes the list to the footer.
**Where:** `src/Darg.Desktop/MainWindow.axaml.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test shows closing with nothing left closing at once, closing with an unpushed commit staying open with the panel, and Leave anyway closing and saving agents as before.
**Blocked by:** The Leaving check panel and button, Leaving check for `.darg`

### LEA-0007: The leaving-check API command
Register `leaving-check {project?, push?}` in the Darg API, returning the findings as JSON and pushing only when `push` is true. It is PC-only and left off the phone's list. Add it to Fettle's briefing with its usage line, so asking "can I leave?" gets a real answer.
**Where:** `src/Darg.Desktop/ViewModels/MainViewModel.Api.cs`, `src/Darg.Core/Remote.cs`, `tests/Darg.Desktop.Tests`
**Done when:** a test calls the command through the API on a throwaway project and gets the findings back, `push: false` pushes nothing, and the phone's server answers 403.
**Blocked by:** Leaving check report
