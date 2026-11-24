# WOR-0116: Don't show Work on for a card in Done

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

A card in the Done column still offers Work on (and Improve), because `CardViewModel.CanWorkOn` in `BoardViewModel.cs` is only `_card is not null && !HasAgent`. Starting an agent on a finished card is never what's wanted.

**Wanted:** no Work on, Improve or Run QA on a card in Done, in its panel, on the card, in the context menu and in the picked-cards "▶ Work on N" count. To redo a finished card, move it out of Done first.

## Where

- `src/Darg.Desktop/ViewModels/BoardViewModel.cs`: `CanWorkOn` (~line 1213), which `CanSendBack` and `CanQaVerdict` also build on; `ToStart` (~line 389) for picked cards.
- `Board.IsDone` already exists (used for `Agents.CardIsDone`), so use it rather than a new check.

## Done when

- A card in Done shows no Work on or Improve button, and picking it adds nothing to "▶ Work on N".
- Moving it back out of Done brings the buttons back.
- The API's `work-on` refuses a card in Done with a clear message.

## How to check

- **Changed:** `CardViewModel.InDone` / `CanStart` and `CardEditor.CanWorkOn` / `CanRunQa` in `BoardViewModel.cs`, the card tile's Work on binding in `BoardView.axaml`, the context menu, picked counts, and the API's `work-on` / `improve-card` (now refuse Done cards with a message). Commits `b70a1dd`, `f96caff` on main.
- **Try:** `pwsh -File tools/publish.ps1` (or build and run the Debug exe with `DARG_HOME=.shots/home`), open a board with a card in Done.
- **Look for:** no Work on / Run QA / Improve / flows on its tile, right-click menu or panel; picking it leaves "▶ Work on N" and "✦ Improve N" unchanged; drag it out of Done and the buttons return (the board reloads on move).

## QA report (2026-11-17)

- Passed: dotnet build (0 errors); dotnet test 545 of 546 (the one failure is the TicketTitle naming test, unrelated and already known). Read both commits: tile, panel, Send back, QA verdict, flow targets, picked count and Work on / Improve picked all use CanStart / InDone; the API's work-on and improve-card skip Done cards and return the new message.
- QA agent check (2026-11-17): build 0 errors; tests 548 of 549 (only the unrelated TicketTitle numbering test fails). Re-read both commits and the call sites: tile, picked counts, Work on/Improve picked, context menu, panel, flow targets, Send back/QA verdict and the API all gate on CanStart/InDone. App not run; the on-screen behaviour needs Philip's look.
- Not checked: I didn't run the app, so the tile, right-click menu and panel in Done, and the buttons returning after a drag out, are unseen. InDone is set when the card view model is built, so it relies on the board reloading on move (Move does call Reload()).
