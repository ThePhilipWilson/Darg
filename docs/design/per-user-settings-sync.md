---
kind: feature
status: draft
parent: seamless-machine-switching
---
# Per-user settings sync

What is about Philip rather than a project or a machine (theme, Optimise tokens, model profiles, shared flows, the ntfy topic, which projects he has and where their repos live online, Fettle chats with no project, and where he was last) lives in one folder that a private settings repo carries between the PC and the Mac. A project missing on this machine is offered as a clone.

## Goals
- Sit down at the Mac and Darg looks and behaves as it did on the PC: same theme, same model picks, same flows, Optimise tokens as he left it.
- A new or second machine knows every project he has and clones the missing ones on a click, instead of setting each one up again by hand.
- Darg opens on the project and section he was in on the other machine.
- Pick up a general Fettle chat (one with no project) on either machine.
- Phone notifications from either machine land in the same ntfy topic, so the phone needs setting up once.

## Not doing
- The sync engine itself (git pull, commit, push, the lock, conflicts kept as `.mine` files, the Sync panel, the footer line): that is the `.darg` on its own branch and Auto-sync features'. This feature uses it on one more folder.
- Project settings (`.darg/project.json`: id, colour, commands, commit rules, preview, features) and saved agents or project Fettle chats: the Project settings in the repo and Agents and Fettle chats travel features.
- Anything the parent plan keeps per machine: repo, vault and docs paths, phone pairing and certificate, `push.json` (sent decision keys), the artifact token, shell choice, local tools, usage, the work timer, logs.
- Claude Code's own settings, hooks and skills in `~/.claude`.
- Following the other machine live: the last project and section apply when Darg starts, never by moving a window that's in use.
- Syncing between instances (home and work stay apart, as now).

## Questions

### Which repo holds the settings?
- **A private `darg-settings-<instance>` repo under the account picked in Settings** (recommended): `darg-settings-home` for the home instance. Darg creates it with `gh repo create --private` if it isn't there, else clones it. A work instance syncs only if it's pointed at a repo of its own.
- **One private `darg-settings` repo with a folder per instance**: one repo for everything, but the work instance's settings sit beside home's under one account.
- **A `user/` folder in the Darg repo's private state repo** (`Darg-darg`, from the parent's first answer): no extra repo, but settings then depend on the Darg project being set up on that machine.

**Context:** the parent plan settled a private settings repo synced like `darg` branches, but not its name, owner or what instances share.

### Where does Clone put a repo that isn't on this machine?
- **Beside the project's other repos if any are here, else in a clone folder set once per machine** (recommended): the first clone asks for that folder, defaulting to the parent of an existing repo of the same owner (`D:\GitHub\ThePhilipWilson` on the PC, `~/GitHub/ThePhilipWilson` on the Mac).
- **Ask with a folder picker every time**: nothing guessed, one more step per clone.
- **One clone root in Settings, always `<root>/<owner>/<repo>`**: predictable, but ignores where repos already live on this machine.

**Context:** paths differ by OS and drive, so only the remote URL travels; this machine has to decide the folder.

### What does removing a project in Setup do on the other machine?
- **Removes it here only, with Remove everywhere beside it** (recommended): it stays in the synced list and Setup shows it under "On your other machine" to clone or add again. Remove everywhere takes it off the list for both.
- **Always removes it everywhere**: one list, no hidden projects, but taking a game project off the Mac takes it off the PC too.

**Context:** some projects only make sense on one machine (Unity work on the PC), so "not here" and "gone" may need to differ.

### Does the ntfy access token go in the settings repo?
- **Yes, with the server and topic** (recommended): the repo is private, the topic name is already the secret, and the phone keeps working whichever machine sends.
- **Server and topic sync, the token stays per machine**: nothing secret in git, but the second machine needs the token typed in once.

**Context:** only a protected ntfy topic has a token; the default ntfy.sh topic has none, and the real instance has no `push` set yet.

### The settings repo needs the git sync the `.darg` on its own branch and Auto-sync features build. How do these cards wait for it?
- **Build what needs no git now, and hold the two git cards** (recommended): the first seven cards below start as their own Blocked by allows; "Connect the settings repo" and "Keep the settings repo synced" stay unticked until those features' cards exist, then their ids go into Blocked by.
- **This feature builds the git sync first**, as a Core `GitSync` the `.darg` feature then reuses: settings travel sooner, but the engine is designed around one folder of small files rather than `.darg`'s worktree.

**Context:** a plan's Blocked by can only name cards in this plan or on a board, and the sibling features have no cards yet.

## Design

