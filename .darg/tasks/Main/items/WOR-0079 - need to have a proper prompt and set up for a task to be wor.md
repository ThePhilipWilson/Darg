# WOR-0079: need to have a proper prompt and set up for a task to be worked on fro

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-16
- **Done:**

## Description

From a note on the wall (20261116-235550-ea5b).

> need to have a proper prompt and set up for a task to be worked on from qa ready

WOR-0075 already made ▶ Work on, on a QA Ready card, get the work running to try. Philip picked the missing half for this card: sending a card back from QA with notes, so the rework agent gets a proper prompt.

## How to check

**What changed:** commit `2442118` on main (pushed). A card in QA Ready now has a "Tried it and it isn't right?" box and a ↩ Send back button in its panel. Send back writes your text into the card file as a dated entry under `## QA notes`, moves the card back to In Progress, and starts a card agent named "<id> <title> (sent back)". That agent's prompt opens with your notes and tells it to read How to check, build on that work, fix what the notes say, ask if a note is ambiguous, and replace How to check for the next round. In QA, the Work on button now reads "▶ Get it running to try". The right-click menu on a QA card also gains "↩ Send back with notes…" (it opens the panel). The API has a `send-back` command.

**Try it:** update Darg (⟳ menu, or `pwsh -File tools/publish.ps1` from a terminal), open this card (it's in QA Ready), type something into the box and press ↩ Send back. This card is a good one to try it on.

**Look for:**
- the agent's first message starts with your notes and the rework instructions
- the card file gets a `## QA notes` section and the card moves to In Progress
- the right-click menu item on a QA card (not checked: it's a popup, so screenshots don't capture it)
- whether the box and its wording sit well in the panel

Already checked live on a throwaway instance (no screenshots kept): the box appeared, the notes were written, the card moved, and the "(sent back)" agent started with the rework prompt.

## Automated QA

Verified: commit 2442118 is on main and on origin/main. Read the diff: `ReportStore.ReworkPreface` and `QaNotes` (Reports.cs), the Send back box and "Send back with notes…" menu item (BoardViewModel, BoardView.axaml), "▶ Get it running to try" in QA (`WorkOnText`), the `send-back` API command, and the prompt built as preface + card prompt (MainViewModel.cs). ReportStore/QaNotes tests pass (13). `dotnet build src/Darg.Desktop` has 0 errors. Full Core suite: 526 of 527 pass; the one failure is `TicketTitleTests.Retitling_a_short_note_keeps_its_words_at_the_top_of_the_description` (expected T-0001, got T-0002), ticket numbering, unrelated to this card.

Could not check: the live panel look and wording, the box, the right-click menu item (popup), and the agent's actual first message. These need a human with the running app.
