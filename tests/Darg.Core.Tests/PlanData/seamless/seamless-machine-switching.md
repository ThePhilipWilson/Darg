---
kind: project
status: agreed
---
# Seamless machine switching

Close Darg on the PC, open it on the Mac (or the other way round) and the project is there as you left it: boards, notes, reports, plans, agents to carry on, settings. Git carries it, on a `darg` branch of each repo that Darg commits, pulls and pushes itself.

## Goals
- Walk from the PC to the Mac and carry on with a project without copying anything, setting it up again or wondering what didn't come across.
- Make cards, notes and decisions on either machine, even both in one day, and never lose one or see two cards with the same id.
- Pick up an agent's conversation on the other machine where it stopped, or know at once why it can't be.
- See the same theme, model profiles and flows wherever he sits down.
- Keep the code history as it is: no state commits on main, and nothing in agents' fast-forward merges to clash with.

## Not doing
- Running one agent on two machines at once, or moving an agent mid-turn. An agent belongs to the machine running it until its turn ends.
- Syncing what is machine-local on purpose (listed in Design): phone pairing, shells, local tools, emulators, worktrees, consoles, logs.
- Syncing the phone app's own data. The phone keeps talking to whichever PC it paired with.
- Syncing Claude Code's own settings, hooks and skills (`~/.claude`). They are not Darg's to sync.
- Syncing uncommitted code. The leaving check says it's there, but git is how code moves.
- A server or cloud service of Darg's own. Git remotes only.
- Linux, and more than two machines as a design target (nothing should stop a third, but nothing is tested for it).

## Questions

### The Darg repo is public now: where does a public repo's `darg` branch push?
- **To a private repo of its own, made by Darg** (recommended): a public repo's state goes to a private `<repo>-darg` repo under the same account, so boards, notes, reports, mistakes and transcripts stay private. A private repo's `darg` branch goes to its own remote as decided.
- **To the same public repo**: one remote for everything, but every card, note, report and agent transcript is public, and the names-never-to-commit list has to hold for all of it, forever.
- **Not synced for public repos**: no leak, but Darg's own project, the one he switches on most, doesn't travel.

**Context:** WOR-0197 made `ThePhilipWilson/Darg` public on 2026-11-24, after the `darg` branch was decided. The 2026-11-24 name leak came from a branch push.

**Answer:** To a private repo of its own, made by Darg (Philip, 2026-11-24)

### Two machines make a card while apart and both hand out WOR-0200. What happens?
- **Sync right before and after making a card, and renumber a clash found after a pull** (recommended): the card not yet pushed gets the next free id, and its references (Blocked by, card links, reports, plan rows) are rewritten. The status line names both ids.
- **A machine letter in new ids** (WOR-0200 on the PC, WOR-M0200 on the Mac): never clashes, but ids look different depending on where they were made, and every id parser changes.
- **Sync around making a card only, and never renumber**: simplest, but a card made offline on both machines leaves two cards with one id until someone fixes it by hand.

**Context:** next id is already worked out as the highest card file plus one (`BoardStore.HighestUsed`), so only cards made on both sides between syncs can clash.

**Answer:** Sync right before and after making a card, and renumber a clash found after a pull (Philip, 2026-11-24)

### Where does a card's place in its column live, so that two machines' reorders merge?
- **A rank line in each card file** (recommended): moving a card edits only that card, so two machines moving different cards never conflict. Ranks are fractional, so one move never renumbers the rest.
- **Darg merges board.json's order itself on a pull**: the file format stays as it is, but every reorder still rewrites a shared file, and the merge code is Darg's to get right.
- **No saved order**: cards sort by number in each column. Nothing to merge, and nothing to drag either.

**Context:** your line said order should come from the card files. A card's column already comes from its Status line, and only the order and next id are in board.json now.

**Answer:** A rank line in each card file (Philip, 2026-11-24)