### What it builds on
- Every setting here is already a file in the instance folder: `settings.json` (`AppSettings`: theme and Optimise tokens), `profiles.md` (`ModelProfiles.FilePath`), `flows/` (`FlowStore.Folder`), instance.json's `push` (`PushConfig`) and `projects`, `last-project` (`LastProject`), and Fettle chats in `agents/~bench/` (`MascotViewModel.Store`), where a chat whose `Project` is null shows in every project.
- Each of those is read through one path method, so moving them is a change in a handful of places plus a one-time move.
- The parent's answers: a private settings repo synced the same way as `darg` branches, conflicts kept with both sides and listed in the Sync panel, both machines syncing when both are open.

### The synced folder
`<instance>/synced/` holds everything that travels, and nothing else. It is a clone of the settings repo once connected, and a plain folder until then, so every card but the git ones works and is testable without a repo.

| File | What | From |
|---|---|---|
| `settings.json` | theme, Optimise tokens | instance folder |
| `profiles.md` | model profiles and rules | instance folder |
| `flows/*.md` | shared flows | instance folder |
| `ntfy.json` | server, topic, token (per Questions) | instance.json `push` |
| `projects.json` | each project's id, name, tab order, and each repo's folder name, remote URL and GitHub account | instance.json `projects` |
| `last-open.json` | project id, section, machine, time | `last-project`, plus the section (new) |
| `chats/<id>.json` | Fettle chats with no project, cwd stored relative to home | `agents/~bench/` |
| `previews/` | preview images those chats show | `previews/` |

`InstanceConfig`, `AppSettings`, `ModelProfiles`, `FlowStore`, `LastProject` and the Fettle chat store get their paths from one `SyncedFolder` in Core. A one-time move (`SyncedFolder.Migrate`, run beside `ProjectData.Migrate`) moves what's there and leaves nothing behind; a file already in `synced/` is never overwritten (the old one goes beside it as `.mine`). One file per kind of thing, so two machines changing different settings never touch one file. Any future app setting that is about the machine goes in a new file outside `synced/`, never into `settings.json`.

### The project list and cloning
`projects.json` is written from instance.json whenever Setup saves or Darg starts: per project, its id and name and per repo its folder name, `git remote get-url origin` and the gh account. It never holds a path. Projects with no repo are listed with no remotes, so the other machine shows them but can't clone them.

Setup reads it beside instance.json: a project in the list with none of its repos here shows under "On your other machine" with Clone. Clone runs `gh repo clone` with the project's account's token in `GH_TOKEN` for that call only (as the GitHub section does), into the folder the Questions settle, then adds the project to instance.json with the cloned path. The Project settings in the repo feature's `.darg/project.json` then fills in the rest when it lands; until then the clone gets the name and colour from the list. A project that is here with a repo missing (Site with no Workspace) gets a Clone for that repo alone.

### Last project and section
`last-open.json` is written when the project or section changes, at most once every 30 seconds and on close, so tab flicking doesn't make commits. On start Darg pulls the settings first (once connected), then opens the newer of this machine's `last-project` and the synced one. The section is new: today only the project is kept. A section the project doesn't have here opens Home.

### Fettle chats with no project
Chats whose `Project` is null are saved to `synced/chats/`; the rest stay where they are (the Agents and Fettle chats travel feature moves those). A chat that gains a project (`MascotViewModel.Guess`) moves out to the project's store. Paths in the record (cwd, preview images) are stored relative to the home folder or `synced/` and turned back on load, so a PC chat opens on the Mac. Carrying on in a chat's Claude conversation needs the resume spike's answer; until then a chat from the other machine reads the same and carries on as a fresh session with the transcript.

### Changes arriving while Darg is open
A watcher on `synced/` applies a changed file without a restart: the theme through `ThemeManager`, Optimise tokens through `TokenSaver.Load`, profiles on the next agent start (already read each time), flows by re-reading the Flows list, ntfy by restarting `PushClient`, the project list by refreshing Setup's "On your other machine". `last-open.json` is not applied live (Not doing).

### Connecting and syncing
Settings gets a Sync part: the account (from `gh auth status`), the repo name (per Questions), Connect. Connect clones the repo into a temporary folder, or creates it private and pushes, then merges this machine's `synced/` files in. A file on both sides goes through the Sync panel's conflict handling. From then on the folder syncs with the Auto-sync engine: pull on open, on focus and on its timer; commit and push a few seconds after a change and on close. Commit messages are generated ("Darg settings: theme, 2 flows") and name nothing from the never-commit list. With no connection, everything above still works on one machine.

