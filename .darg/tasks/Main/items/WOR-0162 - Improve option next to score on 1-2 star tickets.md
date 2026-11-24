# WOR-0162: Improve option next to score on 1-2 star tickets

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:** 2026-11-21
- **Moved:** 2026-11-21 00:45:59

## Description

On any ticket scored 1 or 2 stars, show an "Improve" option next to the score. It should do what the card's Improve button does (start an agent that fills out a thin card).

## How to check

Changed: a `✦ Improve` button beside the star on any card tile and in the card panel when the card is under 3 stars (0 included), has no agent and isn't in Done. It runs the same `StartImprove` as the card's Improve button; the panel's follows the description as you type.

Branch `wor-0162`, merged to `android` as fbd2ce7. Run `pwsh -File tools/publish.ps1` (or ⟳ Update), open a board with a card showing ★1 or ★2: Improve sits next to the star, pressing it starts an Improve agent, and a ★3 card shows none. Not seen by me: how it looks (tile width with the button; the integration test saves `improve-beside-score.png` under `.shots/integration/` when run).
