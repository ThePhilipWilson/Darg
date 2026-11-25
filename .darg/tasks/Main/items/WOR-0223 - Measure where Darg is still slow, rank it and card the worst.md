# WOR-0223: Measure where Darg is still slow, rank it and card the worst

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

## Description

> Follow-up to WOR-0161. Some areas are noticeably faster since that work, but the app still feels slow overall. Use the profiling from WOR-0161 (and its report) to find the remaining slow spots: which views, actions or background work (rendering, IPC, file watching, startup, memory) still lag under typical use. Done when: the remaining hotspots are measured and ranked, each with a suggested fix, and the worst ones are made into cards.

A second trace, not a fix: read the logs Darg has been writing since WOR-0161 shipped, find what still freezes or lags the window in everyday use, rank it, and turn the worst into cards. The Claude side (agent and Fettle turns slow to start or finish) is WOR-0224's, not this card's.

WOR-0161's write-up (`docs/performance.md`, "Hotspots, in the order to fix them") named four, said each was "a card of its own", and no card was made for any of them. As of 2026-11-25 the code suggests all four are still there: `BoardViewModel.Reload` still reads the board on the UI thread and `Board.Reloaded` still calls `Agents.RefreshCards`; Notes and Reports still reload on the UI thread. Only board switching was fixed since (WOR-0195, virtualised columns).

**Where:**
- The real logs: `%APPDATA%\Darg\home\stalls.log`, `perf.log` and `agents.log` (all present and current).
- Reading them: `pwsh -File tools/perf/analyze.ps1 [-Since '2026-11-21']`; a repeatable run on a throwaway instance: `tools/perf/load.ps1`, `tools/perf/gpu.ps1`.
- The four known spots: `BoardViewModel.Reload` (`src/Darg.Desktop/ViewModels/BoardViewModel.cs`) and `AgentsViewModel.RefreshCards` wired in `MainViewModel.cs`; the agent transcript in the Agents view (not virtualised); `NotesViewModel` and `ReportsViewModel` reloads; the ~10 s stall at `Agent.Begin`.
- Write-up: a new "Second trace" part in `docs/performance.md`.

**Done when:**
- `docs/performance.md` has a "Second trace" part from the real logs since 2026-11-21: stalls per day, total time frozen, the longest, and memory over a day, against the first trace's numbers.
- Each of WOR-0161's four hotspots is marked there as still there (with its numbers now) or gone.
- Any new hotspot found is named, with a UI-thread stack from stalls.log where one exists, and the 10 s `Agent.Begin` stall either has a cause or is said to have not recurred.
- The hotspots are ranked by time frozen, each with a one-line suggested fix.
- The worst ones (at least the top three) are cards on this board, each scoring 3/3 and linking the doc.
- No app code is changed.

**Questions:** none.
