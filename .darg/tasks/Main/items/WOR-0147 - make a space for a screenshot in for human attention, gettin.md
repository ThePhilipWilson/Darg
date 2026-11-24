# WOR-0147: make a space for a screenshot in for human attention, getting a…

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

make a space for a screenshot in for human attention, getting a question that says "does this look okay" but I can't see the thing is pointless

From a note on the wall (20261118-115111-8d91).

## How to check

Changed: the decision box in the card panel (For Human Attention) now shows every markdown image in the card file above the options; agents are told (CardDecision.Rules) to put a screenshot in the card as `![x](C:/full/path.png)` when the question is about how something looks. Commit 9dc654e on main (branch wor-0147, merged).

1. Make a card in For Human Attention with a `## Decision` and a line like `![shot](D:/some/image.png)` anywhere in its file.
2. Open the card in a build of main (`dotnet build`, run Darg).
3. Look for: the picture drawn between the question and the options. A card with no image shows no gap. Not seen in a live window (headless test only checks the list).

## QA result 2026-11-18

Suite: unit 617/617, integration 61/61. Rendered in the headless window with a real 600x300 PNG: the picture sits between the question and the options (screenshot checked). Note: an image line placed directly under the Decision's Context line is absorbed into the Context text and also shows as raw markdown below the options. Cosmetic only.
