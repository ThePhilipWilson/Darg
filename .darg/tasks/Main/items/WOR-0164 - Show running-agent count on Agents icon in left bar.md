# WOR-0164: Show running-agent count on Agents icon in left bar

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 22:37:06

## Description

The Agents icon on the left bar should show a number for how many agents are running. It should preferably not look like the notification badges (e.g. a quieter, differently shaped indicator).

## Decision answered 2026-11-20

**Question:** Does the Agents count pip read as quiet enough (an outlined square, top left of the icon) and look right in all three themes?
**Asked by:** agent (QA), 2026-11-20
- **Looks fine** (recommended) → done: the pip stays as built and the card goes to done.
- **Needs a change** → back: sent back for more work with your notes on what to change.
**Answer:** Looks fine
**Context:** Everything checked by automation passed; only how it looks is yours to judge. In the dark theme it is a small outlined blue "2" square beside the icon, unlike the filled pink badges.

![two agents running](D:/GitHub/ThePhilipWilson/Workbench/.shots/integration/The_Agents_icon_counts_agents_at_work_and_hides_the_count_when_none_are/two-running.png)

## How to check

Changed: the rail's Agents icon shows a small outlined square (top left, working colour) with the number of agents starting or at work; hidden at none. An agent waiting on you is not counted (it keeps the ! pip on the right). Branch android, commit 0b342d5 (branch wor-0164).

Try it: build and run (dotnet build; src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe), start two agents, look at the Agents icon in the left rail. Screenshot from the integration test: D:/GitHub/ThePhilipWilson/Workbench/.claude/worktrees/wor-0164/.shots/integration/The_Agents_icon_counts_agents_at_work_and_hides_the_count_when_none_are/two-running.png

Look for: the pip reads as quieter than the filled accent badges (Reports, !), is readable in all three themes, and clears when agents finish.

## QA notes

- Full suite (`tools/test.ps1`) on branch android at 0b342d5: unit 765/765, integration 121/121, no failures. Includes RunningPipTests (two agents show 2, closing one shows 1, hidden at none).
- Code read: RunningCount counts agents that are Working and updates on list change and state change; waiting agents are excluded.
- Not automated: whether it looks quiet and is readable in Dark, Light and Girlypop. Only the dark-theme PNG was viewed; that is a visual judgement.
