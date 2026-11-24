# WOR-0165: Show running-agent count next to Agents on Home page

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 22:21:17

## Description

The same running-agent number as the left bar Agents icon (WOR-0164) should show on the Home page, next to Agents.

## How to check

Changed: Home's Agents tile heading now reads `Agents · N running` (then `· M need you` if any), N being the agents at work, the same count as the rail's Agents icon (WOR-0164, `a.Working`). Branch `wor-0165`, commit 88f993e (on `android`, pushed).

Try it: run the app (`pwsh -File tools/publish.ps1` from a terminal, or a debug build), open a project's Home, start an agent on a card and look at the Agents tile heading. Look for the count matching the rail icon's pip; no suffix when none are running. I did not look at it on screen, and WOR-0164 had not landed when I wrote this, so check both together.
