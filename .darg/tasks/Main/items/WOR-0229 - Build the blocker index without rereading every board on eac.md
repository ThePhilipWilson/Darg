# WOR-0229: Build the blocker index without rereading every board on each reload

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:** 2026-11-26
- **Moved:** 2026-11-26 20:03:13

## Description

Hotspot 3 of the second trace (`docs/performance.md`, "Second trace" (WOR-0223)): `Board.BlockIndex` held the window **53.6 s between 21 and 25 Nov** over 270 stalls, worst 10.6 s (25 Nov 00:33, inside `Board.Apply`). It wasn't in the first trace: the blockers feature (WOR-0172) came after it.

Every board reload calls `BlockersStale()`, so the next `Apply` runs `BlockIndex()`, which calls `CardIndex.Read`, which opens every card file on every board of the project on the UI thread (the stack: `CardIndex.Read > BoardStore.Load > BoardStore.ReadCards > CreateFile`). With agents finishing all the time, that happens on nearly every reload.

**Where:** `src/Darg.Desktop/ViewModels/BoardViewModel.Blockers.cs` (`BlockIndex`, `BlockersStale`), `src/Darg.Desktop/ViewModels/BoardViewModel.cs` (`Reload`, `Apply`), `src/Darg.Core` (`CardIndex.Read`, `Blockers.Index`).

**Done when:**
- The index is built from the boards already read, or updated only for the board that changed, and never by reading card files on the UI thread.
- Blocked-by chips still update when a blocker moves on a board that isn't showing (the WOR-0172 behaviour), and the existing blocker tests pass.
- A test checks that reloading one board doesn't reread the other boards' card files.
- `tools/perf/load.ps1 -Cards 6 -StallMs 100` shows no `Board.BlockIndex` over 50 ms, and the doc's Second trace part notes the result.
