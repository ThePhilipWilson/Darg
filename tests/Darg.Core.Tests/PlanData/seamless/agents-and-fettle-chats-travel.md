---
kind: feature
status: draft
parent: seamless-machine-switching
---
# Agents and Fettle chats travel

A project's saved agents and Fettle chats live in its `.darg` with their pictures and their Claude conversations, written so either machine can read them. The other machine lists them, shows which are busy where, and carries one on with a message; if Claude won't resume it, a fresh session picks up from the transcript.

## Goals
- Sit down at the Mac, open Agents or Fettle, and find the PC's agents and chats there with their whole transcripts and pictures.
- Send one of them a message on the Mac and it carries on where it stopped on the PC, card branch and all.
- See at once that an agent is busy on the other machine, instead of starting a second copy of it.
- Never hit "no session to carry on": if the conversation won't resume, the agent starts again from its transcript and says so.
- A page or decision an agent made on the PC still reaches it from the Mac.

## Not doing
- Committing, pulling or pushing `.darg`. That is the `.darg` on its own branch and Auto-sync features; this one only writes files into `.darg` and reacts when a pull changes them.
- Fettle chats with no project. They go with Per-user settings sync.
- Running one agent on both machines, or moving one mid-turn (the project plan's Not doing).
- Uncommitted work in an agent's worktree. The Leaving check says it's there.
- Conversations of agents Darg no longer lists (most of the 800 MB in `~/.claude/projects`). Only saved agents' conversations are copied.
- Rewriting the Windows paths inside a Claude conversation file, unless the Resume spike finds resume needs it.
- Flow runs, consoles and dev servers: they last only while the app does.
- New phone screens. The phone gets the running-on field through the API it already reads.

## Questions

### An agent shows "running on the PC", but the PC went to sleep mid-turn and will never clear it. Can the Mac take it over?
- **Take over, after an inline confirm** (recommended): the record shows Take over beside "Running on the PC since 14:02"; pressing asks once, then the agent is the Mac's. When the PC next pulls, it sees the agent has moved and ends its own session with a note.
- **Take over only once the mark is old**: no button until the mark is two hours old, so a turn that's really running is never cut off.
- **Never**: the mark clears only from the machine running it. Safest, but a PC left asleep holds the agent until he's back at it.

**Context:** the mark is set when a turn starts and cleared when it ends, and it reaches the other machine only when that machine syncs. A crash or sleep mid-turn leaves it set.

**Answer:** Take over, after an inline confirm (Philip, 2026-11-24)

### Darg is public. Where does a card agent's branch go when its turn ends, so the Mac can carry on with its work?
- **The private `<repo>-darg` repo, as a second remote** (recommended): work in progress stays private, the same repo that already holds the public repo's `darg` branch. Private repos push card branches to their own remote as decided.
- **The public repo, after the never-commit check**: one remote, but every half-done branch is published, and your rule says public pushes need your OK each time.
- **Not pushed for public repos**: the agent stays on its machine, shown on the other as "on the PC, work not pushed", with no Carry on.

**Context:** the project plan settled that card branches are pushed at turn end and checked out on Carry on, before WOR-0197 made Darg public.

**Answer:** The private `<repo>-darg` repo, as a second remote (Philip, 2026-11-24)

### Pictures a transcript points at outside the project's repos (a Forge output on D:, a screenshot in Pictures): what goes with the agent?
- **A thumbnail copy** (recommended): a 260 px copy, the size the transcript shows, about 30 KB each, kept in the agent's folder. Clicking it opens the full picture only where the original is.
- **The full file**: every picture opens full size on both machines, but an art agent's folder can grow by megabytes a turn.
- **Nothing**: the other machine shows the path and "on the PC".

**Context:** pictures a tool sent back as bytes are Darg's own files and go with the agent whatever you pick. Pictures inside a repo are found there by their repo-relative path.

**Answer:** thumbnail copy and an explanation with the pc name that holds the original. option for it to be in git if the project is using LFS for example (Philip, 2026-11-24)

### What happens to the 89 MB of preview images in the instance folder?
- **Delete them when agents move into `.darg`** (recommended): no saved agent points at any of them (checked 2026-11-24). Pictures a saved agent does point at move with it first.
- **Leave them where they are**: nothing lost, nothing gained; the folder stops growing because new previews go to the agent's folder.

**Context:** they are thumbnails of tool results for agents already forgotten. Forgetting an agent never deleted its previews.

**Answer:** Delete them when agents move into `.darg` (Philip, 2026-11-24)

### When a resume fails, what does the fresh session get?
- **The transcript as a file, named in its first message with the last few exchanges quoted** (recommended): it reads as much of the history as it needs, and the opening message stays small.
- **The whole transcript in its first message**: nothing to look up, but a long agent's first turn costs a large share of the usage window.
- **A Haiku summary of the transcript**: short, but Haiku decides what was important and the agent can't check.

**Context:** the transcript Darg saves is what the Agents view shows: your messages, the agent's replies and a line per tool step, not the tool output itself.

**Answer:** The transcript as a file, named in its first message with the last few exchanges quoted (Philip, 2026-11-24)

## Design

### What it builds on
- `AgentStore` saves an `AgentRecord` per agent after every turn (`SaveLater`, off the UI thread), in `<instance>/agents/<project id>/<id>.json`. Fettle's chats are records in `<instance>/agents/~bench/`, each with a `Project` (or none).
- `ProjectData.Root` already gives `<first repo>/.darg`, and `ProjectData.Migrate` already moves old instance folders into it and rewrites saved agents' paths (`Rewrite`).
- Resuming is `--resume <session id>` in `AgentViewModel.Begin`; Claude Code keeps the conversation in `~/.claude/projects/<key of the folder it ran in>/<session>.jsonl`, some with a folder of the same name beside it.
- Measured on the PC, 2026-11-24: 12 saved project agents and 3 Fettle chats, records 408 KB; their 13 conversations 11.8 MB, two with a side folder. 89 MB of previews, none pointed at by a saved agent.

### Where things live
```
.darg/agents/<id>/agent.json     the record, paths as tokens
.darg/agents/<id>/previews/      pictures its transcript shows
.darg/agents/<id>/session/       its Claude conversation (<session>.jsonl, and its folder if any)
.darg/agents/<id>/transcript.md  written only when a resume falls back
.darg/chats/<id>/...             a project's Fettle chats, the same shape
```
A folder per agent, so forgetting one deletes one folder and the deletion syncs. Chats with no project stay in the instance folder for Per-user settings sync.

### Paths that read on both machines
Every path in a record is stored as a token plus the rest, and turned back into this machine's on load: `{repo:Darg}` (a repo of the project, by folder name, as project.json names them), `{darg}`, `{instance}`, `{home}`. That covers `Cwd`, `Args` (`--add-dir`, the artifact folder, the reports and mistakes folders), `Plan`, and each item's `ImagePath` and `Images`. A path under none of the roots is kept as it is and shown as missing on a machine without it.

The class to defend against is a path that names the same place in another spelling. Records hold at least these: back slashes (`D:\GitHub\...`), forward slashes (`D:/GitHub/...`), mixed (`D:/GitHub/ThePhilipWilson/Darg/docs\design`, seen in a saved planner), JSON-doubled back slashes, a different drive-letter case, a trailing slash, and Mac paths (`/Users/...`). One `PathTokens` class does both ways, and its tests run every form against Windows and Mac roots.

Fettle chats run in the instance folder with every project's folders as `--add-dir`. Their args are not carried across but built again from this machine's project list when a chat resumes, since the other machine may not have every project. A project agent's expanded `--add-dir` naming a folder that doesn't exist here is dropped with a transcript note.

### Pictures
New previews (images a tool sent back as bytes) are written to the agent's own `previews/` instead of `<instance>/previews`. Pictures a transcript points at inside a repo are stored repo-relative and found there. Pictures outside the repos follow the Question's answer. Moving agents into `.darg` moves the previews they point at, then handles the rest of `<instance>/previews` as answered.

### The conversation
When a turn ends, Darg copies the agent's conversation file (and its side folder, if the Resume spike says it's needed) into `session/`. Before `--resume`, if this machine's Claude folder has no copy under the key for the agent's cwd here, or a shorter one, Darg copies `session/` there first. Copying happens off the UI thread and only for saved agents; a forgotten agent's folder goes, conversation and all. The key and the file list come from the Resume spike's findings: that feature runs first, and this one doesn't guess them.

