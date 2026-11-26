# Performance

How to find out what is slowing Darg down, and what the first trace found (WOR-0161). Darg is one process, so "renderer and main process" is one UI thread plus a thread pool; the tools below watch both.

## What is recorded

- **stalls.log** (WOR-0076): a heartbeat on the UI thread logs every gap over 200 ms with the named sections that ran in it, the time in none, and GC pauses. Since WOR-0161 a stall held over a second also gets the UI thread's stack, taken from another thread (`UiStacks`, a ClrMD snapshot of the process, so the app is never paused), up to 5 times per stall, indented under its line. A stack answers "in no section", which is layout, rendering, waits and unmeasured work.
- **perf.log** (WOR-0161): one line per minute in the instance folder: CPU in cores, working set, GC heap, gen2 collections, GC pause time, thread-pool size and queue, time the UI was held, and every top-level section and counted event with its time and count: board reloads, agent events, file watchers firing (`Watch.Board`, `Watch.Notes`, `Watch.Reports` and so on) and API calls (`Api work-on`). Memory climbing line after line is a leak; a section whose time keeps growing is the hotspot.
- **IPC**: Darg has no IPC between processes. Its equivalents are the API (`Api <command>` in perf.log), the agents' stream-json events (`Agent event <type>`) and file watchers.

## Profile it

Run a stretch of normal use with the extra logging, then read the logs.

```powershell
$env:DARG_PERF_SECONDS = 10   # a perf.log line every 10 s instead of 60
$env:DARG_STACK_MS = 300      # first stack after 300 ms held, instead of 1000
$env:DARG_STALL_MS = 100      # log stalls over 100 ms
& <install folder>\Darg.Desktop.exe   # or the debug build; use it as usual, then close it
pwsh -File tools/perf/analyze.ps1                             # the real instance
pwsh -File tools/perf/analyze.ps1 -Since '2026-11-20 18:00'   # from a time
```

