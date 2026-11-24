# WOR-0139: API new-card command

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

From the WOR-0135 investigation (see its Findings section). Making a card is Fettle's most common job (about 23 cards in 18 turns) and costs it 4 to 6 calls each: read board.json, list items, copy an existing card's shape, write the file, then edit or sed board.json. Hooks blocked its Python or sed three times. Its cards also skip the story-board placement a note's ticket gets.

**Want**
- `new-card {project, title, description?, type?, labels?, priority?}`: `BoardStore.Create` on the project's inbox, then `StoryBoards.Place` as a note's ticket does. Returns id, board and file path.
- Fettle's briefing says to use it; the card-format paragraph stays only as a fallback.

**Done when**
- One API call makes a well-formed card in the backlog and returns its id (integration test through `app.Api`).

Effort: about 1.5 h.

## How to check

**Changed:** the Darg API has `new-card` (`src/Darg.Desktop/ViewModels/MainViewModel.Api.cs`, `NewCard`). It makes the card on the project's inbox board with `BoardStore.Create`. The type is guessed from the words when none is given, and a bad type is refused. Labels can be a list or a comma-separated string. Haiku then places the card through `PlaceLater`, the same path + Card and a note's ticket use. It returns `{id, title, type, board, file, placing}`. Fettle's briefing now says to use it, and the hand-written card format stays as the fallback. Commit `1290a59` on main (branch `wor-0139`, merged and deleted). Tests: `tests/Darg.Desktop.Tests/NewCardTests.cs` (4 tests).

**Try it:** run Darg from main (⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal), then ask Fettle to "make a card for X".

**Look for:**
- Fettle makes the card in one `new-card` call, without reading board.json or writing files.
- The card shows in Main's backlog with the next WOR id.
- If the project has other story boards, a "Haiku put WOR-xxxx on …" notice may follow.

The tests ran this against fake agents. A real Fettle reading the new briefing, and real Haiku placing the card, were not watched live.
