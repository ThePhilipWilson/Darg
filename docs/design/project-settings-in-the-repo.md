---
kind: feature
status: agreed
parent: seamless-machine-switching
board: Project settings in the repo
---
# Project settings in the repo

A project's own settings (id, name, colour, saved commands, commit rules, preview, GitHub account, features) live in `.darg/project.json` in its first repo. instance.json keeps only where things are on this machine, and adding a folder that has a project.json keeps its id.

## Goals
- Add a project's folder on a second machine and it is the same project: same id, so its agents, chats, artifact links and phone-notification keys still match.
- Change a saved command, commit rule or preview once and see it on the other machine, with nothing to copy by hand.
- Open instance.json and see only what is about this machine: which projects are open here and where their folders are.
- Move today's settings across without losing any, and without a project vanishing or changing id.

## Not doing
- Syncing project.json itself (the `darg` branch and Auto-sync do that). Until they land, project.json is a file in `.darg`, which is already excluded from git.
- New editing screens. Setup keeps its boxes; anything it has no box for (Preview) is edited in project.json.
- Per-user settings (theme, profiles, shared flows, push topic) and the project list with remotes: that is Per-user settings sync.
- Where repos, boards, notes and docs are on this disk. Those paths stay in instance.json.
- Changing the board, notes or reports formats, or moving boards into repos.
- Cloning anything automatically. Setup offers; he taps.

## Questions

### How do today's settings move out of instance.json?
- **Automatically on the first start, the old keys removed, a backup kept** (recommended): Darg writes each project's project.json from instance.json, removes the moved keys, keeps `instance.json.bak`, and says what moved in the footer line (as `ProjectData.Migrate` does). Nothing to do.
- **Automatically, but instance.json keeps its copy**: safer to roll back, but two places say the same thing and drift apart, which is what this feature removes.
- **Only when he saves Setup**: nothing happens behind his back, but the other machine has no project.json until he remembers to.

**Context:** the Darg project is the first to move, and a repo with no `.darg` yet has nowhere to put the file until Darg makes the folder.

**Answer:** Automatically on the first start, the old keys removed, a backup kept (Philip, 2026-11-24)

### Which wins when project.json and instance.json both carry a setting?
- **project.json** (recommended): the repo's copy is the shared one, and a leftover key in instance.json is ignored and cleaned up on the next save.
- **instance.json**: a machine can override a shared setting, but then a change from the other machine silently doesn't show here.
- **Neither: a conflict shown in Setup**: nothing is guessed, but a screen to build for a rare case.

**Context:** this only happens after a hand edit, a restored backup or a half-finished migration.

**Answer:** project.json (Philip, 2026-11-24)

### Saved commands are written for one shell. How do they work on the other OS?
- **An optional `os` on each command** (`windows`, `mac`), and a command with none runs everywhere (recommended): the Mac sees only its own and the shared ones; Setup's box takes `[mac] Name: command` lines.
- **One list, as now**: simplest, but a PowerShell command is shown on the Mac and fails when run.
- **Separate lists per OS in project.json**: clear, but the common ones are written twice.

**Context:** saved commands always run in the platform's first shell (PowerShell on Windows, zsh on the Mac).

**Answer:** An optional `os` on each command (Philip, 2026-11-24)

### Which folders can a commit rule name, now that paths differ by machine?
- **A repo's folder name, plus `docs` and `boards` for those folders** (recommended): `{"Site": "ask", "docs": "push"}` reads the same on both machines. Today's full-path keys are converted on migration, and one that matches no repo, docs or boards folder is kept as written and listed.
- **Repo names only**: simplest; a rule on the vault or docs folder is dropped on migration and listed.
- **Paths relative to the first repo**: precise, but breaks when repos are laid out differently on the Mac.

**Context:** `Commits.For` works by the deepest folder holding the work, and the vault or docs folder can have its own rule.

**Answer:** A repo's folder name, plus `docs` and `boards` for those folders (Philip, 2026-11-24)

### The first repo isn't on this machine yet. What does the project show?
- **The last project.json read, kept in the instance folder** (recommended): the project opens with its commands, features and account, its Code and agents sections say the repo is missing, and Setup offers to clone it. A project's id never depends on a folder being there.
- **Only "repo missing, clone it"**: no copy to go stale, but the project's tab is empty until cloned.
- **The next repo in the list that exists**: works for Site, but project.json belongs with the first repo and a second one has none.