### Running where
A record carries the machine that last ran it (a stable id kept in the instance folder, and a label: "the PC" on Windows, "the Mac" on macOS, the machine name added when both are the same OS) and, while a turn runs, when it started. It's saved at once when a turn starts and when it ends, so the mark goes out with the next sync.

On the other machine:
- the list and transcript show "Running on the PC since 14:02", the box is greyed with that reason, and the API's `agents`, `agent` and `needs-you` carry `runningOn`, so the phone shows it and `say` refuses it.
- once the mark is clear, sending a message carries it on here: the record becomes this machine's, and the transcript notes "Carried on from the PC".
- a stuck mark is handled as the first Question settles. The machine that loses an agent ends its own session when a pull shows the record now names another machine.
- `ContinueAt` (carry on after a usage reset) is honoured only by the machine the record names, so the two don't both resume it.
- a `darg.send()` or a decision answer for an agent running on the other machine is refused with that reason; for one that's free, it reaches the record and carries it on here.

### The list follows the disk
Today the Agents list reads its store once, at start. It now watches `.darg/agents` (and Fettle `.darg/chats`): a record a pull adds shows up, a changed one that isn't live here reloads its transcript, and a deleted one goes, unless it's live here, in which case its next save writes it back.

### Card branches
When a card agent's turn ends with commits on its card branch, Darg pushes the branch (where, per the second Question). Carrying it on where the branch isn't local fetches it and adds the same worktree (`.claude/worktrees/<branch>`) before resuming, so the agent finds its work at the path its history names, relative to the repo.

