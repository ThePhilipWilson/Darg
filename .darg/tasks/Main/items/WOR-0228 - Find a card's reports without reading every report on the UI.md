# WOR-0228: Find a card's reports without reading every report on the UI thread

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:** 2026-11-27
- **Moved:** 2026-11-27 11:10:56

## Description

Hotspot 2 of the second trace (`docs/performance.md`, "Second trace" (WOR-0223)): reading the reports folder on the UI thread held the window **about 96 s between 21 and 25 Nov**: `CardPrompt` 40.7 s (worst 9.7 s, starting work on a card), `Reports.Reload` 33.4 s (worst 6.6 s) and the API's `card` command 22.2 s (worst 11.3 s). In the first trace `Reports.Reload` was 12 s.

The stacks show `CardIndex.ReportsFor` calling `ReportStore.List`, which opens and parses every report in the folder (409 in this project now, and growing every day) to find one card's. It runs from `CardPrompt` (through `BlockedStart.PromptLines`, for each blocker), from `MainViewModel.OneCard`, and from `ReportsViewModel.Reload` on each watcher event and `SettledFromBoard`. This is part of why Darg gets slower the longer it is used.

**Where:** `src/Darg.Core` (`ReportStore.List`, `CardIndex.ReportsFor`, `BlockedStart.PromptLines`), `src/Darg.Desktop/ViewModels/ReportsViewModel.cs` (`Reload`, `SettledFromBoard`), `src/Darg.Desktop/ViewModels/MainViewModel.cs` (`CardPrompt`, `OneCard`).

**Done when:**
- `ReportsFor` reads only the files for that card (report names carry the card id: `<stamp>-<card>.md` and `-qa.md`, archive included), not the whole folder.
- `ReportsViewModel.Reload` reads off the UI thread and rereads only the files that changed.
- A unit test checks `ReportsFor` returns the same reports as before, archived ones included, without opening other cards' files.
- Starting work on a card with blockers in a folder of 400 reports holds the UI under 200 ms (`tools/perf/load.ps1` or a test with that many reports).