### Risks
- **Both machines open, both notifying.** Both send to the same topic; a card's notification has one sequence id per project and card (`PushStore.SequenceId`), so the second replaces the first rather than doubling it, and a tap answers through whichever machine sent the showing one. Two machines both moving a card into attention after a pull is Auto-sync's to stop.
- **profiles.md edited on both machines.** Text merges in git; a real clash lands as `profiles.mine.md` in the Sync panel, and the profile picker keeps reading `profiles.md` meanwhile.
- **The ntfy token in git** (if Questions say so): the repo is private and Darg creates it private; Connect refuses a public repo.
- **A repo cloned under the wrong account** answers 404 when private. Clone uses the account in the list, never the active one, and says which account it tried.

## Cards

### Move the settings files into a synced folder
Theme, Optimise tokens, model profiles and shared flows are files scattered in the instance folder. Put them in `<instance>/synced/` through one `SyncedFolder` class in Core, change `AppSettings.FilePath`, `ModelProfiles.FilePath` and `FlowStore.Folder` to use it, and add `SyncedFolder.Migrate`, run first thing in `MainViewModel`'s constructor beside `ProjectData.Migrate`, which moves `settings.json`, `profiles.md` and `flows/` in and never overwrites a file already there (the old one goes beside it as `.mine`). The footer line says what moved, as `ProjectData.Migrate`'s does, and CLAUDE.md's lines naming these files say where they are now.
**Where:** `src/Darg.Core/SyncedFolder.cs` (new), `src/Darg.Core/Themes.cs`, `src/Darg.Core/ModelProfiles.cs`, `src/Darg.Core/Flow.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `tests/Darg.Core.Tests`, `CLAUDE.md`
**Done when:** unit tests show an old instance folder migrated (files moved, an existing file in `synced/` kept with the other as `.mine`, a second run doing nothing), and an integration test shows the theme, Optimise tokens, ✎ Profiles and the Flows list all reading from `synced/`.
**Blocked by:** none

### Phone notification settings move to synced/ntfy.json
The ntfy server, topic and token (`PushConfig`) live in instance.json's `push`, which stays per machine. Move them to `synced/ntfy.json`, read and written by Settings' notifications part, the API's `push` and `push-test`, and `MainViewModel.Push`; the migration moves an existing `push` out of instance.json. `push.json` (sent decision keys) stays in the instance folder, since only the machine that sent a decision answers its buttons.
**Where:** `src/Darg.Core/Push.cs`, `src/Darg.Core/SyncedFolder.cs`, `src/Darg.Core/Instance.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.Push.cs`, `src/Darg.Desktop/ViewModels/SettingsViewModel.cs`
**Done when:** a test migrates an instance.json with `push` set into `synced/ntfy.json` and instance.json no longer has it; the existing push tests (`FakeNtfy`) pass reading the new file; `push.json` is still written in the instance folder.
**Blocked by:** Move the settings files into a synced folder

### A synced project list with each repo's remote
A second machine has no idea which projects exist. Write `synced/projects.json` from instance.json whenever Setup saves and when Darg starts: each project's id, name and tab order, and for each repo its folder name, its `origin` remote URL and the project's gh account, with no paths. Read it back into a `SyncedProjects` model that says, for this machine, which projects are missing entirely and which have a repo missing. Removing a project follows the answer to the removal Question (here only, or everywhere).
**Where:** `src/Darg.Core/SyncedProjects.cs` (new), `src/Darg.Core/Instance.cs`, `src/Darg.Core/Repo.cs`, `src/Darg.Desktop/ViewModels/SetupViewModel.cs`, `tests/Darg.Core.Tests`
**Done when:** unit tests on two throwaway instances show A's projects written with remotes and no paths, B told which are missing and which have a missing repo, and a removal on A behaving as the Question settles on B.
**Blocked by:** Move the settings files into a synced folder

### Setup offers to clone projects from the other machine
Setup shows the projects `SyncedProjects` says are missing under "On your other machine", each with Clone, and a project with one repo missing gets a Clone for that repo. Clone runs `gh repo clone` with the project's account's token in `GH_TOKEN` for that call only (never switching gh's active account), into the folder the clone-location Question settles, off the UI thread with progress in the row, then adds the project to instance.json with the new path and opens it. A failure says why and which account it tried.
**Where:** `src/Darg.Desktop/ViewModels/SetupViewModel.cs`, `src/Darg.Desktop/Views/SetupView.axaml`, `src/Darg.Core/SyncedProjects.cs`, `src/Darg.Core/GitHub.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test with `fake_gh.py` playing the clone shows a missing project listed, Clone adding it to instance.json with the cloned path and its tab appearing, and a failed clone showing the account it tried; `app.Shot` shows the Setup list.
**Blocked by:** A synced project list with each repo's remote