**Context:** the project plan has project.json in the first repo; Setup offers to clone a missing one.

**Answer:** The last project.json read, kept in the instance folder (Philip, 2026-11-24)

### When project.json changes under a running Darg (a pull, a text editor), what happens?
- **The project reloads at once, as Setup's Apply does** (recommended): Darg watches the file; a changed project swaps in and the others keep running (agents and terminals in the project keep going).
- **Read on next start only**: the least to build, but a pull that changes a commit rule leaves agents on the old rule until a restart.
- **Ask first**: a prompt for every pull that touches it.

**Context:** the project plan has Auto-sync pull every few minutes, so this will be a normal event.

**Answer:** The project reloads at once, as Setup's Apply does (Philip, 2026-11-24)

## Design

### What it builds on
- `ProjectConfig` (in `Instance.cs`) already holds every setting named here, and everything else in the app reads it. It stays the in-memory model, so no caller changes: only where it is read from and written to.
- `ProjectData.Root` gives the first existing repo's `.darg`, and `.darg/` is already excluded from git. project.json goes there.
- `ProjectData.Migrate` is the pattern for a first-start move with a footer line saying what moved.
- Setup's `AddFolder` and `ProjectDetect.FromFolder` already work out a project from a folder.
- `MainViewModel.Apply` already swaps in only the projects that changed.

### The files
**`.darg/project.json`** (new `ProjectFile` in Core): `version`, `id`, `name`, `color`, `commands` (name, command, cwd relative to a repo, optional `os`), `commits` (keys as the Question settles), `preview` (command, cwd relative, url), `github`, `features`, and `repos`: each repo's folder name and remote URL, in order, the first holding the file. camelCase, tolerant of comments and trailing commas like instance.json, unknown keys kept on a rewrite so a newer Darg's fields survive an older one.

**instance.json** per project keeps: `id`, the repo paths in the same order, `boards`, `notes`, `docs`. The instance's own keys (name, tools, shell, push, remote) stay. Tab order is the order of this list.

### Reading and writing
- `InstanceConfig.Load` reads the entries, finds each project's first existing repo, reads its project.json and fills the `ProjectConfig`. A project with no file yet is read as it is today.
- `InstanceConfig.Save` is the one write path: it splits a `ProjectConfig` into the two files, so every existing caller (Setup, the tab order, push) needs no change. project.json is rewritten only when its content changed, so a save doesn't touch the file's time or make a commit for nothing.
- Writes are to a temp file then a rename, so a sync never sees half a file.

### Adding a folder keeps its id
`ProjectDetect.FromFolder` looks for `.darg/project.json` first. If there is one, the project is built from it (id, name, colour, commands, rules, preview, account, features), and only paths are worked out from the folder: the repos are matched by folder name and remote against the file's list, and any not found here are listed. If another project here already has that id, Darg selects it and says so, rather than making a second. A folder with no file is detected as today and project.json is written on save.

### Where it can go wrong
- **Two writers.** Darg's live copy, debug copies and the installed copy share one `.darg`. Writes are small and atomic, and the change-only rule keeps them rare; the project plan's sync lock covers commits.
- **Absolute paths leaking into the shared file.** Commands' and preview's `cwd` are stored relative to a repo, and the writer refuses an absolute one under a repo, making it relative.
- **Public repo.** project.json carries the GitHub account name and commit rules. It goes to the `darg` branch, which the project plan sends to a private repo for a public one. Test data uses `example-studio`.

## Cards

### PRO-0001: Define the project.json file and read and write it
Add a `ProjectFile` class in Core that reads and writes `.darg/project.json` with version, id, name, colour, commands (with optional os), commit rules, preview, GitHub account, features and the repos' folder names and remote URLs. It must tolerate comments and trailing commas, keep unknown keys when rewriting, write through a temp file and rename, and skip the write when nothing changed. Cover reading a hand-written file, a round trip, an unknown key surviving, a broken file (reported, never overwritten) and the no-change write.
**Where:** `src/Darg.Core/ProjectFile.cs`, `tests/Darg.Core.Tests/ProjectFileTests.cs`
**Done when:** the new unit tests pass, including a broken file being left as it was, and a rewrite after no edit leaves the file's modified time unchanged.
**Blocked by:** none