`analyze.ps1` reads `stalls.log` and `perf.log` and prints totals, the sections that held the UI longest, stalls per hour, the longest stalls, the CPU and memory trend, and the busiest watchers and API calls. For a repeatable run on a throwaway instance with fake agents: `pwsh -File tools/perf/load.ps1 -Cards 6 -Touch -Switch -StallMs 100`, then `pwsh -File tools/perf/analyze.ps1 -InstanceHome .shots/perf-home` (the stacks are in `.shots/perf-home/stalls.log`). `DARG_STACKS=0` turns the stack sampling off. The variables are listed in [getting-started.md](getting-started.md#environment-variables).

## First trace

Taken 2026-11-20 from two sources: Philip's real `stalls.log` (17 to 20 Nov, the installed copy, so no stacks and no perf.log yet) and a load run (6 fake agents, switching sections and agents).

### The real log

- **456 stalls, 206 s of a frozen window** in four days; median 331 ms, 23 over a second, worst 31 s. 179 s of it is in no section, 133 s in named ones.
- **Worst hours**: 20 Nov 01:00 (68 stalls, 168 s) and 05:00 (28 stalls, 94 s), 18 Nov 00:00 to 02:00 and 11:00. They coincide with many agents running at once.
- **Board.Reload** is the biggest named cost (36 s over 36 stalls, worst 30 s). Inside it `Agents.RefreshCards` took 25 s of a 27 s `Agent event TurnFinished` stall: a turn ending reloads the board, which refreshes the cards' agents, which reloads the board. A turn finishing blocks the window for as long as that takes with several agents live.
- **Notes.Reload** 24 s over 15 stalls (worst 12.7 s) and **Reports.Reload** 12 s (worst 10 s): both read their files on the UI thread on each watcher event, and agents write in those folders constantly. **Home.Glance** once took 9 s.
- **Stalls of almost exactly 10.1 s** at `Agent.Begin` (20 Nov 05:05 to 05:06, 18 Nov 00:33, 20 Nov 01:53): a 10 s timeout somewhere on the UI thread while an agent starts. The old log has no stack, so the cause is unknown; the next occurrence will name it.

### The load run

- **Switching to an agent holds the UI 0.6 to 1 s each time**, "in no section", with 270 to 360 ms of it GC and the GC heap jumping 42 to 132 MB over six switches. The stack is the layout pass building the whole transcript: `ContentPresenter.UpdateChild`, `TemplateContent.Load`, then `TextBlock` creation and style attachment for every entry. The transcript is an `ItemsControl` with a `StackPanel` inside a `ScrollViewer` (`AgentsView.axaml`), which is not virtualised, so a switch costs the whole transcript and a long-running agent makes it worse. This is the likely reason the app has got slower with use.
- **Agent events** cost about 0.3 ms each on the UI thread (`ToolResult` 99 ms over 300 in ten seconds) with 6 agents: 0.3 CPU cores, harmless alone, but all of it on the UI thread.
- **File watchers** fired about 12 times in ten seconds per watched folder while cards were being touched. Each firing restarts a debounce, so they are cheap; the reload they lead to is what costs.
- **Memory**: working set 146 to 348 MB over the run, GC heap 0 to 132 MB. Whether the real instance keeps climbing needs a day of perf.log.

### Hotspots, in the order to fix them

1. **Board.Reload and Agents.RefreshCards on the UI thread**, called from every `TurnFinished`: read the files off the thread and apply the result once; stop `RefreshCards` calling `Reload` back.
2. **Transcript not virtualised**: use a virtualising list, or cap and page the rendered entries, so a switch builds only what is on screen.
3. **Notes.Reload and Reports.Reload**: read off the UI thread, and only the files that changed.
4. **The 10 s stall at `Agent.Begin`**: read its stack from the next stalls.log.

Nothing in the app was changed for these beyond the logging; each is a card of its own.

## Second trace

Taken 2026-11-25 (WOR-0223) from Philip's real `stalls.log` and `perf.log` (and `perf.old.log`, which perf.log rolls into) from 21 Nov 00:00 to 25 Nov 16:00, now with UI-thread stacks, plus one load run for the transcript. No app code was changed. The section times below are **self time**: a section's time less the named sections inside it, so nested ones (`Board.Reload > Agents.RefreshCards > Board.Reload`) aren't counted twice. `analyze.ps1` adds up only the top-level sections, so its totals differ.

### Against the first trace

The first trace's figures (456 stalls, 206 s) were taken partway through 20 Nov; the same log read in full for 17 to 20 Nov gives the left column.

| | 17 to 20 Nov (4 days) | 21 Nov to 25 Nov 16:00 (4.7 days) |
|---|---|---|
| Stalls | 529 (132 a day) | 698 (150 a day) |
| Time frozen | 585 s (146 s a day) | 517 s (111 s a day) |
| Over 1 s | 77 (19 a day) | 96 (21 a day) |
| Median, longest | 405 ms, 30.9 s | 329 ms, 12.6 s |
| In no section | 390 s | 217 s |
| GC paused in stalls | 17.6 s | 31.2 s |

| Day | Stalls | Frozen | Over 1 s | Longest |
|---|---|---|---|---|
| 21 Nov | 48 | 52 s | 11 | 7.1 s |
| 22 Nov | 103 | 85 s | 15 | 11.3 s |
| 23 Nov | 158 | 183 s | 41 | 12.6 s |
| 24 Nov | 137 | 74 s | 17 | 8.1 s |
| 25 Nov (to 16:00) | 252 | 123 s | 12 | 10.6 s |

So the very long freezes are gone (nothing over 12.6 s, against 30.9 s), but the window still freezes for about two minutes a day, 20 times a day for over a second. The heavy hours are still the ones with many agents running (23 Nov 11:00 to 14:00, 25 Nov 00:00 and 14:00 to 16:00).

**What the stacks say.** 59 stalls were held long enough for a stack (240 s). In 54 of them (216 s) the UI thread was waiting on a file open or read (`CreateFile`, `ReadFile`, `NtQueryDirectoryFile`), and in nearly all of them Darg was reading a whole folder: every report, every card on every board, every artifact. Those folders grow every day (409 reports and 307 cards in this project now), and each open is probably slower still while parallel builds and test suites keep the disk busy (the worst hours line up with them, but nothing in the logs measures the disk). That is why Darg gets slower the longer it is used: the top five hotspots below are all the same mistake, a whole folder read on the UI thread.

**Memory over a day.** No leak. The two longest stretches without a restart or a working-set trim: 21 Nov 01:07 to 22 Nov 03:48 (26.7 h), working set flat at about 478 MB from 04:00 on, GC heap 271 MB; 24 Nov 01:17 to 22:07 (20.8 h), working set 65 MB after a trim, 593 MB by 04:17, flat, then 707 MB from 13:17 (max 771 MB), GC heap 93, 394, then 428 MB (max 492 MB), 101 gen2 collections. It steps up with use and stays flat when idle. The heap is three times the first load run's 132 MB, though, and GC pauses inside stalls doubled (31 s); the transcript (below) is the likely cause.

### The first trace's four hotspots now

1. **Board.Reload and Agents.RefreshCards: still there, worse.** About 100 s (82 s named over 321 stalls, plus 18 s of board-file reads outside any section), worst 6.0 s (a `TurnFinished`); it was 36 s. `RefreshCards` reads every card file on every board twice (`CardBoards`, `DoneCardIds`), and each agent it forgets calls `Board.AgentGone`, which reloads every row, which runs `RefreshCards` again: the log shows `Board.Reload > Agents.RefreshCards > Board.Reload` nested up to 10 deep (22 Nov 21:44, 4.7 s).
2. **Transcript not virtualised: still there, worse.** The real log can't separate it out: it sits in the 217 s in no section, where stalls under a second get no stack. A load run on 25 Nov (`load.ps1` instance, 4 fake chat agents of about 500 transcript entries each, switching between them 12 times) froze the window **1.6 to 2.7 s per switch**, about 40% of it GC; the GC heap went from 68 to 453 MB, with 18 gen2 collections and 7.1 s of GC pause in that one minute. It was 0.6 to 1 s per switch with the first trace's shorter transcripts.
3. **Notes.Reload and Reports.Reload: still there, Reports worse.** `Notes.Reload` 29.1 s (24 s before), `Reports.Reload` 33.4 s (12 s before), and the same whole-folder report read now also runs from starting work and from the API's `card` (hotspot 2 below).
4. **The 10 s stall at `Agent.Begin`: has not recurred, and has a likely cause.** `Agent.Begin`'s worst since 21 Nov is 157 ms. Seven stalls on 17 to 20 Nov held 10.0 to 10.3 s "in no section", five of them right after `AgentSession.Start`, which matches `AgentStore.Flush`'s `Task.Wait` of 10 s; `AgentStore.List` and `Delete` both wait that way. The same wait has a stack now: 25 Nov 14:46, 8.2 s, `MascotViewModel.Retitle > ListChats > AgentStore.List > AgentStore.Flush > Task.Wait`. Likely why it runs to the timeout (from the code, not reproduced): `WriteWaiting` loops until nothing is waiting, so while agents keep saving, the writer task never finishes.

### Hotspots, ranked by time frozen

| # | Hotspot | Frozen, 21 to 25 Nov | Worst | Stack | Suggested fix | Card |
|---|---|---|---|---|---|---|
| 1 | Board reload chain: `Board.Reload`, `BoardStore.Load`, `Agents.RefreshCards`, `Board.Apply` | ~100 s, 321 stalls | 6.0 s | `BoardViewModel.Reload > BoardStore.Load > ReadCards > ReadFile`; also `AgentViewModel.set_State > BoardViewModel.ReloadAll` | Read the boards off the UI thread once per change and apply the result; let `RefreshCards` use them; stop `AgentGone` reloading every row from inside `RefreshCards` | WOR-0227 |
| 2 | Every report read to find one card's: `CardPrompt` (Work on), `Reports.Reload`, API `card` | ~96 s (40.7 + 33.4 + 22.2) | 11.3 s | `CardIndex.ReportsFor > ReportStore.List > File.ReadAllText`, from `BlockedStart.PromptLines`, `MainViewModel.OneCard`, `ReportsViewModel.Reload` | Read only the card's own files (the id is in the name); reload the Reports list off the UI thread, changed files only | WOR-0228 |
| 3 | `Board.BlockIndex` (new) | 53.6 s, 270 stalls | 10.6 s | `BoardViewModel.BlockIndex > CardIndex.Read > BoardStore.Load > ReadCards > CreateFile` | Build it from the boards already read, or update only the board that changed; never read card files for it on the UI thread | WOR-0229 |
| 4 | `Artifacts.Refresh` (new) | 34.8 s, 17 stalls | 7.2 s | `ArtifactsViewModel.Refresh > ArtifactStore.List > NtQueryDirectoryFile` | List the artifacts off the UI thread | WOR-0231 |
| 5 | `Notes.Reload` | 29.1 s, 31 stalls | 4.0 s | `NotesViewModel.Reload > NoteStore.TicketStatuses > BoardStore.ReadCards`; `NoteStore.SyncArchive > NoteStore.List` | Reload off the UI thread; on a board change (`Watch.NotesBoards` fired 2,633 times) reread only the ticketed cards | WOR-0231 |
| 6 | Agent transcript not virtualised | not separable; 1.6 to 2.7 s a switch in the load run | 2.7 s | `ContentPresenter.UpdateChild`, `TemplateContent.Load`, style matching under a `StackPanel` measure | A virtualising panel, or the newest entries with "show earlier" | WOR-0230 |
| 7 | `Agents.Start` | 9.0 s, 83 stalls | 1.6 s | none (all under a second) | Add sections inside it to see what costs 100 to 600 ms a start, then move that off the UI thread | |
| 8 | `AgentStore.List` / `Delete` waiting on the writer | 8.2 s now; about 70 s on 17 to 20 Nov | 10.3 s | `AgentStore.List > Flush > Task.Wait` | Merge waiting records into `List` from memory; drop a waiting save in `Delete` without waiting | WOR-0232 |
| 9 | `gh` run on the UI thread when Setup opens (new) | 5.0 s, 2 stalls | 3.8 s | `ProjectSetup..ctor > GitHub.Accounts > GitHub.Run > WaitHandle` | Load the accounts in the background | |
| 10 | Smaller: `Home.Glance` 3.7 s, `ImagePaths.Find` on a tool result 2.8 s, `BoardStore.Move` 2.4 s, `PlanStore.Edit` 1.5 s | 10 s | 2.8 s | file reads and writes, as above | Off the UI thread when each is next touched | |

The transcript is ranked by what could be measured; with long-running agents, switching between them twenty times a day would be 40 s or more, so it may well belong higher. Hotspots 1, 2 and 3 are about 250 s of the 517 s, and none of the top five needs anything new: each is a folder read moved off the UI thread, or not done at all.