### Open on the project and section used last, on either machine
Darg reopens the last project but not the section, and only from this machine. Keep the section too, and write `synced/last-open.json` (project id, section, machine name, time) when either changes, at most once every 30 seconds and on close. On start, open the newer of this machine's `last-project` and the synced file; a section this project doesn't show opens Home. Never apply it while running.
**Where:** `src/Darg.Core/LastProject.cs`, `src/Darg.Core/SyncedFolder.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.Nav.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test closes the app on project B's Board and a second start opens B's Board; another writes a newer `last-open.json` naming project C's Reports, and the start opens C's Reports; switching tabs ten times in a second writes the file once.
**Blocked by:** Move the settings files into a synced folder

### Fettle chats with no project live in synced/chats
General Fettle chats (`AgentRecord.Project` null) are saved in `agents/~bench/` with absolute paths. Save them to `synced/chats/` instead, with cwd and preview image paths stored relative to the home folder or `synced/` and turned back on load, move their preview images to `synced/previews/`, move existing ones on migration, and move a chat out to the project store when `MascotViewModel.Guess` gives it a project. Chats with a project stay where they are.
**Where:** `src/Darg.Desktop/ViewModels/MascotViewModel.cs`, `src/Darg.Core/AgentStore.cs`, `src/Darg.Core/SyncedFolder.cs`, `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `tests/Darg.Desktop.Tests`
**Done when:** a test saves a no-project chat under one home folder and loads it under another with its cwd and previews resolving; migration moves existing no-project chats and leaves project chats; the chat still lists in every project's Fettle list.
**Blocked by:** Move the settings files into a synced folder

### Apply synced settings that change while Darg is open
When a pull changes `synced/`, Darg should show it without a restart. Watch the folder (debounced, off the UI thread) and apply each file: the theme through `ThemeManager`, Optimise tokens through `TokenSaver.Load` (running agents take it from their next turn, as the toggle does), the Flows list read again, `PushClient` restarted on a new ntfy topic, Setup's "On your other machine" refreshed. `last-open.json` is ignored while running.
**Where:** `src/Darg.Core/SyncedFolder.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Desktop/ViewModels/SettingsViewModel.cs`, `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test writes each file into `synced/` from outside the app and sees the theme change, the Optimise tokens toggle flip, a new flow listed and pushes go to the new topic (`FakeNtfy`), while editing `last-open.json` changes nothing on screen.
**Blocked by:** Move the settings files into a synced folder, Phone notification settings move to synced/ntfy.json

### Connect the settings repo from Settings
Settings gets a Sync part: the gh account, the repo name the repo Question settles, and Connect. Connect clones the repo if it exists, else creates it private (`gh repo create --private`) and pushes, refuses a public repo, then merges this machine's `synced/` files in, a file on both sides kept through the Sync panel's conflict handling. The connection (account, repo) is kept per machine outside `synced/`. Its Blocked by gets the `.darg` on its own branch feature's sync card ids once they exist (the ordering Question).
**Where:** `src/Darg.Desktop/ViewModels/SettingsViewModel.cs`, `src/Darg.Desktop/Views/SettingsView.axaml`, `src/Darg.Core/SyncedFolder.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test with a local bare repo standing in for GitHub connects instance A (creating it) then instance B (cloning it); B ends up with A's theme and flows plus its own, a file both had shows as a conflict, and a public repo is refused.
**Blocked by:** Move the settings files into a synced folder

### Keep the settings repo synced
Once connected, `synced/` syncs with the Auto-sync engine: pull on open (before the last project is picked), on window focus and on its timer; commit a few seconds after changes stop and push; commit and push on close. Commit messages are generated from what changed ("Darg settings: theme, 2 flows") and go through the Auto-sync engine's never-commit check. Settings' Sync part shows last synced, changes waiting, offline or a conflict. Its Blocked by gets the Auto-sync feature's card ids once they exist (the ordering Question).
**Where:** `src/Darg.Core/SyncedFolder.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Desktop/ViewModels/SettingsViewModel.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test with two instances on one bare repo changes the theme on A and sees it on B after B's next pull, with no restart; a change made offline pushes once the remote is back; a commit message holding a never-commit name is refused by the engine's check.
**Blocked by:** Connect the settings repo from Settings, Apply synced settings that change while Darg is open, Open on the project and section used last, on either machine
