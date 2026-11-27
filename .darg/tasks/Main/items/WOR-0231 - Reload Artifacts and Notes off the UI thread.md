# WOR-0231: Reload Artifacts and Notes off the UI thread

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:** 2026-11-27
- **Moved:** 2026-11-27 12:05:39

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

## Decision answered 2026-11-27 12:04

**Question:** Everything QA could automate passed, but the week of stalls.log can't be judged yet: close this card now and track that week as its own card?
**Asked by:** agent (QA of WOR-0231), 2026-11-27
- **Done, ticket the week's check** (recommended): this card goes to done and a new card is made: "after a week on a published build with WOR-0231, run analyze.ps1 and note Notes.Reload / Artifacts.Refresh in performance.md"
- **Keep it here a week** → stay: the card waits until a week of stalls.log from a build with the fix exists
- **Done, skip the week's check** → done: the measured before/after (below) is enough

**Context:** The installed copy (D:\Apps\Darg, built 26 Nov 15:12 UTC) predates this work, and it writes the same stalls.log as the live copy, so nothing in that log can be credited to the fix until you ⟳ Update or publish. On a debug build at your wall's size the worst Notes.Reload stall fell from 587 ms to 58 ms.

**Answer:** Done, ticket the week's check (2026-11-27 12:04)

## How to check

**What changed** (commit `4e76e82` on `android`, pushed): Artifacts and Notes now read their folders on the thread pool through `BackgroundRead` (Desktop) and show the result on the UI thread in one go. The notes wall reads in one call (`NoteStore.ReadWall`) and asks only for its tickets' card statuses, opening only the files named for those cards. The board watcher reloads the wall only when one of those cards' files changes. Home's artifact tile now uses the Artifacts section's last read instead of listing the folder every 3 s. `Artifacts.Show` opens a page that's already listed at once (Back/Forward relies on that) and reads the folder again only for a page not listed yet.

**Already checked:** the whole suite passes (969 unit, 287 integration). `OffThreadReloadTests` fails with the read put back on the UI thread, and its Notes test also fails with the board filter turned off.

**What's left to check:**
1. In the published copy, after ⟳ Update or `pwsh -File tools/publish.ps1` from a terminal:
   - Notes: make a ticket from a note, move that card to Done on the board, and see the note go to the archive within a second or so.
   - Artifacts: have an agent write a page and see it appear in Artifacts and on Home's tile.
   - Back/Forward between two artifacts.
2. The card's last Done when: from 26 Nov, a week of stalls.log with no `Artifacts.Refresh` or `Notes.Reload` stall over 200 ms. Check with `pwsh -File tools/perf/analyze.ps1 -Since 2026-11-26`, then note the result under "Hotspots 4 and 5, fixed (WOR-0231, 26 Nov)" in `docs/performance.md`. Those two section names now time only the showing, not the reading.

## QA 2026-11-27

Checked at a80b868 (android, holds 4e76e82), in worktrees under `.shots/`, never the real instance.

- **Whole suite** (`pwsh -File tools/test.ps1`): unit 986/986, integration 303/304. The one failure is `PhoneBoardTests.A_tap_on_a_tile_opens_it_and_a_long_press_puts_up_its_move_sheet` ("Waited 30 s for: the tapped card to open"). It is a flake under load, not this card: it passed 3/3 run alone at a80b868 and 3/3 at c29744d (before this card), and it passed in an earlier full run at a80b868 the same morning. The machine was running other worktrees' suites at the time. It touches neither Notes nor Artifacts. Worth a card of its own.
- **Live, debug build, throwaway instance** (`.shots/qa-0231-evidence/qa0231-live.ps1`: 3000 cards, 300 artifact pages, `DARG_STALL_MS=50`, the same script on the pre-fix build c29744d for comparison):
  - At your wall's size (113 notes; the real `workbench` wall has 2 plus 111 archived): Notes.Reload worst stall **58 ms after, 587 ms before**. The ticketed card set to Done archived its note in **384 ms after, 9.4 s before**. Reopened, the note came back in 377 ms (4.6 s before). Artifacts.Refresh: **0 stalls after, 10 before**.
  - At 400 notes the showing alone still reaches 150 to 490 ms (stalls that also had 0.7 to 1.1 s of GC pause, with other suites running). Your wall is far smaller, so this doesn't touch the Done when, but a wall that size would still stall.
  - Home's Artifacts tile listed the five newest pages written while Notes was on screen (`.shots/qa-0231-home4/home.png`), so the tile now relies on the section's watcher and that works.
- **Headless scratch tests** (`.shots/qa-0231-evidence/QaWor0231NavTests.cs`, 4/4 passed): Back and Forward between two artifacts land at once and stay after the section's read lands, Home then Back reopens the page, a page just written opens through `Show`, a ticketed note made from the editor archives 381 ms after its card goes to Done and comes back when reopened, and a burst of 20 note saves with reloads ends with all 20 on the wall.
- Read the diff and `OffThreadReloadTests`. The test fails on any read on the UI thread, and the Notes test also checks that a change to a card no note is ticketed to reads nothing.

**Could not be checked by automation, and why:**
- *The published copy* (How to check step 1): `D:/Apps/Darg` was built on 26 Nov at 15:12 UTC, before this commit. Publishing from inside a Darg agent is refused (closing the installed copy kills the agent's host). The same steps ran on a debug build instead (above).
- *A week of stalls.log* (the last Done when): only one day has passed, and the installed copy (pre-fix) and the live copy (post-fix) both write to the `home` instance's `stalls.log`, which doesn't record which copy wrote a line. Since the commit it shows two Notes.Reload stalls (457 ms at 02:59 and 428 ms at 10:10 on 27 Nov). On a 2-note wall the post-fix code spends ~50 ms showing, so these most likely came from the pre-fix installed copy, but the log can't prove it. `docs/performance.md` isn't updated yet for the same reason.

## Closed 2026-11-27

Closed by Philip's answer above. The last Done when (a week of stalls.log on a build with the fix) moved to WOR-0247.
