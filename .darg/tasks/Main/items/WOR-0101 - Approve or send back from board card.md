# WOR-0101: Approve or send back from board card

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

qa ready/ needs phil attention tickets should have the approve/send back available from clicking the card in the board view. some of them don't need the report writing

From a note on the wall (20261117-112140-6c84).

## How to check

Changed: a card in QA Ready or Needs Phil Attention (with no agent running) now offers Approve (Done) and Send back from the board. Commit 089e4b8 on main (branch wor-0101, merged).

- Click such a card on the board: the panel shows "Where does this card go?" with Done (no report written; marks any report approved) and, for Needs Attention cards, the Send back box. Needs my attention shows only for QA Ready cards.
- Right-click the card: Approve: move to done, and Send back with notes.
- Build and run: src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe

## QA notes (2026-11-17)

- Build OK; tests 537/538 pass. The one failure (TicketTitleTests, a file-name expectation) is in code this card does not touch.
- Not exercised in the running app: the panel and context-menu buttons were checked by reading the diff only. Needs your eyes.
