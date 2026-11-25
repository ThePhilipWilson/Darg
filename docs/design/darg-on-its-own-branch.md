---
kind: feature
status: draft
parent: seamless-machine-switching
planner: 1c9fff9a-cfb0-4023-88e1-c6324ca85dd4
---
# .darg on its own branch

Each repo's `.darg` folder becomes a git worktree on a `darg` branch with no code history, which Darg alone commits, pulls and pushes. The `.darg` folders on the PC and the Mac today merge into it without losing a file, the cards DAR-0003 force-added come off Darg's code branches, and a Sync now button runs one round by hand.

## Goals
- Press Sync now on the PC, press it on the Mac, and both machines show the same cards, notes, reports, mistakes and artifacts, with every file either one had before.
- Never lose a card to a git command again. Tonight a planner's `git pull --rebase --autostash` checked out `origin/android`, which tracks `.darg/tasks`, and when the rebase was aborted git deleted 210 card files.
- Keep Darg's own state out of the public repo: a public repo's `darg` branch goes only to a private repo of its own.
- Code branches carry code only. No board file on main, and nothing in an agent's fast-forward merge touches `.darg`.

## Not doing
- Syncing by itself: the timer, on-open, on-focus, after-changes and on-close rounds, the footer's sync state, the Sync panel, the cross-process sync lock, generated commit messages, and the `sync` and `sync-keep` API commands. All of that is Auto-sync, which builds on this feature's round.
- Keeping both sides of a conflicted file (`SyncConflicts`). That is Auto-sync's first card. This feature calls it from the first-sync merge and, until Auto-sync's round lands, stops a pull at a conflict without merging.
- Ranks, next id from the card files, board.json merging and renumbering a clashing id. Those are Merge-safe boards.
- project.json and the split of instance.json. That is Project settings in the repo.
- Agents' records and conversations in `.darg`. That is Agents and Fettle chats travel.
- Making private repos on GitLab or other hosts. Only GitHub gets the private `<repo>-darg`. Other hosts follow the visibility Question.
- Rewriting old history. Commits from before this feature that track `.darg/tasks` stay as they are.

## Questions

### Which code branches lose the `.darg` files DAR-0003 force-added?
- **Every branch on GitHub that tracks them: main, android and mac-path-fixes** (recommended): a checkout of any branch that tracks `.darg` in the main checkout can delete cards, which is how 210 went tonight (from `origin/android`, which tracks 214). One `git rm --cached` commit on each. They are pushed only with your OK, since the repo is public.
- **main only**: as the parent plan says. android and mac-path-fixes keep tracking them until they merge into main, and checking either out stays a risk until then.
- **main and the branch this checkout is on (android)**: covers the branch you work on now, but leaves mac-path-fixes, which the Mac has used.

**Context:** `git ls-tree` shows 4 files under `.darg` on origin/main and origin/mac-path-fixes, and 214 on origin/android. When a checkout overwrites ignored files, git doesn't warn.

**Answer:** Every branch on GitHub that tracks them: main, android and mac-path-fixes (Philip, 2026-11-24)

### A sync round finds a lot of `.darg` gone. What does it do?
- **Puts them back from the branch and says so** (recommended): if a round finds more than 10 files, or a whole board folder, deleted and Darg didn't delete them, it restores them from the last commit instead of committing the deletion. The status line names how many came back. A deletion Darg made itself (a forgotten agent, an archived report) commits as usual.
- **Stops syncing and asks**: nothing is committed or restored until you pick "they were meant to go" or "put them back".
- **Commits it like any other change**: simplest, but a deletion like tonight's would reach the other machine on the next push.

**Context:** without a guard, Auto-sync would commit tonight's deletion within 30 seconds and the Mac would delete the same 210 cards on its next pull.

**Answer:** Puts them back from the branch and says so (Philip, 2026-11-24)

### A repo whose visibility Darg can't read (GitLab, another host, gh not signed in): where does its `darg` branch go?
- **Nowhere: committed locally only, and the footer says why** (recommended): nothing is pushed unless Darg knows the remote is private. Setup gets a "this repo is private" tick to override it.
- **To its own remote, as for a private repo**: works for a private GitLab repo straight away, but a public one publishes every card and note.
- **Asked once per repo on its first Sync now**: you say private or public, and Darg keeps the answer in instance.json.

