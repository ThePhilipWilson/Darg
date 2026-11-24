---
kind: feature
status: draft
parent: seamless-machine-switching
---
# Auto-sync

Darg pulls and pushes each project's `darg` branch by itself: on open, on switching to a project, when the window gets focus, on a timer, shortly after changes and on close. A line in the footer says where it stands, and a Sync panel shows any conflict with both machines' versions kept.

## Goals
- Sit down at the Mac and the cards moved on the PC ten minutes ago are already there, without pressing anything.
- Glance at the footer and know whether this machine's work has reached the other one, or why it hasn't.
- When both machines changed the same card or note, see both versions side by side and keep one, the other or a hand-made mix, losing neither.
- Close Darg and walk away knowing the changes went up, or being told they didn't.
- Leave Darg open on both machines and have it cost nothing.

## Not doing
- Making the `darg` branch, its worktree, its remote (a private `<repo>-darg` for a public repo) or the first-sync migration, and the Sync now button. That's the `.darg` on its own branch feature; this one builds on its sync round.
- Renumbering a clashing card id, ranks, or merging board.json's columns. That's Merge-safe boards; this feature only raises the event it hooks onto after a pull.
- The leaving check's list of what won't reach the other machine. That's the Leaving check feature, which reads the state this one keeps.
- Syncing per-user settings, agents' conversations or project.json. Their features use this scheduler, but wire themselves in.
- Ever touching main or any code branch. Auto-sync commits, pulls and pushes the `darg` branch only.
- Merging the two sides of a conflict with an agent or Haiku. He picks one or merges by hand, as the project plan settled.
- Sync on the phone. The phone keeps reading the PC it's paired with.

## Questions

### Where does the Sync panel live?
- **A Sync section, on the rail only while the project has a conflict, and always a click on the footer line away** (recommended): room to show two versions side by side, and the rail stays as it is on a quiet day.
- **Always on the rail**: easy to find, but a 16th section that is empty almost every day.
- **A flyout from the footer line**: no section at all, but too narrow to compare two versions of a card well.

**Context:** the project plan settled that conflicts are listed in a Sync panel and the footer says "1 conflict", not where the panel sits.

### Who builds the both-sides-kept merge: this feature or the `.darg` on its own branch feature?
- **This feature, as its first card, small enough to land first** (recommended): a Core piece with no UI that keeps both sides and records the conflict. The other feature's first-sync migration calls it for a file in both places, and its Sync now uses it on a pull.
- **The `.darg` on its own branch feature**: it needs it first, for the migration. This feature then only shows conflicts and resolves them.

**Context:** your line for this feature names "conflicts show with both sides kept", and the project plan's migration says a file in both places "goes through conflict handling". Whichever plan doesn't own it should say so in its Not doing.

### Closing with changes not yet pushed, and the push slow or offline: what happens?
- **Commit at once, wait up to 10 seconds for the push with "Pushing…" showing, then close anyway** (recommended): closing never hangs, and the footer says "3 changes to push" next time this machine opens.
- **Close at once and leave a helper running that keeps pushing**: the window goes straight away and the push lands as soon as the network does, but a process stays behind after Darg closes, as the ⟳ Update helper does.
- **Don't close until it's pushed, unless he presses Close anyway**: nothing is ever left behind silently, but closing on a train means a second click every time.

**Context:** committing is local and takes a moment. Only the push needs the network. The Leaving check feature will list what didn't go.

### Can a project's auto-sync be turned off?
- **On for every project with a `darg` branch, with a switch per project in Setup** (recommended): one place to stop a project syncing (a repo shared with someone else, a slow remote), and nothing to set up otherwise.
- **Follows the project's commit rules**: `push` syncs, `commit` commits only, `ask` and `never` don't sync. One set of rules, but they were written for code, and a repo set to `ask` for code would never sync its boards.
- **Always on**: nothing to set, nothing to forget, and no way to stop it short of taking the remote away.

