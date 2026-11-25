# WOR-0216: Show why a card is in Needs more work at the top of its panel

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 14:11:38

## Description

> When a card is in Needs more work (sent back from QA), the reason is buried in the QA notes at the bottom of the card, so finding out why it was sent back means reading the whole file. Show the reason at the top of the card's inspect panel: a short summary of the latest QA notes (or the send-back notes) above the description, visible whenever the card is in that column. Link or scroll to the full QA notes section.

Show the latest send-back reason at the top of the card panel whenever the card is in the Needs more work column, so nobody has to read the whole file to learn why it came back.

**Where:** the card panel in `src/Darg.Desktop/Views/BoardView.axaml`, next to the Decision box (about line 511, bound to `ShowDecision`) and above the description; its state in `src/Darg.Desktop/ViewModels/BoardViewModel.cs` (the check at ~line 1232 says a card is in `BoardColumns.Rework`). The text comes from the card's `## QA notes` section: `CardIndex.QaNotesOf` (`src/Darg.Core/CardIndex.cs`) reads it, `QaNotes` (`src/Darg.Core/Reports.cs`) writes its dated `###` entries.

**Done when:**
- A card in Needs more work with QA notes shows a box above its description holding the newest dated entry (long text folded), with its date.
- A "Full QA notes" link in the box scrolls the panel to the `## QA notes` section.
- A card in any other column, or with no QA notes, shows no box.
- After ↩ Send back with new notes, the box shows the new entry.
- An integration test in `tests/Darg.Desktop.Tests` fails without the change (send back, open the panel, see the box).

**Questions:** "short summary" is taken as the newest entry shown as written (folded), not a model-made summary. Say if you want a Haiku summary instead.

## How to check

Changed: `QaNotes.Latest` (Core) and `CardEditor.SendBack*` plus BoardView.axaml's "Why it was sent back" box. Branch `android` (merged from `wor-0216`), commit 7efd3cd.

Try: open a card in Needs more work that has a `## QA notes` section (or send one back from QA with Send back, then move it to Needs more work). Its panel shows an amber-bordered box above the description with the date, the newest entry (folded past 8 lines) and a Full QA notes button that shows every entry. A card in any other column, or with no QA notes, shows no box. I did not look at it in the running app, only through tests (`pwsh -File tools/test.ps1 -Filter SendBackReason`); check spacing and colour.

Note: Full QA notes expands the whole section inside the box rather than scrolling the panel.
