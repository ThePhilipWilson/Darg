# WOR-0111: send back to qa ready needs option for notes as to why

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** reports, qa
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

why did I need to see this button on Needs Phil Attention that sends back to qa ready and tells them to sort there shit out. option for notes as to why.

From a note on the wall (20261117-155504-a5d7).

## How to check

Changed: the card panel's QA verdict area (card in QA Ready) has a new optional box, "Why it needs your attention", above the Needs my attention button. Branch wor-0111, merged to main as 2e65185.

Try it: dotnet build, run src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe, open a card in QA Ready with no agent, type a reason, press Needs my attention.

Look for: the card lands in Needs Phil Attention; its file has a dated entry under ## Why it needs attention; the decision report in Reports shows a Why it needs attention section with your words. Empty box behaves as before. Not seen on screen: only the Core test (ReportStoreTests) and the build were run.

Note: I read the card as "why is this card in Needs Phil Attention" (your pick); Send back already needs notes everywhere.
