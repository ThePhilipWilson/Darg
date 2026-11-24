# WOR-0086: Fettle asks new ticket or amend before sending to an agent

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-17
- **Done:**

## Description

When Philip asks Fettle for a change while looking at a card's agent, Fettle forwarded it straight to that agent (`say`). That silently amends the agent's task, and Philip may have meant a separate ticket.

Fettle's prompt should have it ask with AskUserQuestion first: "new ticket or amend?". New ticket adds a board card; amend sends the text to the agent. It should not pick either itself.

Done when a request like "X should also do Y", made from an agent's view, gets that question before anything is sent or created.

## How to check

What changed: Fettle's briefing (`MascotViewModel.Briefing`) now tells it, when you ask for a change while looking at an agent, to ask with AskUserQuestion "New ticket" or "Amend the agent's task" before doing either, and never pick one itself unless your words already say which. The "[Philip is looking at]" line for the Agents section now also carries the agent's id and its card. Commit `1c66ed8` on main (branch wor-0086, merged and deleted). Built clean; not run live.

To try: publish or ⟳ Update Darg (Fettle's briefing is set when its chat starts, so open a new Fettle chat). Open Agents, pick any card agent, and tell Fettle something like "this should also update the README".

Look for: a question with New ticket / Amend the agent's task before anything is sent or created. Pick New ticket: a card appears on the board, the agent gets nothing. Pick Amend: the agent's transcript gets your words.