### When both machines changed the same thing, how is the conflict shown?
- **Both sides kept and listed in a Sync panel** (recommended): the file takes the other machine's version, and this machine's version sits beside it as `<name>.mine.md`. The footer says "1 conflict" until he picks one or merges by hand. Nothing is lost and nothing blocks syncing.
- **Newest edit wins, the other kept in git history only**: no prompts, but an edit can disappear from sight without anyone noticing.
- **Syncing stops until it's resolved**: safest for the file, but one conflict holds every other change on that machine back.

**Context:** with ranks in card files, real conflicts come down to the same card, note or report edited on both machines between syncs.

**Answer:** Both sides kept and listed in a Sync panel (Philip, 2026-11-24)

### How much of an agent travels to the other machine?
- **Its record and its Claude conversation, for every saved agent** (recommended): Carry on works on either machine. Only saved agents' sessions are copied, and finished card agents are already forgotten (WOR-0183), so it stays small.
- **Its record only**: the transcript reads the same on both machines, but carrying on there starts a fresh session with the transcript as context.
- **Only when handed over**: a Hand to other machine button copies one agent's conversation. Nothing moves until he asks.

**Context:** Darg's main checkout alone has 800 MB of Claude conversations in `~/.claude/projects`, most of them for agents Darg no longer lists. Resuming from a copied conversation is unproven until the spike runs.

**Answer:** Its record and its Claude conversation, for every saved agent (Philip, 2026-11-24)

### What happens to an agent's unmerged work in its worktree when he switches?
- **Darg pushes the card branch when the agent's turn ends, and Carry on elsewhere checks it out into a worktree there** (recommended): the conversation and the work arrive together.
- **Only the conversation travels**: the agent carries on, but without its uncommitted or unpushed work, which stays on the first machine.
- **Agents with unmerged work stay on their machine**: shown on the other machine as "on the PC, work not pushed", with no Carry on.

**Context:** card agents work on a branch per card in their own worktree and fast-forward into main only when done (`CardBranch`).

**Answer:** Darg pushes the card branch when the agent's turn ends, and Carry on elsewhere checks it out into a worktree there (Philip, 2026-11-24)

### How do per-user settings (theme, profiles, shared flows, push topic) get to the other machine?
- **A private settings repo that Darg syncs the same way as `darg` branches** (recommended): one code path, history included, works anywhere git does.
- **A cloud folder (OneDrive or iCloud)**: no repo, but each OS has its own client, and a file both machines write at once has no merge.
- **Export and import a file by hand**: nothing runs in the background, but every change means doing it again.