**Context:** Darg reads GitHub visibility with `gh repo view --json visibility` as the project's account. GitLab has its own API (`GitLab.cs`), but making a private repo there is out of scope.

**Answer:** Nowhere: committed locally only, and the footer says why (Philip, 2026-11-24)

### Should a `darg` push scan for the names that must never be committed?
- **No: a `darg` branch only ever goes to a private remote, and that is the guard** (recommended): the push checks visibility every time, so it never goes to a public one. The names list stays in CLAUDE.local.md, where agents read it.
- **Yes, from a list in project settings**: every push is grepped and refused on a hit. A second guard, but the list has to move from CLAUDE.local.md's prose into a file Darg reads, and a card or note that mentions a game by name would block syncing.

**Context:** the parent plan's risks say the check runs on every `darg` push. The answer about public repos, which came later, sends their state to a private repo instead.

**Answer:** No: a `darg` branch only ever goes to a private remote, and that is the guard (Philip, 2026-11-24)

### Projects whose boards are in the vault: does this feature move them into their repo's `.darg`?
- **Yes, as part of that project's first sync** (recommended): boards and notes are copied into `.darg/tasks` and `.darg/notes`, the copy is checked, the config is repointed, and the vault's copy is renamed to `<folder>.moved-to-repo` rather than deleted. No sibling feature owns this.
- **A feature of its own, after this one**: this feature stays to the branch and the sync, and the move gets its own plan.
- **Project settings in the repo does it**: it already rewrites where a project's things live.

**Context:** the parent plan answered that vault boards move into their repos' `.darg`, but none of its features names the move.

**Answer:** Yes, as part of that project's first sync (Philip, 2026-11-24)

### When does a project's `.darg` first go onto the branch?
- **On its first Sync now, after one inline confirm** (recommended): the button says what it will do ("Make ThePhilipWilson/Darg-darg (private), move .darg onto a darg branch, push"), and Yes does it. Projects nobody presses it for stay as they are, and Auto-sync only runs on projects that have the branch.
- **At start, for every project with a repo**: like today's `ProjectData.Migrate`. Nothing to press, but it makes private repos on GitHub without asking.
- **A switch per project in Setup**: deliberate, but one more place to visit before anything syncs.

**Context:** the first sync moves the folder, may make a repo on GitHub, and pushes. Each later one only commits, pulls and pushes.

**Answer:** On its first Sync now, after one inline confirm (Philip, 2026-11-24)

## Design

### What it builds on
- `ProjectData.Root` puts every working file in `<first repo>/.darg`, and Reports, Mistakes, Artifacts and Flows all go through it. Nothing that reads `.darg` changes: the folder only becomes a worktree.
- `.darg/` is in `.git/info/exclude`, which every worktree of the repo shares, so the main checkout's `git status` and agents' `git add -A` never see the nested worktree.
- Agents' worktrees are separate folders and don't contain `.darg`. Card agents remove only their own worktree (`CardBranch`).
- `GitHub.Run` and `Gh.For(account)` already run gh with a project's account token in `GH_TOKEN` for one call, without switching the active account. `Repo.Git` and `LiveCopy.Git` are two copies of a small git runner; this feature folds them into one with a timeout.
- `ProjectData.MoveInto` already merges one folder into another file by file, across drives, keeping what's there. The bring-in step extends it to compare contents.

### The branch
`darg` is an orphan branch: its first commit is made with plumbing (`git commit-tree` on the empty tree, then `git branch darg`), not `worktree add --orphan`, because the Mac's Apple git may be older than 2.42. Its root holds a `.gitattributes` (`* text=auto eol=lf`, images binary) so a card written on Windows and one written on the Mac don't differ only by line endings.

