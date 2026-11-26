# WOR-0231: Reload Artifacts and Notes off the UI thread

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

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
