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
& D:\Apps\Darg\Darg.Desktop.exe   # or the debug build; use it as usual, then close it
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