### PRO-0002: Load and save a project's settings through the repo
Make `InstanceConfig.Load` read each project's `.darg/project.json` from its first existing repo and fill the `ProjectConfig` from it, and make `InstanceConfig.Save` split a `ProjectConfig` into project.json (id, name, colour, commands, rules, preview, account, features, repo names) and instance.json (id, repo paths, boards, notes, docs, tab order). The project wins over a leftover key in instance.json. A project with no file reads as it does today, and the existing callers that call Save must need no change.
**Where:** `src/Darg.Core/Instance.cs`, `src/Darg.Core/ProjectData.cs`, `tests/Darg.Core.Tests/InstanceTests.cs`
**Done when:** a unit test saves a project, finds its settings in project.json and only paths in instance.json, loads it back equal, and shows project.json winning over a leftover instance.json key. The existing suite still passes.
**Blocked by:** Define the project.json file and read and write it

### PRO-0003: Commit rules keyed by repo name, docs and boards
Store a project's commit rules in project.json keyed by repo folder name, with `docs` and `boards` for those folders, and have `Commits.For` and `Commits.Describe` resolve them against this machine's paths. Convert today's full-path keys when a project is first split out, and keep (and list) a key that matches nothing, so no rule is lost. Settle the key form as the question about commit rule folders answers.
**Where:** `src/Darg.Core/Commits.cs`, `src/Darg.Core/ProjectFile.cs`, `tests/Darg.Core.Tests/CommitsTests.cs`
**Done when:** a unit test shows the same project.json giving the right rule for a repo at different paths on two machines, a full-path key converted, and an unmatched key kept and listed.
**Blocked by:** Load and save a project's settings through the repo

