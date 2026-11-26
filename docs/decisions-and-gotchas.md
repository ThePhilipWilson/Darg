# Decisions, findings and gotchas

The *why* behind Darg, the traps found and what was tried and dropped, harvested from the finished-agent reports (2026-11-16 to 2026-11-20). Each item names its card; the report is in [Reports](guide.md#reports) (`.darg/reports/`, `archive/`, or the instance's `projects/<project>/reports/`). Behaviour that is simply how the app works is in [guide.md](guide.md), [architecture.md](architecture.md) and [cards-and-qa.md](cards-and-qa.md).

## Decisions

- **Avalonia, native, one codebase** (2026-11-16): not a web app. HTML is the exception (artifacts, doc-site previews). Considered and rejected: Tauri 2, Slint, Compose Multiplatform, Flutter, Electron (no phone). See [PLAN.md](PLAN.md).
- **One app, run as instances**: instances can't see each other; work and home are separate on purpose. See [PLAN.md](PLAN.md) and [architecture.md](architecture.md#instances-and-projects).
- **Real Claude Code sessions, not the raw API**: hooks, skills, rules and plugins come along, and the structured stream is what lets Darg draw images and diffs inline ([architecture.md](architecture.md#agents)).
- **Docs in the repo** (Philip, 2026-11-18, WOR-0123): docs live in the repo's `docs/`; `.darg/` is git-ignored with opt-in; Darg only reads docs, with no junction back to the vault. Side findings: Alpha's 31 design docs and Site's 5 docs were never in git; Darg's docs path in `instance.json` pointed at a deleted folder.
- **Archive reports, don't delete** (WOR-0102): history survives, QA reports go to `reports/archive/`.
- **CNR is a label, not a column** (WOR-0109): Done plus a `cnr` label ([cards-and-qa.md](cards-and-qa.md#work-on)).
- **Inline confirm above 3 agents** (WOR-0125): the bulk buttons ask first rather than cap and queue. The card allowed either.
- **Git landing** (WOR-0122): see [cards-and-qa.md](cards-and-qa.md#known-gaps). GitHub and GitLab are read-only today apart from ✓ Mark read; the branch-per-card is prompt text only.
- **Darg is the only notifier** (WOR-0119): the `notify-complete`/`notify-decision` hooks in `~/.claude` are gone and `notify-permission.ps1` no longer toasts (it logs to `stats/permission-log.jsonl`). Only a question, a permission ask or a turn of 15 minutes or more notifies (WOR-0078).
- **Fettle makes a card for a described change and edits for "just fix it"** (WOR-0132): the unit tests check only the prompt text; the effect is checked by opt-in `DARG_LIVE=1` Haiku turns.
- **Fettle asks while you look at an agent** (WOR-0086): asked for a change while you are looking at an agent, it must ask "New ticket" or "Amend the agent's task". The report notes that repeated misses call for a runtime gate rather than more prompt text.
- **No studio name**: Darg's repo carries no studio name anywhere (files, test data, commits); test data uses `example-studio`.
- **Android as a thin client** (2026-11-20, AND-0001): a native Avalonia app over the Darg API, holding no agents, repos or boards of its own. See [ANDROID.md](ANDROID.md).

## Findings

- **Titling with small local models failed** (WOR-0082): qwen3:1.7b labelled 25 of 82 notes "bug", mistral 7B invented content. Hence Haiku for ticket titles and board placement.
- **Where tokens go** (WOR-0136, 30 days): Alpha 55% and the general `D:\GitHub` folder 30%, both interactive and outside Darg; Darg-run sessions about 12-13%, around 80% of that on Opus. The cost is re-reading a long context every turn (cache reads ran about 100x output; heavy sessions peaked at 165-310k context). Moving routine work to Standard saves at most about 10% overall. Recommended: `Otherwise: Standard`, QA and checker stages on Standard, designer on Deep, a fresh session at about 100 messages or 150k context, and log which profile was chosen. Not measured: sub-agent fan-out and briefing size.
- **Fettle's weak spots** (WOR-0135): making cards, finding the API, joining card state with agents. Fixed by the `new-card`, `cards`, `card` and `overview` commands (WOR-0139 to WOR-0141; [architecture.md](architecture.md#the-darg-api)).
- **Colour audit** (WOR-0121, then WOR-0137): the logo is pink `#d74f9d` and teal `#34c4bf`; Dark and Light themes now follow it (Light uses `#c13c88` and text teal `#1a8783` for contrast). Info blue, Girlypop, Fettle and post-its are deliberately unchanged; the logo has no blue.
- **Window drag** (WOR-0128): the project-tabs ListBox filled the bar and swallowed presses; it is now left-aligned and the bar's DockPanel has a transparent background. Tested by hit-test (`TitleBarTests`).
- **Minimized lists not restoring** (WOR-0106): could not be reproduced; they are stored as `"minimized": [...]` in `board.json`. If it recurs, look at `board.json` first: key still there means the app is wrong, key gone means something rewrote the file.
- **The wrong commit named in a card** (WOR-0118): it happens. WOR-0118's card named 9973adc (actually WOR-0120's); the dictation commit is f06176a. Check commit ids against `git log`.
- **Dictation accuracy** (WOR-0118): the untrained System.Speech en-GB recogniser gave confidence about 0.37 on synthetic speech and the default mic level was about 3/100, so pick the right input in Windows' Settings > Sound. The mic loop can't be automated without a virtual audio device. Darg now uses Whisper ([architecture.md](architecture.md#things-that-run-beside-the-app)).

## Gotchas

### Tests and build

- **Stray boards**: a stray board next to a test board bumps its id (`BoardStore.HighestUsed`); this caused the "unrelated" `TicketTitleTests` failure for a week (WOR-0082, WOR-0087). Boards go in their own folder ([getting-started.md](getting-started.md#tests)).
- **Fake-claude logs crossing tests** (WOR-0131): a late-starting fake-claude process from one test could log into the next test's `FAKE_LOG`. The class fix suggested: teardown waits for every agent process, or `FAKE_LOG` per agent. The closed-project fix removed the commonest cause.
- **Fake-claude log lines lost** (WOR-0158): two fakes appending at once overwrote each other (Python's append on Windows seeks, then writes); a lock folder now makes them take turns.
- **Empty runs fail** (WOR-0127): a run in which no test ran now fails (`-Filter SendBack` used to print "All passed").
- **One Avalonia app for all integration tests**: a rebuild per test failed about one run in six.
- **Timeouts under load** (WOR-0150): `ClosedProjectTests` once timed out under load and passed on rerun. With several agents' suites running at once, run yours in a worktree of its own.
- **"Not run live"**: QA reports saying it are code-and-test verified only, unless they name a live run (`bulk-start.ps1`, a UI Automation run, a console harness).
- **Leftover worktrees**: folders under `.claude/worktrees/` are often left behind because a dotnet build node holds them (a locked `wor-0140` one couldn't be removed); safe to delete later.

### UI

- **ToggleButton re-push** (WOR-0134): a `ToggleButton` bound one-way to a state doesn't re-push an unchanged value, so Fettle's Chat tab unchecked itself on click; `TabClicked` in `MainWindow.axaml.cs` re-checks it (`FettleTabTests`).
- **Fettle's chats dropdown** (WOR-0130): it needed several clicks because its popup sat inside the drag-handle header; dragging is now only from the title and tabs rows.
- **Hot reload and startup handlers**: hot reload doesn't rewire handlers set at startup, so Restart (in the ⟳ menu) after a Pull in ([getting-started.md](getting-started.md#the-live-copy)).
- **Clipped pips** (WOR-0068): `Button.critter` has Padding 6,6,6,0 and CornerRadius 0 so badges aren't cut off; the button is also the drag handle.
- **Drag lift** (WOR-0080): the lift is a snapshot of the card's bounds, so a polaroid's tape can clip mid-drag.
- **Overwritten status messages** (WOR-0131): a file watcher reload overwrote the status message after Reports → Clean up; the reload has a 400 ms debounce, so the message now holds 8 s.
- **Two pending asks** (WOR-0094): notification entries don't record which ask they came from, so with two pending asks both stay until the last is answered.
- **Logo and icon**: they only change after a publish or Update in the ⟳ menu.

### Agents

- **Finished agents hiding Work on** (WOR-0115): a finished agent was still counted as "on the card" and hid the Work on button; fixed in `AgentsViewModel.ForCard` and with agent purposes (WOR-0110).
- **Fettle chat with no session** (WOR-0133): a saved chat with no session starts a new chat when you send from Fettle.
- **Chats cut off mid-turn** (WOR-0157): + New chat used to end the open chat's session mid-turn, so Fettle made the card and never replied; a chat left mid-turn now finishes its turn.
- **Merging into a dirty checkout** (WOR-0074): merging a branch into a checkout holding another agent's uncommitted changes blocks the merge; commit exactly your own files and leave theirs.
- **Usage bar lag** (WOR-0069): only Darg's own agents feed the usage bar, so use in a terminal appears only after Darg's next turn.

### Process

- **No failing test is unrelated**: agents are told to name it and say whether it fails without their work.
- **Docs drift**: QA found docs naming labels and variables the app doesn't have (WOR-0154). `DocsTests` now checks every icon-led label, every `DARG_`/`WORKBENCH_` variable, every link and anchor, and the formatting rules in [README.md](README.md#how-these-docs-are-written).

## Not built or open

- **Phone**: [ANDROID.md](ANDROID.md) sets the approach. The desktop half is coming in: phone access to the API ([REMOTE.md](REMOTE.md), AND-0002), notifications through ntfy (AND-0006), the phone's board (AND-0004) and Fettle from the phone (AND-0007). The phone app is a shell so far (AND-0003: connection, project list, section bar), its sections still being filled in; it builds outside `Darg.sln` because the system dotnet has no Android workload ([getting-started.md](getting-started.md#the-phone-app)). [PHONE-PLAN.md](PHONE-PLAN.md) was the first draft.
- **Work tools**: Jira for the work instance; starting Forge and ComfyUI from Darg.
- **Git landing**: `landing` and MR settings ([cards-and-qa.md](cards-and-qa.md#known-gaps)).
- **Live tests**: a live test of real agents and real Haiku-drafted decisions; the decision box in Light and Girlypop was only checked by a theme-switching shot test.
- **Integration coverage**: none yet for Artifacts, Preview and Terminal (native web view and processes).
- **Agents view tool steps**: still drawn in full (Fettle's are folded).
