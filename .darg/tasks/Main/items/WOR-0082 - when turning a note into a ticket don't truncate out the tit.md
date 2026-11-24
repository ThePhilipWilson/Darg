# WOR-0082: when turning a note into a ticket don't truncate out the title

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

if there is any additional detail it should go into the body here. also it should be getting a type at this point to. experiment with a tiny local model for turning postits into tickets
From a note on the wall (20261117-000819-bd70).

## How to check

**What changed** (main, pushed; commits `226387e`, `b92171c`):
- A long first line is no longer cut at 70 characters with the rest thrown away. The title is the first sentence if that's short, or the first words and "…", and the description keeps every word of the note (`NoteStore.TicketText`).
- Type is guessed from prose bugs too: "got a bug with", "had a bug where", "fix …", broken, crashes, not working, wrong, hanging, cut off (`CardTypes.Guess`).
- Then Haiku retitles the card in the background, 10 to 20 s later (`TicketTitle`, your pick). A reply that isn't one short line in the note's own words is refused and the first title stays. If you rename the card before it arrives, yours wins.
- Tiny local models were tried and rejected: qwen3:1.7b titled 25 of 82 notes "bug"; mistral 7B made up things like "broken link in marketing page"; gemma3:1b won't run on your Ollama 0.24.

**Try it:** update the installed copy first (header ⟳ → Update, or `pwsh -File tools/publish.ps1` from a terminal).
1. On the wall, write a long one-line post-it, e.g. `had a bug where the board didn't refresh after I made a ticket and I had to leave and come back before the link would open it`.
2. Open it and press the make-a-ticket button in its panel. This path was **not** exercised live; Pick all → Make N tickets was.
3. Look for: the card appears at once with a title ending "…" and type **Bug**, and the whole line in its description. Within about 20 s the title changes to a short Haiku one and the file is renamed to match.
4. Also try a short note (e.g. `fix logo aliasing`). If Haiku rewords it, the note's own line should now sit at the top of the description.

No screenshot kept (it went with the worktree). The live run on a throwaway wall gave T-0001 "smart generate boards that organise the backlog for me" (Task) and T-0002 "Performance: hangs on starting multiple tickets" (Bug).

## Automated QA

**Verified:** commits `226387e` and `b92171c` exist and are on main (also on wor-0083). Read the diffs: `NoteStore.TicketText`, `CardTypes.Guess`, `TicketTitle` (Clean/Retitle) and the `NotesViewModel` wiring are present as described. `dotnet test tests/Darg.Core.Tests`: 526 of 527 pass, including the TicketText, type-guess and `TicketTitle.Clean` refusal tests.

**Failed:** `TicketTitleTests.Retitling_a_short_note_keeps_its_words_at_the_top_of_the_description` fails every run (expected `T-0001 - white text on post-its.md`, got `T-0002 - ...`). Cause: since WOR-0087 (`7ff9878`), `BoardStore.HighestUsed` scans sibling folders of a board for boards with the same prefix, and these tests put their boards directly in the temp dir with prefix `T`, so any leftover `T` board there bumps the id. The feature isn't wrong; the test isn't isolated. Fix: put the test board in its own subfolder (e.g. `_dir/board`) or use a unique prefix. Other tests in that file using `Board()` may be sensitive the same way.

**Not checked (needs the live app / Haiku):** the note panel's make-a-ticket path, the live Haiku retitle after about 20 s and file rename, and the short-note case.
