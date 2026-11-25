# WOR-0217: Story cards should link out to the task cards they contain

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-25
- **Done:**

## Description

> Story tickets currently look like a tag, with no functional difference from other cards. They should link out to the task cards they contain (e.g. list the contained cards on the story card, each clickable to open it), so a story is visibly and usefully a container.

**What's wanted:** a card of Type Story shows the cards it contains, each one a link that opens that card, so a story reads as a container. Today a story's `Cards: WOR-0100, WOR-0101` line is plain text in its description (written by the wrap-up in `Stories.Prompt`), and each wrapped card has a `Story: <id> <name>` line.

**Where:**
- `src/Darg.Desktop/Views/BoardView.axaml` (the card panel; the Blocked by / Blocks link lists near line 601 are the pattern) and `src/Darg.Desktop/ViewModels/BoardViewModel.Blockers.cs` (`CardEditor.InitBlockers`, `BlockerLink`: reuse for a "Contains" list, opened with `_board.Root.OpenById`).
- `src/Darg.Core/Board.cs` (`BoardCard.IsStory`) and `src/Darg.Core/Stories.cs` (the `Cards:` line's format); read ids from that line and also find cards whose `Story:` line names this story's id, resolved across boards with `CardIndex`.
- A story's tile on the board could show a count (e.g. "4 cards"); optional.

**Done when:**
- Opening a Story card's panel lists its contained cards, each with id, title and column, and clicking one opens that card.
- A card named in `Cards:` that no board has is shown as not found, not dropped.
- A non-story card's panel is unchanged.
- An integration test in `tests/Darg.Desktop.Tests` (fails without the change) makes a story with two cards, opens it and checks both links; `pwsh -File tools/test.ps1` passes.

**Questions left:** none blocking; the count on the tile is the only extra.