**Context:** commit rules (`Commits` in Core) say how agents may commit code in a folder. The `darg` branch holds no code.

### Does a new conflict notify, or only show in the footer?
- **Footer and rail only** (recommended): a conflict loses nothing and blocks nothing, so it can wait until he next looks. Notifications stay for agents waiting on him, as now.
- **A desktop toast and a phone notification too**: he hears about it at once, but a conflict is not something to act on from the sofa.

**Context:** today only an agent's question, a permission ask, a card's decision and a long turn notify (`Decisions` in Core).

## Design

### What it builds on
- The `.darg` on its own branch feature makes each project's `.darg` a worktree on the `darg` branch, pushes with the project's GitHub account without switching the active one, and gives a Sync now button that commits, pulls and pushes. Auto-sync is a scheduler around that round, plus what the round needs to run unattended: a state, a lock, conflict keeping and generated messages.
- One `.darg` per project (`ProjectData.Root`, the first repo), so one sync state per project, even with several repos.
- The boards, wall, reports, mistakes and artifacts views all reload from `FileSystemWatcher`s, so a pull that changes files shows without new reload code.
- `MainWindow` already raises `WindowActive`, `OnSelectedChanged` runs on switching projects, and `Closing` calls `SaveAll`, which the ⟳ Restart and Update paths call too. These are the hooks.

### A sync round
One round for a project, always off the UI thread, one at a time per project:
1. Take the sync lock: a lock file in the `.darg` worktree's own git folder (`.git/worktrees/<name>/`), so it's never committed and the installed copy, the live copy and debug copies on one machine never run git in the same worktree at once. A lock older than 2 minutes with no process behind it is taken over.
2. Commit everything changed in `.darg`, with a message made from counts only ("Darg: 3 cards moved, 1 report, 2 notes"), never titles, so no name from the never-commit list can reach a commit message. The machine name goes in a `Machine:` trailer, which the Sync panel uses to say whose version is whose.
3. Fetch, then merge the remote `darg` (a merge, not a rebase, so conflict handling sees both sides once). A merge refused because Darg wrote a file meanwhile commits again and tries once more.
4. For each conflicted file, keep both sides (below), then finish the merge.
5. Push. A repo with no remote stops after the commit.
6. Raise `Pulled(project, changed files)` when the merge changed anything, for Merge-safe boards' id check and anything else that needs it.

The round ends in a state the footer and the panel read: synced (with when), syncing, changes waiting to push (how many commits ahead), offline (a network failure: retried on the next trigger), can't push (auth or a rejected push, with git's reason cut to one line), conflict (how many), no remote, or off.

### Keeping both sides
A file both machines changed takes the other machine's version, and this machine's goes beside it as `<name>.mine<ext>` (`WOR-0201 - Title.mine.md`, `wall.mine.json`), as the project plan settled. A file deleted on one side and edited on the other keeps the edited one, so nothing is lost there either. Each conflict is recorded in `.darg/sync/conflicts.json` (path, when, which machine wrote each side, kind). The record and the `.mine` files are committed like everything else, so the other machine shows the conflict too and either machine can settle it; the panel says "the PC's version" and "the Mac's version", not "mine".

A `.mine` file beside a card would read as a second card with the same id, so every reader of `.darg` (boards, notes wall, reports, mistakes, artifacts, flows, saved agents) skips `*.mine.*` files.