### PRO-0004: Move today's settings out of instance.json on first start
Add a migration, run first in `MainViewModel`'s constructor like `ProjectData.Migrate`, that writes each project's project.json from its instance.json entry, removes the moved keys, keeps an `instance.json.bak`, and puts what moved in the footer line. It is done as the question about moving settings answers. A project with no repo on this machine is left in instance.json and listed, and running it twice changes nothing. Darg's own project keeps id `workbench`.
**Where:** `src/Darg.Core/ProjectData.cs`, `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `tests/Darg.Core.Tests/MigrationTests.cs`
**Done when:** a unit test migrates an old instance.json with two projects (one with no repo here), checks both files, the backup and the footer text, and a second run changes nothing. Ids, including `workbench`, are unchanged.
**Blocked by:** Commit rules keyed by repo name, docs and boards

### PRO-0005: Setup edits the shared and machine parts separately
Change Setup's Save and its change check (`SetupViewModel.Same`) so a project's name, colour, commands, rules, account and features go to project.json and its folders to instance.json, through the one Save path. Add a read-only Preview line and an Open project.json button so what has no box isn't hidden, and show a short line under the project saying which settings are shared with the repo.
**Where:** `src/Darg.Desktop/ViewModels/SetupViewModel.cs`, `src/Darg.Desktop/Views/SetupView.axaml`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test edits a command and a folder in Setup, saves, and finds the command in project.json and the folder in instance.json. A screenshot in `.shots/integration` shows the new line and button.
**Blocked by:** Load and save a project's settings through the repo

### PRO-0006: Adding a folder keeps the project's id
Make `ProjectDetect.FromFolder` and Setup's `AddFolder` read `.darg/project.json` first and build the project from it, working out only paths from the folder: repos matched by folder name and remote, others listed as missing. A project here with the same id is selected with a message rather than added twice. A folder with no file is detected as today.
**Where:** `src/Darg.Core/ProjectDetect.cs`, `src/Darg.Desktop/ViewModels/SetupViewModel.cs`, `tests/Darg.Core.Tests/ProjectDetectTests.cs`
**Done when:** a unit test adds a folder holding a project.json with id `workbench` and gets that id, name, commands and rules, and adding it again selects the existing project. The Found text names what was read from the file.
**Blocked by:** Load and save a project's settings through the repo

### PRO-0007: Second machine integration test
Add an integration test that plays the other machine: a throwaway instance with different folder paths adds the repo of a project made on the first instance, and checks it keeps the id, commands and commit rules, that an agent record saved under that id is listed, and that nothing absolute from the first instance appears in project.json. Test data uses `example-studio`, never real names.
**Where:** `tests/Darg.Desktop.Tests`, `tests/Darg.Desktop.Tests/DargApp.cs`
**Done when:** the test passes, and fails when `FromFolder` is changed to make a new id (break it once to check).
**Blocked by:** Adding a folder keeps the project's id

### PRO-0008: Offer to clone a repo that is missing here
When a project's repo from project.json isn't on this machine, show it in Setup with its remote URL and a Clone button that clones it into a folder he picks and adds the path to instance.json. The project's Code and Agents sections say the repo is missing until then. Clone runs off the UI thread with the project's GitHub account and never switches gh's active account.
**Where:** `src/Darg.Desktop/ViewModels/SetupViewModel.cs`, `src/Darg.Desktop/Views/SetupView.axaml`, `src/Darg.Core/Repo.cs`
**Done when:** an integration test with a local bare repo as the remote clones through the button, and the repo then shows as present. A failed clone shows its message and changes nothing.
**Blocked by:** Adding a folder keeps the project's id

### PRO-0009: Keep a copy of project.json when the first repo is missing
Keep the last project.json read in the instance folder, and use it when the project's first repo isn't on this machine, so the project still opens with its commands, features and account. It is never written back to the repo and is dropped once the repo is there. Do it as the question about a missing first repo answers.
**Where:** `src/Darg.Core/Instance.cs`, `src/Darg.Core/ProjectData.cs`, `tests/Darg.Core.Tests/InstanceTests.cs`
**Done when:** a unit test loads a project whose repo folder doesn't exist and gets its settings from the kept copy, then loads it with the repo present and prefers the file.
**Blocked by:** Load and save a project's settings through the repo

### PRO-0010: Reload a project when its project.json changes
Watch each project's project.json and, when it changes from outside (a pull, a text editor), reload that one project through the same swap `MainViewModel.Apply` does, leaving others running. Ignore Darg's own writes, tell the footer line what changed, and don't reload while Setup has unsaved edits to that project: say so there instead. Follow the answer to the question about changes under a running Darg.
**Where:** `src/Darg.Desktop/ViewModels/MainViewModel.cs`, `src/Darg.Core/ProjectFile.cs`, `tests/Darg.Desktop.Tests`
**Done when:** an integration test edits a commit rule in project.json while the app runs and sees the rule used by the next agent's briefing, with another project's agent untouched. Darg's own write causes no reload.
**Blocked by:** Load and save a project's settings through the repo

### PRO-0011: Commands that belong to one OS
Add the optional `os` to saved commands: the list shows and runs only the commands for this OS plus those with none, Setup's box takes a `[mac]` or `[windows]` prefix, and a command with an os the other machine hides is kept when saved there. Follow the answer to the question about OS-specific commands.
**Where:** `src/Darg.Core/Instance.cs`, `src/Darg.Desktop/ViewModels/SetupViewModel.cs`, `tests/Darg.Core.Tests/SavedCommandTests.cs`
**Done when:** a unit test parses and renders `[mac]` lines, shows only this OS's commands, and saving on Windows keeps the Mac-only command in project.json.
**Blocked by:** Load and save a project's settings through the repo

### PRO-0012: Update CLAUDE.md and the docs for project.json
Rewrite the lines in CLAUDE.md and the docs that say instance.json holds a project's settings: what is in project.json, what stays in instance.json, how adding a folder keeps an id, and the migration. Add the file's fields to the guide, and remove any "set in instance.json" for Preview. Names from the never-commit list stay out.
**Where:** `CLAUDE.md`, `docs/architecture.md`, `docs/guide.md`, `docs/getting-started.md`
**Done when:** a search of CLAUDE.md and `docs/` for settings said to live in instance.json finds only machine-local ones, and the file's fields are listed once in the guide.
**Blocked by:** Setup edits the shared and machine parts separately, Move today's settings out of instance.json on first start