**Context:** these live in the instance folder today (`settings.json`, `profiles.md`, `flows/`, instance.json's `push`) and never leave the machine.

**Answer:** A private settings repo that Darg syncs the same way as `darg` branches (Philip, 2026-11-24)

### Projects whose boards are in the vault (Alpha, Beta, Gamma, Site): do their boards move into the repo?
- **They stay in the vault, synced however the vault is** (recommended): the vault path goes in each machine's instance.json, and the third-party board app keeps reading them.
- **They move into their repos' `.darg`**: one way for every project, but the vault and the third-party board app lose them.

**Context:** only projects that Setup made keep their boards in `.darg/tasks`. Those four keep theirs in the Obsidian vault.

**Answer:** They move into their repos' `.darg` (Philip, 2026-11-24)

### What if Darg is open on both machines at once?
- **Both sync: pull every few minutes and when the window gets focus, push shortly after a change** (recommended): leaving one open costs nothing, and renumbering and the Sync panel catch what overlaps.
- **One machine at a time**: opening a project on one machine takes a lock in the `darg` branch, and the other shows it read-only until he takes over. No overlap, but a machine left on at home locks him out.

**Context:** switching usually means one machine at a time, but nothing stops both being on.

**Answer:** Both sync: pull every few minutes and when the window gets focus, push shortly after a change (Philip, 2026-11-24)

## Design

### What it builds on
- `ProjectData.Root` already puts every working file in `<first repo>/.darg`, and Reports, Mistakes, Artifacts and Flows go through it, so moving `.darg` onto a branch changes one folder's nature, not every caller.
- `.darg/` is already in `.git/info/exclude`, so a nested worktree there is invisible to the main checkout's status and to agents' commits.
- A card's column comes from its Status line, and next id already takes the highest card file into account (`BoardStore.HighestUsed`).
- WOR-0199 commits every Design edit and keeps the Design page's state (unticked rows, the planner chat) in the plan file. Plans are on main in `docs/design`, so they travel with the code.
- The Mac fixes (`docs/MAC-NOTES.md`): `FileNames.Safe` already makes `.darg` file names that open on both OSes.

### The `darg` branch
In each repo, `.darg` becomes a linked worktree on an orphan branch `darg` (no code history). Darg makes it, commits to it, pulls it and pushes it. Agents are told never to commit there, and their worktrees don't have it, as now. The first sync on a machine that already has a `.darg` folder (both the PC and the Mac today):
- moves the folder aside, adds the worktree (from the remote's `darg` if there is one, else a new orphan), then merges the old files in. A file in both places goes through conflict handling and is never overwritten.
- takes DAR-0003's force-added `.darg/tasks` off main (`git rm --cached`, the files stay) and removes the CLAUDE.md lines that say it is committed.
- restores the Main board's WOR prefix, next id and columns where the cherry-pick replaced them.

Darg runs every git call off the UI thread, and a repo with no remote just commits. Pushing signs in with the project's GitHub account as other GitHub calls do (`gh auth token --user`), and never switches the active account.

### Project settings in the repo
`.darg/project.json` carries what is about the project: id, name, colour, saved commands (Cwd relative to a repo), commit rules (keyed by repo name, not path), preview, GitHub account and features. instance.json keeps only what is about this machine: which projects are open here and where each repo, the vault and the docs are. Adding a folder in Setup reads project.json and keeps its id, so Darg stays `workbench`, and its agents, chats, artifact URLs and push keys still match. A project with several repos keeps project.json in its first repo and names the others by folder name and remote URL. On a machine where a repo is missing, Setup offers to clone it.

### Merge-safe boards
- The next id is the highest number of that prefix across the project's boards, plus one. board.json's `nextId` stays only as a floor, so the third-party board app's format still reads.
- A card's order is a rank line in its card file (see Questions). board.json keeps columns, prefix and minimised lists, which rarely change. Darg merges two machines' columns by id when they do.
- After every pull, Darg checks the boards' ids against the disk, and a clash is handled as the Questions settle.

### Auto-sync
Darg pulls on open, on switching to the project and on a timer while open. It commits shortly after changes stop (one commit for a burst of card moves) and pushes, and it commits and pushes on close. A footer line per project says "synced 2 min ago", "3 changes to push", "offline" or "1 conflict", and opens the Sync panel, which lists conflicts and the last syncs. A pull that changes files reloads the boards, wall and reports through their existing file watchers. Commit messages are generated ("Darg: 3 cards moved, 1 report") and carry no names from the never-commit list.

### Agents and Fettle chats
- Saved agents move from `<instance>/agents/<project id>/` to `.darg/agents/`, with paths stored relative to the repo and turned back into this machine's on load. The same goes for Fettle chats started in a project. Chats with no project go with the per-user settings.
- The transcript preview images an agent uses (89 MB in `<instance>/previews` today, mostly for agents already gone) go with the agent that shows them.
- An agent's Claude conversation (`~/.claude/projects/<cwd key>/<session>.jsonl`, keyed by the folder it ran in) is copied into `.darg` when its turn ends, and copied into this machine's key for its cwd before `--resume`.
- A record says which machine is running it. The other machine shows it as "running on the PC" and offers Carry on here only once its turn has ended.
- A resume that fails starts a fresh session with the transcript as context and says so. That's the artifact investigation's "resume" card, wanted with or without syncing.
- Once agents travel, artifacts (folders named by agent id) and reports (which carry agent ids) point at agents that exist on both machines. A `darg.send()` from a page made on the other machine reaches the agent's record, and Carry on starts it here.
- **The spike comes first:** copy one finished agent's conversation from the PC to the Mac under the Mac's path key and `--resume` it. Note what Claude Code needs (the file only, or its folder of subagents and tool results too) and what the agent makes of Windows paths in its history. The agents feature waits on that answer.

### Per-user settings
These sync through what Questions settle: theme and Optimise tokens (`settings.json`), `profiles.md`, shared flows, the push topic, the project list with each repo's remote (so a new machine offers to clone them), Fettle chats with no project, and the project and section open last, so the Mac opens where the PC left off. Instance data is in `%APPDATA%\Darg\<instance>` on Windows and `~/Library/Application Support/Darg/<instance>` on the Mac.

### Stays per machine, and why
- **Where each repo, the vault and the docs are** (instance.json): paths differ by OS and drive.
- **Phone pairing** (`remote-devices.json`, `remote-cert.pfx`): the phone pins this machine's certificate, so each machine pairs on its own.
- **Push state** (`push.json`): which decision keys this machine sent. Only the machine that sent a decision answers its buttons, so a reply is never applied twice.
- **The artifact token** (`artifact-token`): it guards this machine's local server. A page sends to the server it was opened from.
- **Shell choice and local tools**: PowerShell and Git Bash on the PC, zsh on the Mac. Tool paths differ.
- **Emulators, the Android SDK, the dictation model**: big, OS-specific, downloaded or set up where they're used.
- **Worktrees, consoles, dev servers and agents mid-turn**: processes and folders on this disk. Their work moves through git and the agent records once they stop.
- **Usage, work timer, crash, perf and stall logs**: about this machine's runs. Usage refreshes from the next turn anyway.

### Risks
- **Resume might not work across paths.** The spike settles it before the agents feature is planned in detail, and the fallback (fresh session with the transcript) is useful either way.
- **Public repos.** Anything pushed to a public remote is published. The first Question decides where a public repo's state goes, and the never-commit check runs on every `darg` push as it does before code pushes.
- **Size.** `.darg` is about 6 MB today. Conversations and preview images could make it hundreds. Only saved agents' files go, and forgetting an agent removes its files from the branch (history keeps them, so a branch reset can trim it if it grows).
- **Renumbering rewrites references.** It is limited to cards never pushed, so no other machine has seen the old id.
- **Two Darg processes, one repo.** The live copy, debug copies and the installed copy share a `.darg` on one machine. Sync takes a lock file in `.darg` so only one of them commits at a time.

## Features
- **[.darg on its own branch](darg-on-its-own-branch.md)**: each repo's `.darg` becomes a worktree on a `darg` branch. Today's folders on the PC and the Mac merge into it without losing a file, DAR-0003's force-add comes off main, and a Sync now button pulls and pushes.
- **[Merge-safe boards](merge-safe-boards.md)**: next id comes from the card files, order is a rank in each card, and an id clash found after a pull is settled without a duplicate card.
- **[Project settings in the repo](project-settings-in-the-repo.md)**: `.darg/project.json` carries the id, commands, commit rules, preview, GitHub account and features. instance.json keeps only where things are on this machine, and adding a folder keeps its id.
- **[Auto-sync](auto-sync.md)**: pull on open, on switching and on a timer, commit and push after changes and on close, a sync line in the footer, and a Sync panel where conflicts show with both sides kept.
- **[Resume spike](resume-spike.md)**: proves (or disproves) that an agent carries on from its conversation copied to the other machine under another path, and writes down what has to be copied.
- **[Agents and Fettle chats travel](agents-and-fettle-chats-travel.md)**: saved agents, project Fettle chats and their preview images in `.darg` with repo-relative paths, their conversations carried across, "running on the PC" shown on the other machine, and a fresh-with-transcript fallback when resume fails.
- **[Per-user settings sync](per-user-settings-sync.md)**: theme, Optimise tokens, profiles, shared flows, push topic, the project list with remotes (offering to clone what's missing), chats with no project, and the project open last.
- **[Leaving check](leaving-check.md)**: on close, or from a button, says what won't reach the other machine (uncommitted or unpushed code, a live agent, `.darg` changes not pushed) and pushes what it can.