### When it runs
- **On open:** a round for every project with a `darg` branch, staggered so they don't all start at once.
- **On switching** to a project: a round for it, unless one finished in the last minute.
- **On focus** (the window activated): a round for the project on screen, unless one finished in the last minute, so alt-tabbing doesn't hammer the remote.
- **On a timer:** every 5 minutes for every project, while Darg is open, focused or not.
- **After changes:** a watcher on each `.darg` (ignoring `.mine` files and the lock) starts a round 30 seconds after the last change, so a burst of card moves is one commit.
- **Around making a card** (the project plan's answer on id clashes): a quick pull before a card is made, given at most 3 seconds and skipped when offline, and a round straight after, not 30 seconds later.
- **On close** and before Restart or Update: as the Question settles.

Before a round, anything typed but not yet saved into a `.darg` file (the open card's description, the note being written) is saved, so a pull never lands under unsaved text. Intervals take `DARG_SYNC_SECONDS` and `DARG_SYNC_QUIET_MS` so tests run them in milliseconds.

The scheduler (`AutoSync` in Desktop, over the round in Core) takes any folder and branch, so the Per-user settings sync feature can register its settings repo with it rather than write its own.

### The footer line
The footer's right side gets the project on screen's sync state: "synced 2 min ago", "syncing…", "3 changes to push", "offline · 3 changes to push", "can't push: …", "1 conflict" in the `Warn` colour. A click opens the Sync panel. The left side keeps today's `FooterLine`. A project tab with a conflict carries a small `Warn` dot, so one not on screen isn't missed.

### The Sync panel
Where it lives is a Question. What it holds:
- **Conflicts**, each named as the thing it is ("card WOR-0201 Title", "note Groceries", "report …"), not a path, with when and which machines. Opening one shows both versions side by side with the differing lines marked, and three buttons: Keep the PC's, Keep the Mac's, and Edit, which opens the file to merge by hand, with the other version beside it, and a Done that settles it. Settling deletes the `.mine` file, takes the row out of `conflicts.json` and starts a round.
- **Last syncs** on this machine: when, what came in and went out (counts), or the failure. Kept in the instance folder (machine-local), the last 50.
- **Sync now** and the project's on/off state.

### The API
`sync {project}` answers the state, the conflicts and the last syncs; `sync-now {project}` runs a round and answers its state; `sync-keep {project, path, keep: theirs|mine}` settles a conflict. Fettle can then say why a card from the Mac isn't showing, and the Leaving check reads `sync` rather than working it out again. All three go in `RemoteAccess.Commands`, so the phone can read it later.

### Order
These cards build on the `.darg` on its own branch feature's sync round, which has no card id yet, so its cards can't be named in Blocked by. Make them once that feature's cards are made, and add its Sync now card's id to the Blocked by of "A sync round that runs unattended".

### Risks
- **A pull under an agent's edit.** A card agent writes its report and moves its card in `.darg` mid-turn. A pull can change the same card file between the agent's read and write. The round commits the agent's write in the next round and a real clash becomes a conflict, not a loss.
- **Git in the background on a big branch.** Every 5 minutes per project is a fetch, which is cheap when nothing changed. If agents' conversations make the branch large, the push after a turn is what costs; it's off the UI thread and measured in `perf.log` like any other section.
- **Conflict records conflicting.** Two machines settling conflicts at once both edit `conflicts.json`. It is merged by row (path) rather than kept as a conflict of its own.
- **Lock left behind** by a crash: taken over after 2 minutes when no process holds it.

## Cards

### Keep both sides of a sync conflict
Make the Core piece that keeps both sides of a file two machines changed, so nothing is ever lost: the other machine's version goes in the file, this machine's beside it as `<name>.mine<ext>`, a delete against an edit keeps the edit, and each conflict is recorded in `.darg/sync/conflicts.json` (path, when, which machine wrote each side, kind). It also settles one: keep theirs, keep mine, or done after a hand edit, each deleting the `.mine` file and the record's row. Two machines' `conflicts.json` merge by path. No UI and no git calls of its own: it is given the two sides, so a pull and the first-sync migration can both use it.
**Where:** `src/Darg.Core/SyncConflicts.cs` (new), `tests/Darg.Core.Tests/SyncConflictsTests.cs` (new)
**Done when:** unit tests show a card, a note and board.json each kept with both sides, a delete against an edit keeping the edit, keep theirs and keep mine settling and leaving one file, and two `conflicts.json` files merging by path; `pwsh -File tools/test.ps1` passes.
**Blocked by:** none

### Darg's readers skip .mine files
A `.mine` copy left beside a file by a sync conflict must never show as a second thing: a `WOR-0201 - Title.mine.md` in a board's items would read as a second card with the same id, and one in notes as a second note. Every reader of `.darg` leaves out `*.mine.*` files: boards (`BoardStore`, `CardIndex`), the notes wall (`NoteStore`), reports, mistakes, artifacts, flows and saved agents. Next ids ignore them too.
**Where:** `src/Darg.Core/Board.cs`, `src/Darg.Core/CardIndex.cs`, `src/Darg.Core/Notes.cs`, `src/Darg.Core/Reports.cs`, `src/Darg.Core/Mistakes.cs`, `src/Darg.Core/Artifacts.cs`, `src/Darg.Core/Flow.cs`, `src/Darg.Core/AgentStore.cs`
**Done when:** a unit test puts a `.mine` copy beside a card, a note, a report and a flow and each store lists only the original; an integration test shows the board with one tile for that card; the suite passes.
**Blocked by:** none

### A sync round that runs unattended
Make the `.darg` branch feature's Sync now round safe to run with nobody watching: take a lock in the worktree's git folder so two Darg copies on one machine never run git in the same `.darg` at once (taken over after 2 minutes with no live holder); commit with a message made from counts only and a `Machine:` trailer; fetch and merge, retrying once when Darg wrote a file meanwhile; keep both sides of every conflicted file through the conflict keeper; push; raise `Pulled` with the changed files; and end in a state (synced, syncing, to push, offline, can't push with git's reason, conflict count, no remote, off). Add a test helper with a bare remote and a second clone that plays the other machine, for this card and the ones after it.
**Where:** `src/Darg.Core/` (the sync round the `.darg` branch feature adds; `SyncState` new), `tests/Darg.Core.Tests/`, `tests/Darg.Desktop.Tests/` (the other-machine helper)
**Done when:** tests against a bare remote show a commit message with counts and no titles, a second Darg process waiting on the lock, an offline remote giving the offline state with the changes kept, a conflicting card ending with both sides kept and the conflict state, and `Pulled` naming the files the merge changed; the suite passes.
**Blocked by:** Keep both sides of a sync conflict

### Sync on open, on switching, on focus, on a timer and after changes
Run the round without being asked: `AutoSync` in Desktop starts one for every project with a `darg` branch at open (staggered), for a project switched to, for the project on screen when the window gets focus (neither within a minute of the last), every 5 minutes for all, and 30 seconds after the last change a watcher sees in `.darg` (ignoring `.mine` files and the lock). One round at a time per project; a trigger during a round queues one more. Unsaved text in the open card or note is saved first. `DARG_SYNC_SECONDS` and `DARG_SYNC_QUIET_MS` shorten the timings for tests. A project switched off in Setup never syncs, as the Question settles. The scheduler takes any folder and branch so the per-user settings repo can join it later.
**Where:** `src/Darg.Desktop/ViewModels/AutoSync.cs` (new), `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Desktop/MainWindow.axaml.cs`, `src/Darg.Desktop/ViewModels/SetupViewModel.cs`, `tests/Darg.Desktop.Tests/`
**Done when:** integration tests with the other-machine helper show a card moved on the other clone appearing on the board after a focus, after a switch and after the timer, a burst of three card moves pushed as one commit after the quiet time, and a project switched off pushing nothing; each test fails with its trigger taken out; the suite passes.
**Blocked by:** A sync round that runs unattended

### Sync around making a card, and on close
Two triggers with deadlines. Before a card is made (a note's ticket, + Card, the API's `new-card`, Make cards on a plan, an issue to a card), pull first, given at most 3 seconds and skipped when offline, then run a round straight after rather than waiting the quiet time, as the project plan's answer on id clashes asks. On close, and before ⟳ Restart and Update, commit at once and push as the close Question settles, with "Pushing…" showing while it waits. Everything that makes a card goes through one helper so a new path can't miss it.
**Where:** `src/Darg.Desktop/ViewModels/AutoSync.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.Cards.cs`, `src/Darg.Desktop/ViewModels/NotesViewModel.cs`, `src/Darg.Desktop/ViewModels/DesignViewModel.cs`, `src/Darg.Desktop/ViewModels/RestartViewModel.cs`, `src/Darg.Desktop/MainWindow.axaml.cs`
**Done when:** integration tests show a card made with the remote ahead pulling first and the new card on the remote right after, a card made offline made within 3 seconds, and closing with a change pushing it before the window goes (or leaving it counted to push, per the answer); the suite passes.
**Blocked by:** Sync on open, on switching, on focus, on a timer and after changes

### Sync line in the footer
Show the project on screen's sync state on the right of the footer: "synced 2 min ago" (ticking), "syncing…", "3 changes to push", "offline · 3 changes to push", "can't push: <reason>", and "1 conflict" in the `Warn` colour, with today's `FooterLine` kept on the left. A click opens the Sync panel. A project tab whose sync has a conflict gets a small `Warn` dot. A new conflict notifies too only if the notify Question says so. Colours come from theme keys, never hex.
**Where:** `src/Darg.Desktop/MainWindow.axaml`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Desktop/ViewModels/SyncLineViewModel.cs` (new), `tests/Darg.Desktop.Tests/`
**Done when:** integration tests with screenshots in `.shots/integration` show each state's text after the matching round (synced, to push, offline, conflict), the tab dot on a project not on screen, and a click opening the Sync panel; the suite passes.
**Blocked by:** Sync on open, on switching, on focus, on a timer and after changes

### The Sync panel: conflicts side by side, and the last syncs
Build the panel where the Question puts it. Conflicts are listed as the things they are (card id and title, note name, report), with when and which machines; opening one shows both versions side by side with the differing lines marked and Keep the PC's, Keep the Mac's (named from each side's machine), and Edit, which opens the file to merge by hand with the other version beside it and a Done that settles it. Settling starts a round. Below: the last 50 syncs on this machine (when, counts in and out, or the failure), kept in the instance folder, and Sync now with the project's on/off state.
**Where:** `src/Darg.Desktop/Views/SyncView.axaml` (new), `src/Darg.Desktop/ViewModels/SyncViewModel.cs` (new), `src/Darg.Desktop/ViewModels/MainViewModel.cs` (`Section`), `src/Darg.Core/SyncConflicts.cs`, `tests/Darg.Desktop.Tests/`
**Done when:** an integration test makes a card conflict with the other-machine helper, the panel lists it by id and title with both versions showing, Keep the Mac's leaves one card file with that text and no `.mine` file on both clones after the next round, and the footer goes back to synced; a screenshot of the panel is in `.shots/integration`; the suite passes.
**Blocked by:** Sync line in the footer, Darg's readers skip .mine files

### Sync in the Darg API
Add `sync {project}` (state, conflicts, last syncs), `sync-now {project}` (runs a round, answers its state) and `sync-keep {project, path, keep: theirs|mine}` (settles a conflict) to the Darg API with usage lines saying what each takes, and put all three in `RemoteAccess.Commands`. Fettle can then explain why something from the other machine isn't showing, and the Leaving check feature reads `sync` instead of working the state out again.
**Where:** `src/Darg.Desktop/ViewModels/MainViewModel.Api.cs`, `src/Darg.Core/Remote.cs`, `tests/Darg.Desktop.Tests/`
**Done when:** integration tests call each command through `app.Api`: `sync` lists a conflict made with the other-machine helper, `sync-keep` settles it, `sync-now` pushes a change; a bad path or project answers a 400 with a message; the suite passes.
**Blocked by:** Sync on open, on switching, on focus, on a timer and after changes
