# WOR-0140: API cards and card reads

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

From the WOR-0135 investigation (see its Findings section). Fettle answers board questions by grepping `Status:` across items, `head`-ing 19 card files to triage QA, and joining `agents` output against card files in Python to see which agents sit on Done cards (twice). Finding a card's file is `ls items | grep`.

**Want**
- `cards {project, column?, label?, text?}`: each card's id, title, column, type, labels, file and live agent (id, state), across the project's boards.
- `card {project, id}`: that plus its text, decision (`CardDecision`), QA notes, reports for it and every agent on it.

**Done when**
- "Which QA cards have no agent?" and "which agents are on Done cards?" are one call each (integration test).

Effort: about 2 h.
