# WOR-0105: Mark QA ticket done like approving report

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

mark as done on qa ticket should follow same flow as pressing approve on the report

and should mark the report approved

From a note on the wall (20261117-145940-cb81).

## How to check

Changed: Done on a card in QA (card panel) now also marks that card's newest unsettled report approved (decision: approved, Approve box ticked), as Approve in Reports does. Commit d6faf4d on main (branch wor-0105, merged).

1. Run the app built from main (`dotnet build`, then the Debug exe) on a throwaway instance with a card in QA that has a QA or decision report.
2. Open the card, press Done (the agent must be finished).
3. Look: card moves to the done column; Reports shows that report as approved with no Approve/Send back buttons.

## QA notes

- 2026-11-17: code reviewed (d6faf4d), build clean, tests 537/538 (the one failure is TicketTitleTests, unrelated). Not exercised in the running app: press Done on a QA card with a report and confirm Reports shows it approved.
