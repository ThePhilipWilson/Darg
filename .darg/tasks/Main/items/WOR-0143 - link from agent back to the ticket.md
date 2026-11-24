# WOR-0143: link from agent back to the ticket

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-18
- **Done:**

## Description

link from agent back to relevant ticket

From a note on the wall (20261118-101828-d33d).

## Decision answered 2026-11-18 11:45

**Question:** Is the `↗ WOR-xxxx` button's placement and size in the agent header row acceptable?
**Asked by:** agent (QA), 2026-11-18
- **Looks fine** (recommended) → done: the card goes to done.
- **Needs restyling** → back: sent back for more work with your notes on placement or size.
**Context:** Everything automatable passed (full suite: unit 603/603, integration 47/47, including An_agent_links_back_to_its_card, which checks the button shows for a card agent and opens the board with the card panel). The test doesn't draw the header, so how the button looks can't be judged by automation.

**Answer:** there is no image in the report for me to judge by (2026-11-18 11:45)

## Decision

**Question:** Looking at the screenshot of the agent header (artifact "WOR-0143: the card link in the agent header", image `.shots/qa143/agents.png`), is the `↗ FLO-0001` button's look and placement fine?
**Asked by:** agent (QA), 2026-11-18
- **Looks fine** (recommended) → done: it sits right after the status and reads as a small link; the card goes to done.
- **Make it look like a button** → back: it has no border or fill, so it may not read as clickable; sent back for more work with your notes.
**Context:** Screenshot taken from the debug build on a throwaway instance with a fake card agent: the button is small, borderless text between "working · 27 steps" and the path, and the header isn't crowded.

## How to check

- Changed: a card agent's header (Agents section) now has a `↗ WOR-xxxx` button that opens its card on the board. Commit 7247f0e on main (branch wor-0143, merged and pushed).
- Try: build (`dotnet build`), open Darg, start ▶ Work on a card, go to Agents and click the `↗ WOR-xxxx` button beside the agent's status.
- Look for: it jumps to the Board with the card panel open; it only shows for agents with a card; how it looks in the header row (I couldn't see it drawn, so placement and size are unchecked). Integration test `An_agent_links_back_to_its_card` covers the behaviour.

