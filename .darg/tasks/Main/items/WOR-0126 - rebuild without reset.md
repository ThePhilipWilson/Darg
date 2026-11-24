# WOR-0126: rebuild without reset

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

From a note on the wall (20261117-221222-1ead).

Asked (in chat): run updates and have them apply without restarting. The installed copy can't (the SDK turns hot reload off in Release builds, and it needs dotnet watch beside the app), so Philip picked the experiment: hot reload in a dev copy.

## How to check

What changed (commit `b280306` on `main`, ThePhilipWilson/Darg): `tools/live.ps1` runs Darg under `dotnet watch` from its own worktree (`.claude/worktrees/live`, detached at `main`). In that copy the ⟳ menu reads **⟳ live** and has **Pull in N commits from main**, which moves the worktree to main's newest commit. C# changes then apply through .NET hot reload and `.axaml` changes through HotAvalonia (Debug-only, live builds only). Restart goes back through live.ps1; closing Darg closes the dotnet watch window.

1. Close the installed Darg: live.ps1 refuses while it runs on the same instance, since two copies would fight over agents. Closing it stops agents mid-turn, so pick a quiet moment.
2. From a terminal: `pwsh -File D:\GitHub\ThePhilipWilson\Workbench\tools\live.ps1`. A "Darg (live)" console window opens showing dotnet watch. The first build takes a minute, then Darg opens on your real instance. (`-Shortcut` instead makes "Darg (live)" Start menu and desktop shortcuts.)
3. Use it as normal. When an agent merges something to main, open ⟳ live: it says main has moved on. Press Pull in. The console says "C# and Razor changes applied", or for XAML HotAvalonia reloads the view. The change should show without the window closing.
4. Look for:
   - Does a real change (an agent's merged card) show without a restart?
   - When hot reload can't take an edit, the console says "Restart is needed" and Darg closes and reopens by itself. That stops agents mid-turn. How often does that happen in your use?
   - A view other than the main window whose code-behind wires its named parts in the constructor can lose that wiring after a XAML reload, until a restart. Only MainWindow has been moved to `InitializeComponentState` so far.
   - Does the Debug build feel slower to use than the installed Release copy?
5. To go back: close Darg (the console goes too) and start the installed copy as usual.

Screenshot of the live copy on a throwaway instance: `D:\GitHub\ThePhilipWilson\Workbench\.shots\wor-0126-live-header.png`.

## QA findings (2026-11-17)

Checked by automation, all passed: Core and Desktop build; 558 of 559 tests pass (the one failure is the unrelated TicketTitle test), LiveCopyTests 3/3. End to end on a throwaway instance (DARG_HOME=.shots/home, scratch branch, scratch worktree, `live.ps1 -Here`): the live copy built and opened under dotnet watch; a commit changing MainWindow.axaml and RestartViewModel.cs, moved into the worktree the way PullIn does (`git checkout --detach`), gave "C# and Razor changes applied in 1656ms" with the same process still running (same PID, same start time), and UI Automation showed the restart button's tooltip changed from the .axaml edit. Closing the window ended dotnet watch and left no processes. Philip's own Darg was already running as a live copy during this QA.

Needs Philip's judgement, and why it can't be automated:
- "Does the Debug build feel slower than the installed Release copy?" is a feel question; stalls.log could give numbers but whether it is acceptable is his call.
- "How often does hot reload say Restart is needed in real use?" only shows up over days of real edits, not in one scripted run.
- Whether a real merged card shows without a restart: I could not press the real "Pull in" button (a hot-reload edit of a throwaway branch stood in for it), and a real card's mix of edits is unpredictable.
- Views other than MainWindow whose code-behind wires named parts in the constructor lose that wiring after an XAML reload: known, not exercised here (would need an XAML edit to each such view; worth a ticket to move them to `InitializeComponentState`).

