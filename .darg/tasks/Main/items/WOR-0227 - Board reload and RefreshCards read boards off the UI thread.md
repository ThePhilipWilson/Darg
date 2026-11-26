# WOR-0227: Board reload and RefreshCards: read boards off the UI thread and stop the re-entry

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-26 19:31:15

## Description

Hotspot 1 of the second trace (`docs/performance.md`, "Second trace" (WOR-0223)): the board reload chain held the window **about 100 s between 21 and 25 Nov** (82 s in named sections over 321 stalls, about 18 s more reading board files outside any section), worst 6.0 s. In the first trace it was 36 s, so this one has got worse.

`Board.Reload` reads the board on the UI thread, then `Board.Reloaded` runs `Agents.RefreshCards`, which reads every card file of every board twice (`Board.CardBoards` and `Board.DoneCardIds`). Each agent it forgets calls `Board.AgentGone`, which reloads every row, which runs `RefreshCards` again: stalls.log shows `Board.Reload > Agents.RefreshCards > Board.Reload` up to 10 levels deep, one per agent forgotten. Every `TurnFinished`, `SessionStarted` and agent state change sets this off.

**Where:** `src/Darg.Desktop/ViewModels/BoardViewModel.cs` (`Reload`, `AgentGone`, `CardBoards`, `DoneCards`, `DoneCardIds`), `src/Darg.Desktop/ViewModels/AgentsViewModel.cs` (`RefreshCards`, `Forget`), the wiring in `src/Darg.Desktop/ViewModels/MainViewModel.cs` (`Board.Reloaded += Agents.RefreshCards`, `Agents.Forgotten`).

**Done when:**
- The board files are read off the UI thread and the result is applied in one go; the UI thread does no file reads in `Board.Reload` or `RefreshCards`.
- `RefreshCards` uses the boards already read rather than reading every card file again, and forgetting agents no longer re-enters `Board.Reload` (several agents forgotten in one pass cause one reload).
- An integration test with several agents finishing at once checks the reload is not re-entered (it fails on the old code).
- `pwsh -File tools/perf/load.ps1 -Cards 6 -StallMs 100` shows no `Board.Reload > Agents.RefreshCards > Board.Reload` line, and the doc's Second trace part notes the result.

## How to check

**What changed** (commit `1418d64` on `android`, https://github.com/ThePhilipWilson/Darg/commit/1418d64): a board reload that anything but your own click asks for (the board watcher, an agent's state change, the API's move-card / new-card) is now one pass for the whole Board section: every board read once on a pool thread, all rows applied together, `Reloaded` raised once. `RefreshCards` and the each-card's-board / done-cards / cut-off lookups answer from that read (`ProjectBoards` in Core) and read no file. A forgotten agent only redraws the tiles from memory (once per burst), so `RefreshCards` never reloads the board inside itself. A click that writes a board (drag a card, rename or add a column, Send back) still reads that one board on the UI thread so it shows at once, then hands the rest to a pass.

**Try it** (after ⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal):
1. Start Work All or QA Run All on 4 or more cards, with the Board showing. When the turns end, the window shouldn't freeze. Tiles drop their agent, cards that agents move show up in their new column within a moment, and the agents leave the list.
2. Drag a card between columns and between board rows. It should land at once with no snap back. Rename a column while agents are working: the half-typed name should survive the reloads.
3. Leave Darg running for a day as usual, then `pwsh -File tools/perf/analyze.ps1 -Since '2026-11-27'`. There should be no `Board.Reload > Agents.RefreshCards > Board.Reload` in stalls.log and much less time in `Board.Reload`. The click path now shows as `Board.Reload (click)`.

**Look for:** a board that shows a stale card position for more than about half a second after an agent or a text editor changes it; a card's agent link (Work on, ⏹) not updating after its agent ends.

**Checked by the agent:** the full suite passes (unit 971, integration 293). The new `BoardReloadTests` QA test fails on the old code every time; the work-agents test failed on the old code on one of two runs. `load.ps1 -Cards 6 -StallMs 100` on a 245-card copy of the Main board: old build 874 ms stall nested three deep as the six turns ended, new build 113 and 252 ms with no nesting (docs/performance.md, "Hotspot 1, fixed"). **Not checked:** interactive use in the real app (drags, renames, the feel while agents run). Hotspot 3 (`Board.BlockIndex`, still a UI-thread read inside each redraw) is WOR-0229's.