Attaching a repo (`DargBranch.Attach` in Core):
1. Refuse if the checked-out branch tracks anything under `.darg/`, naming the branch and the `git rm -r --cached .darg` that fixes it. Darg never commits to a code branch itself.
2. Fetch the push remote (below). If it has `darg`, base the local branch on it. Otherwise make the orphan.
3. Move the plain `.darg` folder aside to `.darg.before-sync` (a rename on the same drive, so it is atomic). If something holds a file open and the rename fails, stop and say which project, and touch nothing.
4. `git worktree add .darg darg`.
5. Bring every file from `.darg.before-sync` in: one that isn't on the branch moves in; one with identical contents is dropped; one that differs goes to `SyncConflicts` (Auto-sync's first card), which keeps both sides. The folder is deleted only once it is empty.
6. Commit ("Darg: brought in this machine's .darg, n files"), then run a round.

This runs before anything opens `.darg` for that project (file watchers, the artifact server's folder). For the first Sync now, the project's views let go of their watchers and take them back afterwards.

### Where it pushes
`DargRemote` in Core decides once per round:
- **No remote:** commit only.
- **GitHub, private** (read as the project's account): `origin`, branch `darg`.
- **GitHub, public:** a private `<owner>/<repo>-darg`, made with `gh repo create --private` as the project's account the first time, and added as remote `darg`. Its URL copies origin's form, so `git@github.com-thephilipwilson:ThePhilipWilson/Darg.git` gives `git@github.com-thephilipwilson:ThePhilipWilson/Darg-darg.git` and the same SSH key signs it in. An HTTPS origin pushes with `gh auth git-credential` and the account's token for that call only.
- **Can't tell:** as the visibility Question settles.

Visibility is read again before a push if the last read is over 10 minutes old. A repo that has gone public since its `darg` branch was pushed to origin stops pushing, and the status line says to move it (to the private repo, through the same path) and delete `origin/darg`.

### A round
`DargSync.Round(project)` in Core, always off the UI thread, one at a time per project within a process (the lock across processes is Auto-sync's):
1. Check for a mass deletion (the Question) before staging anything.
2. `git -C .darg add -A`, then commit if anything changed ("Darg: n files changed". Auto-sync makes the messages better).
3. Fetch, then merge the remote `darg` with `--allow-unrelated-histories`, because two machines that attached while offline each made their own orphan.
4. On a conflict: `merge --abort`, keep the local commit, and end with "conflict in n files, not merged" until Auto-sync wires `SyncConflicts` into this step.
5. Push.
6. Return a result (pulled n files, pushed n commits, or why not) for the status line and the API. A merge that changed files reloads boards and the wall through their existing watchers.

Every git call in a round runs in `.darg` (`-C`), never in the main checkout.

### Sync now
A Sync now button sits on the right of the footer, where Auto-sync's state line will go. It is disabled while a round runs. On a project without the branch it shows the first-sync confirm inline first (the Question), and its result goes in the status line. The API's `sync-now {project}` does the same and answers the result line, plus `confirm: true` for a first sync; Auto-sync extends its answer with the state. It goes in `RemoteAccess.Commands`.

### Taking DAR-0003 off the code branches
A one-off on this repo, by a card agent: `git rm -r --cached .darg` on each branch the Question names, each in a temporary worktree of that branch (never a checkout in the main checkout), the files left on disk. The CLAUDE.md sentence saying this repo commits `.darg/tasks` goes, and so does the line in docs/architecture.md. The Main board's board.json in the working copy already has the WOR prefix, nextId 200 and the real columns back: the card checks that they are still there, and that nextId is above the highest WOR card. The pushes are to a public repo, so each waits for your OK.

### Keeping .darg safe from git in the main checkout
Tonight's loss: in the main checkout, a planner's `git pull --rebase --autostash` checked out a commit that tracks `.darg/tasks`. Git overwrites ignored files without asking, so it overwrote them, and when the rebase was aborted it deleted the 210 that `android` doesn't track. They were restored from that commit, so they carry its wording, with stand-in names instead of the originals.

Once `.darg` is a worktree with its own `.git` file, git should treat it as a nested repository and leave it alone, but nobody has checked. A test replays tonight's sequence against the new layout. If git still deletes files, the mass-delete guard puts them back from the branch on the next round, and the test proves the cards come back. Agents' briefings get two lines: `.darg` is Darg's, so never run `git add`, `commit` or `worktree remove` on it, and never pull, rebase, stash or switch branches in the main checkout. Commit there and push, and if the push is refused, stop and say so.

### Rollout on this repo
Once the cards land: Philip publishes, presses Sync now on the PC (it makes `ThePhilipWilson/Darg-darg`), then pulls the code on the Mac and presses Sync now there. The Mac's own cards (DAR-0001 to DAR-0003 were made there) come in alongside the PC's WOR cards. Where the same card or board.json differs, both sides are kept for the Sync panel.

### Order
Two cards here need a card from Auto-sync: "Put .darg on a darg branch and bring today's files in" calls Auto-sync's "Keep both sides of a sync conflict". Make that card first and add its id to this card's Blocked by. In turn, Auto-sync's round card names this plan's "One sync round for the darg branch" in its Blocked by once it has an id.

### Risks
- **A file held open during the move aside.** A live agent writing a report, or another copy of Darg, can hold a file in `.darg`. The rename fails as a whole and nothing moves, so a failure is safe, but the first sync of Darg's own project should happen with no agents running.
- **Two copies of Darg on one machine** (installed, live, debug) could both run a round. Within a process rounds are queued. Across processes the lock is Auto-sync's, so until it lands Sync now should be pressed in one copy only.
- **The nested worktree surprising tools.** `git worktree list` now shows `.darg`. A cleanup that removes every worktree it doesn't recognise would remove it. The briefing line covers agents, and the folder can be put back from the remote.
- **Size.** About 5 MB today, mostly artifacts. Agents' conversations, which arrive with a later feature, are what could make it large.
- **Line endings.** `.gitattributes` normalises them, but files that came over from Windows before it existed may show as changed once, on the first round on the Mac.

## Cards

### Take the force-added .darg off Darg's branches
DAR-0003 force-added `.darg/tasks` to Darg's own code branches, and a checkout of any branch that tracks it can delete card files, which happened tonight. On each branch the Question about branches names, commit `git rm -r --cached .darg` from a temporary worktree of that branch, never a checkout in the main checkout, leaving the files on disk. Remove the CLAUDE.md sentence saying this repo commits `.darg/tasks` and the matching line in docs/architecture.md. Check that the Main board's board.json still has prefix WOR, the real columns and a nextId above the highest WOR card. The repo is public, so commit each branch but ask Philip before every push.
**Where:** `CLAUDE.md`, `docs/architecture.md`, `.darg/tasks/Main/board.json` (checked, not committed), branches main, android and mac-path-fixes.
**Done when:** `git ls-tree -r --name-only <branch> .darg` is empty for each branch named, every card file is still on disk (the same count before and after), and the CLAUDE.md line is gone.
**Blocked by:** none

### Put .darg on a darg branch and bring today's files in
Make `DargBranch` in Core. It attaches a repo's `.darg` to an orphan `darg` branch as a linked worktree, as the Design's "The branch" describes. It refuses when the checked-out branch tracks `.darg/`. It makes the orphan with plumbing, or bases it on the remote's `darg` when there is one. It moves the plain folder aside to `.darg.before-sync`, adds the worktree, writes `.gitattributes`, then brings every file in: missing ones move, identical ones drop, and differing ones go through Auto-sync's `SyncConflicts` so both sides are kept. Fold `Repo.Git` and `LiveCopy.Git` into one git runner with a timeout for this to use.
**Where:** `src/Darg.Core/DargBranch.cs` (new), `src/Darg.Core/Repo.cs`, `src/Darg.Core/LiveCopy.cs`, `src/Darg.Core/ProjectData.cs`, `tests/Darg.Core.Tests/DargBranchTests.cs` (new)
**Done when:** unit tests on throwaway repos show: a plain `.darg` becomes a worktree on `darg` with every file still there; a branch from a remote plus a local folder ends with the union, differing files kept on both sides; a repo whose branch tracks `.darg/` is refused with the command named; a held file leaves everything untouched; and `git status` in the main checkout stays clean. `pwsh -File tools/test.ps1` passes.
**Blocked by:** none

### Pick where a repo's darg branch pushes
Make `DargRemote` in Core. It decides where a repo's `darg` branch goes: no remote means commit only; a private GitHub repo means origin; a public one means a private `<owner>/<repo>-darg`, made with `gh repo create --private` as the project's account and added as remote `darg` in origin's URL form, so SSH aliases keep working; a host whose visibility can't be read follows the visibility Question. Visibility is read again when the last read is over 10 minutes old, and a repo that has gone public stops pushing to origin and says so. gh calls use the project's account token without switching the active account.
**Where:** `src/Darg.Core/DargRemote.cs` (new), `src/Darg.Core/GitHub.cs`, `tools/perf/fake_gh.py`, `tests/Darg.Core.Tests/DargRemoteTests.cs` (new)
**Done when:** tests with fake gh show a private repo pushing to origin, a public one making `<repo>-darg` once (with an SSH alias URL and with an HTTPS URL) and pushing there, an unreadable visibility pushing nowhere, and a repo turned public refusing to push to origin. The suite passes.
**Blocked by:** none

### One sync round for the darg branch
Make `DargSync.Round` in Core, which Sync now and later Auto-sync call. Off the UI thread, and one at a time per project within the process, it commits everything changed in `.darg`, fetches, and merges the remote `darg` with `--allow-unrelated-histories`. On a conflict it aborts the merge, keeps the local commit and reports the files, until Auto-sync wires in keeping both sides. It pushes through `DargRemote` and returns a result (pulled n, pushed n, or why not). Every git call runs with `-C .darg`, never in the main checkout.
**Where:** `src/Darg.Core/DargSync.cs` (new), `tests/Darg.Core.Tests/DargSyncTests.cs` (new), with a helper that makes a bare remote and a second clone to play the other machine.
**Done when:** tests against a bare remote show a card made in one clone arriving in the other after a round in each, two orphan branches made offline merging, a conflicting card leaving both clones unchanged with the conflict reported, no remote committing only, and the main checkout's HEAD and status untouched. The suite passes.
**Blocked by:** Put .darg on a darg branch and bring today's files in, Pick where a repo's darg branch pushes

### A sync never commits a mass delete
Before a round stages anything, compare the `.darg` worktree with its last commit. Deletions Darg didn't make itself, over 10 files or a whole board folder, are handled as the mass-deletion Question settles (recommended: put back from the last commit, and the result line says how many). Deletions Darg makes itself must still commit: a forgotten agent's files, archived reports, a card deleted from the board. Darg marks those through a small `DargSync.Deleting(paths)` record that its delete paths call, so the guard can tell them apart. This is what stops a loss like tonight's 210 cards from reaching the other machine.
**Where:** `src/Darg.Core/DargSync.cs`, the delete paths in `src/Darg.Core/Board.cs`, `src/Darg.Core/Reports.cs` and `src/Darg.Core/AgentStore.cs`, `tests/Darg.Core.Tests/DargSyncTests.cs`
**Done when:** a test deletes 50 card files behind Darg's back and a round puts them back and commits nothing deleted, while a test that deletes a card through the board, and one that archives reports, commits those deletions. The suite passes.
**Blocked by:** One sync round for the darg branch

### Sync now in the footer and the API
Add a Sync now button on the right of the footer that runs a round for the project on screen and puts its result in the status line, disabled while one runs. On a project without the `darg` branch, it first shows the first-sync confirm inline (what it will make, move and push, as the Question about the first sync settles). Yes runs `DargBranch.Attach`, with the project's views letting go of their `.darg` watchers during the move. Register `sync-now {project, confirm?}` in the Darg API and in `RemoteAccess.Commands`, answering the result line.
**Where:** `src/Darg.Desktop/MainWindow.axaml`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.Api.cs`, `src/Darg.Core/Remote.cs`, `tests/Darg.Desktop.Tests/SyncNowTests.cs` (new)
**Done when:** an integration test presses Sync now on the throwaway project with a bare remote, sees the confirm, confirms, and finds `.darg` a worktree with the board still showing its cards. A second press pushes a card moved meanwhile, and `app.Api("sync-now", …)` does the same. The test fails with the button's command removed, and the suite passes.
**Blocked by:** Put .darg on a darg branch and bring today's files in, One sync round for the darg branch

### Two machines' first syncs keep every file
An integration test of the switch itself, as the PC and the Mac will do it. Two clones of one repo each have their own plain `.darg`: different cards, one card edited differently on each, a board.json that differs, reports and artifacts. The first presses Sync now and pushes. The second presses it, attaches from the remote and brings its own files in. Afterwards both clones hold the union of every file, the differing ones are kept on both sides, and a further round on each leaves the two identical. Anything it shows missing is fixed in the cards it came from.
**Where:** `tests/Darg.Desktop.Tests/TwoMachineSyncTests.cs` (new), `tests/Darg.Desktop.Tests/` (the other-machine helper)
**Done when:** the test passes and names every file it checked; deleting the bring-in step's conflict handling makes it fail.
**Blocked by:** Sync now in the footer and the API, A sync never commits a mass delete

### A checkout in the main checkout can't lose .darg
Replay tonight's loss against the new layout: in a throwaway repo whose `.darg` is a worktree on `darg`, check out (and pull with rebase and autostash, then abort) a commit that tracks `.darg/tasks`, and record what git does to the worktree's files. If git leaves them alone, the test pins that. If it doesn't, the test shows the next round putting them back. Add the two briefing lines from the Design to Darg's agent briefings: `.darg` is Darg's, never add, commit or remove it; and never pull, rebase, stash or switch branches in the main checkout, and stop if a push is refused.
**Where:** `tests/Darg.Core.Tests/DargCheckoutTests.cs` (new), `src/Darg.Core/Planner.cs`, `src/Darg.Core/CardBranch.cs`, `src/Darg.Core/FettleRules.cs`
**Done when:** the test passes and its name says which way git behaved. A planner's and a card agent's prompt (read through `app.Prompts()` in an integration test) carry both lines. The suite passes.
**Blocked by:** Put .darg on a darg branch and bring today's files in, A sync never commits a mass delete

### Move vault boards and notes into the repo's .darg
For a project whose boards or notes are set outside `.darg` (the vault), its first sync copies them into `.darg/tasks/<board>` and `.darg/notes`. It checks that every file arrived with the same bytes, repoints the config's boards and notes, and renames the vault's folder to `<folder>.moved-to-repo` rather than deleting it, so nothing is lost if the copy is wrong. A card id already used in `.darg` keeps both through `SyncConflicts`. This only runs if the Question about vault boards gives it to this feature.
**Where:** `src/Darg.Core/DargBranch.cs`, `src/Darg.Core/ProjectData.cs` (`Repoint`), `src/Darg.Core/Instance.cs`, `tests/Darg.Core.Tests/DargBranchTests.cs`
**Done when:** a test with a board and notes in a fake vault folder ends with them in `.darg`, the config pointing there, the board reading the same cards, and the vault folder renamed, not gone. The suite passes.
**Blocked by:** Put .darg on a darg branch and bring today's files in

### Docs and CLAUDE.md for .darg on its own branch
Bring the docs up to what was built: in CLAUDE.md's working-files paragraph, `.darg` is a worktree on an orphan `darg` branch that Darg commits and pushes (to origin for a private repo, to a private `<repo>-darg` for a public one), Sync now runs a round, and agents never touch it. Say the same in docs/architecture.md and docs/guide.md, with the rollout steps for a second machine. Add tonight's loss and its cause to docs/decisions-and-gotchas.md as a gotcha: checkout overwrites ignored files.
**Where:** `CLAUDE.md`, `docs/architecture.md`, `docs/guide.md`, `docs/decisions-and-gotchas.md`
**Done when:** each file describes the branch, the remote rule and Sync now, nothing in them says `.darg` is committed to code branches, and `git grep -n "DAR-0003"` finds only tests and history.
**Blocked by:** Sync now in the footer and the API, Take the force-added .darg off Darg's branches
