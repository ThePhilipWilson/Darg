# WOR-0092: Cards entering Needs Phil Attention get a decision report automatically

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

Philip wants to go through Needs Phil Attention cards in series without rooting for information. Today the reports for these were written by hand (one-off agent). Make it automatic: when a card lands in Needs Phil Attention (an agent or QA check moves it, or Philip does), a report `<stamp>-<card>-qa.md` is written to the project's reports folder with what was asked, what changed, how to look, what was and wasn't checked, the commit, and a Decision section at the foot (recommendation, Approve / Send back, notes). Ideally the decision in the report acts on the card (Approve moves it to Done; Send back uses the send-back path with the notes).

I also want a link from the ticket card directly to it.

Where: `ReportStore.Rules` and the rework preface in Core; the board's column-move path for the trigger.

Done when: moving a card into Needs Phil Attention produces an unread report ending in a Decision section, without anyone asking.


## How to check

Commit af22156 on main (branch wor-0092, merged and pushed). Not tried live: only Core logic has tests.

1. Update the installed copy (header ⟳ menu → Update), or build and run the Debug exe on a throwaway instance (`DARG_HOME=.shots/home`).
2. On a board with a Needs Phil Attention column, drag any card into it (or have an agent set its Status and board.json order).
3. Within a second or so Reports gets an unread `<stamp>-<card>-qa.md` (list "QA"), ending in a Decision section. Asked/Look at come from the card; Changed/Checked from the newest agent report for it, else flagged as unchecked.
4. The card panel has a 📋 Report button that opens it.
5. In Reports, a pending report shows a notes box with ✓ Approve (card goes to Done) and ↩ Send back (needs notes; they go into the card under QA notes, an agent starts, card to In Progress). Either ticks the box in the file and sets `decision:`.

Look for: the report reads usefully in series; a card already in the column at app start gets no report (only new arrivals); a second move within 2 minutes makes no duplicate.