### When resume fails
The failure class: no conversation anywhere; Claude refuses the session (exits, or ends the first turn with an error, before a session starts); the conversation resumes but Claude reports it can't read it. In each case Darg writes `transcript.md` from the saved items, starts a fresh session in the same folder with the opening the last Question settles, says so in the transcript ("Couldn't resume: …; started fresh from the transcript"), and stores the new session id. This is wanted with or without a second machine, so it lands first. A cwd that doesn't exist here is a different case: it says which repo to clone or which branch couldn't be fetched, and starts nothing.

### Moving in
`ProjectData.Migrate` moves each project's `<instance>/agents/<project id>/*.json` and `~bench` chats with that project into the new folders, tokenising paths as it goes, alongside its existing moves; the footer line says what moved. Running twice moves nothing.

### Risks
- **Resume across paths may not work.** The Resume spike answers it; the fallback keeps every agent usable whatever it finds.
- **Conversation files hold everything the agent read**, file contents included. They go only to the `darg` branch, which for a public repo is the private `<repo>-darg` repo (the project plan's first Answer).
- **A save after every turn means a sync after every turn.** Auto-sync batches commits after changes stop; a long session's `session/` file grows a few hundred KB a turn.
- **Both machines carry on one agent while apart (offline).** The record and the conversation conflict; Auto-sync's Sync panel keeps both sides, and the agent shows which machine's version it's on.

## Cards

### Agent records store paths as tokens that read on both machines
Saved agent records hold absolute Windows paths in Cwd, Args, Plan and each transcript item's ImagePath and Images, so a record copied to the Mac points at nothing. Add a `PathTokens` class in Core that turns a path under a known root into a token (`{repo:<folder name>}`, `{darg}`, `{instance}`, `{home}`) plus the rest, and back again with this machine's roots, and have `AgentStore` apply it on every write and read. It must match the whole class of spellings: back slashes, forward slashes, mixed, JSON-doubled back slashes, drive-letter case, a trailing slash and Mac paths. A path under no root stays as it is.
**Where:** `src/Darg.Core/PathTokens.cs` (new), `src/Darg.Core/AgentStore.cs`, `tests/Darg.Core.Tests/`
**Done when:** unit tests round-trip every spelling listed in the plan against Windows roots and Mac roots (a record saved with `D:\...` roots loads with `/Users/...` roots and every path points under them), and an unknown path survives unchanged.
**Blocked by:** none

### Resume that fails starts fresh from the transcript
Today a saved agent whose conversation Claude can't resume just fails, and the person has to start a new agent and explain it all again. Detect the failure class (no conversation, Claude refusing the session id before a session starts, a first turn that ends in an error about the session), then write `transcript.md` beside the agent's record from its saved items, start a fresh session in the same folder whose first message is shaped as the plan's last Question settles, store the new session id and add a transcript note saying why. Run `claude --resume` with a made-up id once to record Claude's real output, and teach fake claude to play it (e.g. `FAKE_NO_SESSION`). A cwd that doesn't exist on this machine says so and starts nothing.
**Where:** `src/Darg.Desktop/ViewModels/AgentsViewModel.cs` (`AgentViewModel.Begin`, `On`), `src/Darg.Core/AgentStore.cs`, `tools/perf/fake_claude.py`, `tests/Darg.Desktop.Tests/`
**Done when:** an integration test resumes a saved agent whose session fake claude refuses, and the agent carries on in a new session with the transcript note shown, `transcript.md` written and the new session id saved; breaking the detection makes the test fail.
**Blocked by:** none

### Saved agents and project Fettle chats move into .darg
Move each project's saved agents from `<instance>/agents/<project id>/` to `.darg/agents/<id>/agent.json`, and Fettle chats that belong to a project from `<instance>/agents/~bench/` to that project's `.darg/chats/<id>/chat.json`, a folder per agent so forgetting deletes one folder. `AgentsViewModel` and `MascotViewModel` read and write there through `ProjectData.Root`; chats with no project stay in the instance folder. Fettle chats rebuild their args from this machine's project list when they resume instead of using saved ones. `ProjectData.Migrate` moves the old files once (tokenising paths), and `ProjectData.Rewrite` follows the new folders.
**Where:** `src/Darg.Core/AgentStore.cs`, `src/Darg.Core/ProjectData.cs`, `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `src/Darg.Desktop/ViewModels/MascotViewModel.cs`, `tests/Darg.Core.Tests/`, `tests/Darg.Desktop.Tests/`
**Done when:** a test instance with agents and chats in the old folders starts with them listed as before and their files in `.darg/agents` and `.darg/chats`; a second start moves nothing; forgetting an agent removes its folder; a resumed Fettle chat's args name this instance's projects, not the saved ones.
**Blocked by:** Agent records store paths as tokens that read on both machines

### Preview pictures go with the agent that shows them
Tool previews are written to `<instance>/previews`, which no other machine sees, and 89 MB of them belong to no saved agent. Write new previews into the agent's own `previews/` folder in `.darg`, store pictures inside a repo repo-relative, and handle pictures outside the repos as the plan's Question on them settles (a thumbnail copy, if that's the answer). Restoring a transcript shows the copy when the original isn't on this machine. Migration moves the previews a saved agent points at into its folder and treats the rest of `<instance>/previews` as the Question on the 89 MB settles.
**Where:** `src/Darg.Desktop/ViewModels/AgentsViewModel.cs` (`ToolItem.SetResult`, `ToolItem.Restore`), `src/Darg.Core/ProjectData.cs`, `src/Darg.Core/AgentStore.cs`, `tests/Darg.Desktop.Tests/`
**Done when:** an integration test's tool result with an image puts the file under `.darg/agents/<id>/previews/`, and the transcript restored with the original deleted still shows the picture; migration of a test instance leaves no preview a saved agent points at in the instance folder.
**Blocked by:** Saved agents and project Fettle chats move into .darg

### The Claude conversation is copied into .darg and back before resume
For Carry on to work on the other machine, the agent's Claude conversation has to travel with it. Start only once the Resume spike feature has reported: its findings say how Claude Code makes the folder key and which files a resume needs. When a turn ends, copy the conversation file (and its side folder if needed) from Claude's folder into the agent's `session/`; before `--resume`, if this machine's Claude folder has no copy under the key for the agent's cwd here, or a shorter one, copy `session/` in first. Off the UI thread, saved agents only, and through `UserHome` so tests never touch the real `~/.claude`.
**Where:** `src/Darg.Core/SessionCarry.cs` (new), `src/Darg.Core/UserHome.cs`, `src/Darg.Desktop/ViewModels/AgentsViewModel.cs` (`Begin`, turn end), `tools/perf/fake_claude.py` (writes a conversation file under the sandbox's Claude folder, as the real one does), `tests/Darg.Core.Tests/`, `tests/Darg.Desktop.Tests/`
**Done when:** a unit test with two fake home folders and two cwds copies a session from one machine's key into the other's before resume, and leaves a longer local copy alone; an integration test shows `session/` filled after a turn.
**Blocked by:** Saved agents and project Fettle chats move into .darg

### Agents show which machine runs them and only that machine runs them
Add the running machine to the record: a stable machine id kept in the instance folder, a label ("the PC", "the Mac", plus the machine name when both are the same OS), and the start time of a running turn, saved at once when a turn starts and ends. A record running on another machine lists as "Running on the PC since 14:02" with the message box greyed; once clear, a message carries it on here and the transcript notes it. Take over follows the plan's first Question. A machine that finds its live agent now named for another machine ends its session with a note. `ContinueAt` is honoured only by the named machine. The API's `agents`, `agent` and `needs-you` carry `runningOn`; `say`, `decide` to its agent and `darg.send()` to it are refused with that reason.
**Where:** `src/Darg.Core/AgentStore.cs` (`AgentRecord`), `src/Darg.Core/Instance.cs`, `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `src/Darg.Desktop/Views/`, `src/Darg.Desktop/ViewModels/MainViewModel.Api.cs`, `src/Darg.Core/Artifacts.cs`, `tests/Darg.Desktop.Tests/`
**Done when:** an integration test with a record marked as another machine's shows the running-on line and a refused `say`; clearing the mark lets a message resume it here with the note; a live agent whose record is rewritten to another machine ends its session.
**Blocked by:** Saved agents and project Fettle chats move into .darg

### The Agents list and Fettle's chats follow changes on disk
The Agents view reads its saved agents once at start, so records a pull brings in would never show. Watch `.darg/agents` and `.darg/chats` (debounced, off the UI thread, updating the bound lists in place with `ListSync`): a new record appears, a changed record that isn't live here reloads its transcript and running-on state, a deleted one leaves the list unless it's live here, in which case its next save writes it back.
**Where:** `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `src/Darg.Desktop/ViewModels/MascotViewModel.cs`, `tests/Darg.Desktop.Tests/`
**Done when:** an integration test that writes a new record, edits a saved one and deletes another under `.darg/agents` sees the list follow each within a few seconds, and a live agent's deleted record comes back after its next turn.
**Blocked by:** Saved agents and project Fettle chats move into .darg

### Card branches are pushed at turn end and checked out on Carry on
A card agent works on its card branch in `.claude/worktrees/<branch>`, which exists only on the machine it ran on. When its turn ends with commits on that branch, push the branch to the remote the plan's second Question settles (signing in with the project's account through `gh auth token --user`, running the never-commit check first on a public repo). Carrying it on where the branch or its worktree isn't present fetches the branch and adds the worktree at the same repo-relative path before resuming. Uncommitted changes don't travel; the transcript says so if the worktree had any.
**Where:** `src/Darg.Core/CardBranch.cs`, `src/Darg.Core/Repo.cs`, `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`, `tests/Darg.Core.Tests/`
**Done when:** a unit test with a bare remote and two clones pushes a card branch from one after a turn and, on Carry on in the other, finds the branch checked out in `.claude/worktrees/<branch>` with the commit in it.
**Blocked by:** The Claude conversation is copied into .darg and back before resume

### Two machines in one test: an agent carried on from the other
Each card above tests its own part; nothing yet shows the whole trip. Write an integration test with two throwaway instances standing in for the PC and the Mac, with different repo paths for the same repo, sharing its `.darg` by copying it between them as a sync would. An agent runs a turn on the first; the second lists it with its transcript and preview, shows it running while the mark is set, then carries it on with the conversation copied under its own key, and the first ends its copy. A second run with the conversation removed falls back to a fresh session from the transcript.
**Where:** `tests/Darg.Desktop.Tests/`, `tools/perf/fake_claude.py`
**Done when:** the test passes in `pwsh -File tools/test.ps1 -Integration`, and fails if path tokens, the conversation copy or the running-on mark is broken.
**Blocked by:** Resume that fails starts fresh from the transcript, Preview pictures go with the agent that shows them, Agents show which machine runs them and only that machine runs them, The Agents list and Fettle's chats follow changes on disk, The Claude conversation is copied into .darg and back before resume
