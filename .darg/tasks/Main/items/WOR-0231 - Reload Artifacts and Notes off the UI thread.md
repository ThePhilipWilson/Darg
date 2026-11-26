# WOR-0231: Reload Artifacts and Notes off the UI thread

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-26 19:14:18

## Description

Hotspots 4 and 5 of the second trace (`docs/performance.md`, "Second trace" (WOR-0223)), the same pattern in two sections: a file watcher fires and the whole folder is read on the UI thread.

- `Artifacts.Refresh` held the window **34.8 s** between 21 and 25 Nov (17 stalls, worst 7.2 s, plus 5.5 s outside a section). The stack: `ArtifactsViewModel.Refresh > ArtifactStore.List`, listing directories and opening files. It wasn't in the first trace.
- `Notes.Reload` held it **29.1 s** (31 stalls, worst 4.0 s), against 24 s in the first trace. The stacks: `NoteStore.TicketStatuses > BoardStore.ReadCards` and `NoteStore.SyncArchive > NoteStore.List`. `Watch.NotesBoards` fired 2,633 times in perf.log, so every board change reloads the notes wall and rereads card files.

**Where:** `src/Darg.Desktop/ViewModels/ArtifactsViewModel.cs` (`Refresh`, `Reload`), `src/Darg.Desktop/ViewModels/NotesViewModel.cs` (`Reload`, `Changed`), `src/Darg.Core` (`ArtifactStore.List`, `NoteStore.List`, `NoteStore.SyncArchive`, `NoteStore.TicketStatuses`).

**Done when:**
- Both read their folders off the UI thread and apply the result in one go; the UI thread does no file reads in either reload.
- A board change rereads only the statuses of the cards that notes are ticketed to, not every card.
- The existing Notes and Artifacts tests pass, plus one per section that fails if the reload reads on the UI thread (for example through `StallWatch` or a file-read counter).
- A week of stalls.log after the fix shows no `Artifacts.Refresh` or `Notes.Reload` stall over 200 ms (noted in the doc's Second trace part).

## How to check

**What changed** (commit `4e76e82` on `android`, pushed): Artifacts and Notes now read their folders on the thread pool through `BackgroundRead` (Desktop) and show the result on the UI thread in one go. The notes wall reads in one call (`NoteStore.ReadWall`) and asks only for its tickets' card statuses, opening only the files named for those cards. The board watcher reloads the wall only when one of those cards' files changes. Home's artifact tile now uses the Artifacts section's last read instead of listing the folder every 3 s. `Artifacts.Show` opens a page that's already listed at once (Back/Forward relies on that) and reads the folder again only for a page not listed yet.

**Already checked:** the whole suite passes (969 unit, 287 integration). `OffThreadReloadTests` fails with the read put back on the UI thread, and its Notes test also fails with the board filter turned off.

**What's left to check:**
1. In the published copy, after ⟳ Update or `pwsh -File tools/publish.ps1` from a terminal:
   - Notes: make a ticket from a note, move that card to Done on the board, and see the note go to the archive within a second or so.
   - Artifacts: have an agent write a page and see it appear in Artifacts and on Home's tile.
   - Back/Forward between two artifacts.
2. The card's last Done when: from 26 Nov, a week of stalls.log with no `Artifacts.Refresh` or `Notes.Reload` stall over 200 ms. Check with `pwsh -File tools/perf/analyze.ps1 -Since 2026-11-26`, then note the result under "Hotspots 4 and 5, fixed (WOR-0231, 26 Nov)" in `docs/performance.md`. Those two section names now time only the showing, not the reading.
