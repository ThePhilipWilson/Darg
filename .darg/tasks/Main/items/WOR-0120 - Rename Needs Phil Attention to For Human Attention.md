# WOR-0120: Rename Needs Phil Attention to For Human Attention

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** 
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

Rename the "Needs Phil Attention" column to "For Human Attention", and make it a default column for all projects (new boards get it, and existing projects' boards gain it if missing).

Also update anything that refers to the old name: button labels, report headings, flows and docs.

## How to check

Changed: column renamed to For Human Attention; it is in `BoardStore.DefaultColumns`; `BoardStore.EnsureAttentionColumn` runs on every board load (rename legacy column + its cards' Status, or add the column before the last one, once per board via `attentionColumn: true` in board.json). Button now reads "For human attention".

Commit 9973adc on main (branch wor-0120, merged and pushed).

1. Build and open Darg on a throwaway instance (`DARG_HOME=.shots/home`) whose board has a column named Needs Phil Attention: it should show as For Human Attention, cards kept.
2. Open a board with no such column: one appears before the last column.
3. Make a new board: it has To Do, In Progress, For Human Attention, Done.
4. A QA card: the "🔔 For human attention" button moves it there.

Not seen on screen. Your real Workbench board will migrate the first time the new build loads it.

## Checked in the app

Round 2 (after "why is this needing me?"): all four checks above run by the agent on a throwaway instance (a copy of the real Main board, a board with To Do/In Progress/Done, and a project with no boards), build of 9973adc, no code changed. Screenshots in `D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0120/`.

1. Copy of the real Main board: column shows as For Human Attention, its 8 cards kept, their Status lines rewritten, `attentionColumn: true` written; no decision reports fired by the rename.
2. Plain board: For Human Attention added before Done.
3. + Board on a project with no boards: columns offered are To Do, In Progress, For Human Attention, Done.
4. A card in QA Ready: the 🔔 For human attention button moved it there and Darg wrote its decision report.

## QA notes

### 2026-11-17 18:05

why is this needing me?
