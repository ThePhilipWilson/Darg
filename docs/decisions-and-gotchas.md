# Decisions, findings and gotchas

Harvested from the finished-agent reports (2026-11-16 to 2026-11-20). Each item names the card; its report is in Reports (`.darg/reports/`, `archive/`, or the instance's `projects/<project>/reports/`). Behaviour that is simply how the app works is in [guide.md](guide.md), [architecture.md](architecture.md) and [cards-and-qa.md](cards-and-qa.md); this page keeps the *why*, the traps and what was tried and dropped.

## Decisions

- **Avalonia, native, one codebase** (2026-11-16): not a web app. HTML is the exception (artifacts, doc-site previews). Considered and rejected: Tauri 2, Slint, Compose Multiplatform, Flutter, Electron (no phone). [PLAN.md](PLAN.md).
- **One app, run as instances; instances can't see each other.** Work and home are separate on purpose. [PLAN.md](PLAN.md).
- **Real Claude Code sessions, not the raw API**, so hooks, skills, rules and plugins come along; the structured stream is what lets Darg draw images and diffs inline.
- **Docs live in the repo's `docs/`; `.darg/` is git-ignored with opt-in; Darg only reads docs** (Philip, 2026-11-18, WOR-0123). No junction back to the vault. Side findings: Alpha's 31 design docs and Site's 5 docs were never in git; Darg's docs path in `instance.json` pointed at a deleted folder.
- **Archive reports, don't delete** (WOR-0102): history survives, QA reports go to `reports/archive/`.
- **CNR is Done plus a `cnr` label, not a column** (WOR-0109).
- **Inline confirm above 3 agents** for the bulk buttons, not cap-and-queue (WOR-0125). The card allowed either.
- **Git landing** (WOR-0122): see the end of [cards-and-qa.md](cards-and-qa.md). GitHub and GitLab are read-only today; the branch-per-card is prompt text only.
- **Darg is the only notifier** (WOR-0119): the `notify-complete`/`notify-decision` hooks in `~/.claude` are gone and `notify-permission.ps1` no longer toasts (it logs to `stats/permission-log.jsonl`). Only a question, a permission ask or a turn of 15 minutes or more notifies (WOR-0078).
- **Fettle makes a card for a described change; it edits for "just fix it"** (WOR-0132). The unit tests check only the prompt text; the effect is checked by opt-in `DARG_LIVE=1` Haiku turns.
- **When you ask Fettle for a change while looking at an agent, it must ask** "New ticket" or "Amend the agent's task" (WOR-0086). The report notes that repeated misses call for a runtime gate rather than more prompt text.
- **Docs and the studio name**: Darg's repo carries no studio name anywhere (files, test data, commits); test data uses `example-studio`.

## Findings

- **Titling with small local models failed** (WOR-0082): qwen3:1.7b labelled 25 of 82 notes "bug", mistral 7B invented content. Hence Haiku for ticket titles and board placement.
- **Where tokens go** (WOR-0136, 30 days): Alpha 55% and the general `D:\GitHub` folder 30%, both interactive and outside Darg; Darg-run sessions about 12-13%, around 80% of that on Opus. The cost is re-reading a long context every turn (cache reads ran about 100x output; heavy sessions peaked at 165-310k context). Moving routine work to Standard saves at most about 10% overall. Recommended: `Otherwise: Standard`, QA and checker stages on Standard, designer on Deep, a fresh session at about 100 messages or 150k context, and log which profile was chosen. Not measured: sub-agent fan-out and briefing size.
- **Fettle's weak spots** (WOR-0135): making cards, finding the API, joining card state with agents. Fixed by the `new-card`, `cards`, `card` and `overview` commands (WOR-0139 to 0141).
- **Colour audit** (WOR-0121, then WOR-0137): the logo is pink `#d74f9d` and teal `#34c4bf`; Dark and Light themes now follow it (Light uses `#c13c88` and text teal `#1a8783` for contrast). Info blue, Girlypop, Fettle and post-its are deliberately unchanged; the logo has no blue.
- **Window drag** (WOR-0128): the project-tabs ListBox filled the bar and swallowed presses; it is now left-aligned and the bar's DockPanel has a transparent background. Tested by hit-test (`TitleBarTests`).
- **Minimized lists not restoring** (WOR-0106) could not be reproduced; they are stored as `"minimized": [...]` in `board.json`. If it recurs, look at `board.json` first: key still there means the app is wrong, key gone means something rewrote the file.
- **The wrong commit named in a card** happens: WOR-0118's card named 9973adc (actually WOR-0120); the dictation commit is f06176a. Check commit ids against `git log`.
- **Dictation accuracy** (WOR-0118): the untrained System.Speech en-GB recogniser gave confidence about 0.37 on synthetic speech and the default mic level was about 3/100, so pick the right input in Settings > Sound. The mic loop can't be automated without a virtual audio device.

## Gotchas

**Tests and build**
- A stray board next to a test board bumps its id (`BoardStore.HighestUsed`); this caused the "unrelated" `TicketTitleTests` failure for a week (WOR-0082, WOR-0087). Boards go in their own folder.
- A late-starting fake-claude process from one test could log into the next test's `FAKE_LOG` (WOR-0131). The class fix suggested: teardown waits for every agent process, or `FAKE_LOG` per agent. The closed-project fix removed the commonest cause.
- A run in which no test ran now fails (`-Filter SendBack` used to print "All passed", WOR-0127).
- Integration tests share one Avalonia app because a rebuild per test failed about one run in six.
- `ClosedProjectTests` once timed out under load and passed on rerun (WOR-0150).
- QA reports saying "not run live" are code-and-test verified only, unless they name a live run (`bulk-start.ps1`, a UI Automation run, a console harness).
- Worktree folders under `.claude/worktrees/` are often left behind because a dotnet build node holds them; safe to delete later.

**UI**
- A `ToggleButton` bound one-way to a state doesn't re-push an unchanged value, so Fettle's Chat tab unchecked itself on click; `TabClicked` in `MainWindow.axaml.cs` re-checks it (`FettleTabTests`, WOR-0134).
- Fettle's chats dropdown needed several clicks because its popup sat inside the drag-handle header; dragging is now only from the title and tabs rows (WOR-0130). Hot reload doesn't rewire handlers set at startup: Restart (in the ⟳ menu) after a Pull in.
- Clipping a pip: `Button.critter` has Padding 6,6,6,0 and CornerRadius 0 so badges aren't cut off; the button is also the drag handle (WOR-0068).
- The drag lift is a snapshot of the card's bounds, so a polaroid's tape can clip mid-drag (WOR-0080).
- A status message that a file watcher reload overwrites (Reports → Clean up): the reload has a 400 ms debounce, so the message now holds 8 s (WOR-0131).
- Notification entries don't record which ask they came from, so with two pending asks both stay until the last is answered (WOR-0094).
- The logo and Start menu icon only change after a publish or ⟳ Update.

**Agents**
- A finished agent was still counted as "on the card" and hid the Work on button; fixed in `AgentsViewModel.ForCard` (WOR-0115) and with agent purposes (WOR-0110).
- A saved chat with no session starts a new chat when you send from Fettle (WOR-0133).
- Merging a branch into a checkout holding another agent's uncommitted changes blocks the merge; commit exactly your own files and leave theirs (WOR-0074).
- Only Darg's own agents feed the usage bar, so use in a terminal appears only after Darg's next turn (WOR-0069).

**Process**
- Agents are told "no failing test is unrelated": name it and say whether it fails without your work.
- A leftover locked `wor-0140` worktree can't be removed while a build node holds it.

## Not built / open

- Phone and tablet ([PHONE-PLAN.md](PHONE-PLAN.md)): nothing built; the link server comes first.
- Jira for the work instance; starting Forge and ComfyUI from Darg.
- Git `landing`/MR settings (see [cards-and-qa.md](cards-and-qa.md)).
- A live test of real agents and real Haiku-drafted decisions; the decision box in Light and Girlypop was only checked by a theme-switching shot test.
- Integration coverage for Artifacts, Preview and Terminal (native web view and processes).
- The Agents view still draws tool steps in full (Fettle's are folded).
