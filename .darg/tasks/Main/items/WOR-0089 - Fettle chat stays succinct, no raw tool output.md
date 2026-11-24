# WOR-0089: Fettle chat stays succinct, no raw tool output

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-17
- **Done:**

## Description

Keep the Fettle chat short. Raw tool output (bash and the like) shouldn't fill the box: collapse or hide it so the reply is just the answer or confirmation that the instruction was done. Full detail belongs to agents; Fettle is for quick answers and instructions followed.

Ideas to look at: collapse tool calls and their output by default (expand on click), tighten the system prompt on reply length, and point Philip at an agent when something needs the long version.

## How to check

What changed: in Fettle's box each tool step (Bash, Read, Edit…) is now one quiet line (name, summary, tick or cross) with its output, diff or images folded away; click the line to open it (▸ turns to ▾). The Agents view still shows every step in full. Fettle's briefing now asks for replies of one to three sentences, never pasted output, and to offer an agent (with a question) when a job needs the long version.

Commit: 68febca on main (branch wor-0089, merged and deleted).

To try: update Darg (header ⟳ menu, or `pwsh -File tools/publish.ps1` from a terminal), open Fettle and ask something that makes it run a command, e.g. "how many commits are on Darg's main and what's the newest?". Then ask for something long ("review the whole MascotViewModel").

Look for:
- the command shows as one dim line, no output, until clicked; clicking opens and closes it
- the reply is short, with no pasted output
- the long request gets a one-line offer to start an agent instead of a long answer
- in the Agents view, a Fettle chat or any agent still shows tool steps in full (not checked live)

Screenshots: D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0089/.shots/ was removed with the worktree; none kept.
