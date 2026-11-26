# WOR-0227: Board reload and RefreshCards: read boards off the UI thread and stop the re-entry

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

## Description

Hotspot 1 of the second trace (`docs/performance.md`, "Second trace" (WOR-0223)): the board reload chain held the window **about 100 s between 21 and 25 Nov** (82 s in named sections over 321 stalls, about 18 s more reading board files outside any section), worst 6.0 s. In the first trace it was 36 s, so this one has got worse.

`Board.Reload` reads the board on the UI thread, then `Board.Reloaded` runs `Agents.RefreshCards`, which reads every card file of every board twice (`Board.CardBoards` and `Board.DoneCardIds`). Each agent it forgets calls `Board.AgentGone`, which reloads every row, which runs `RefreshCards` again: stalls.log shows `Board.Reload > Agents.RefreshCards > Board.Reload` up to 10 levels deep, one per agent forgotten. Every `TurnFinished`, `SessionStarted` and agent state change sets this off.

**Where:** `src/Darg.Desktop/ViewModels/BoardViewModel.cs` (`Reload`, `AgentGone`, `CardBoards`, `DoneCards`, `DoneCardIds`), `src/Darg.Desktop/ViewModels/AgentsViewModel.cs` (`RefreshCards`, `Forget`), the wiring in `src/Darg.Desktop/ViewModels/MainViewModel.cs` (`Board.Reloaded += Agents.RefreshCards`, `Agents.Forgotten`).

**Done when:**
- The board files are read off the UI thread and the result is applied in one go; the UI thread does no file reads in `Board.Reload` or `RefreshCards`.
- `RefreshCards` uses the boards already read rather than reading every card file again, and forgetting agents no longer re-enters `Board.Reload` (several agents forgotten in one pass cause one reload).
- An integration test with several agents finishing at once checks the reload is not re-entered (it fails on the old code).
- `pwsh -File tools/perf/load.ps1 -Cards 6 -StallMs 100` shows no `Board.Reload > Agents.RefreshCards > Board.Reload` line, and the doc's Second trace part notes the result.
