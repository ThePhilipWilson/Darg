# WOR-0108: run QA button on cards in QA ready

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

run qa button on cards in qa ready

From a note on the wall (20261117-150353-52d0).

## How to check

- Changed: a 🧪 Run QA button on a card in QA (card panel, and the card's context menu). It starts an agent (`ReportStore.QaRunPrompt`) that builds and checks the card from its How to check section and says what passed, failed or couldn't be checked. It changes no code, doesn't move the card, writes no report. The board stays showing.
- Commit: `124cc00` on main (branch wor-0108, merged and deleted).
- Try: publish or build Darg, open a card in QA Ready with no agent, press 🧪 Run QA (also right-click, Run QA). An agent named "WOR-xxxx title (QA)" should start; the Board stays on screen.
- Not exercised live: I only built and ran unit tests (one new prompt test passes; an unrelated TicketTitle test fails, not from this change). The wording of the prompt is mine; say if it should also move the card or write a report.
