# WOR-0242: Agents stop after an installed-copy restart; no sign it is not the live copy

- **Status:** QA Ready
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-26
- **Done:**
- **Moved:** 2026-11-26 17:17:58

## Description

HOM-001 finished and should have been hot-reloaded, but the running Darg is the installed copy (D:/Apps/Darg), not the live copy from tools/live.ps1 (dotnet watch). Only the live copy takes updates without restarting. After the update, all In Progress agents stopped and did not restart. Wanted: a restart or update must not strand running agents (resume them afterwards), and the app should make clear when it is not the live copy so a missing hot reload is not a surprise.

## How to check

**What changed** (commit 888e663 on `android`, pushed: https://github.com/ThePhilipWilson/Darg/commit/888e663). The ⟳ button in the header now names the copy: `⟳ installed`, `⟳ live` or `⟳ debug`. Its tooltip says only the live copy takes changes while it runs. The installed copy re-reads its repo every minute (off the UI thread), so a merged card shows as `⟳ installed •` without the menu being opened. The flyout starts "The installed copy: it doesn't hot reload, so changes reach it only through Update". The restart warning now says work agents on cards carry on by themselves, chats come back saved, and QA or Improve runs end.

**The resume half did not reproduce.** agents.log and perf.log show no agent mid-turn at either restart today: nothing ran from 16:08 to 16:42, so the ⟳ Update at 16:22 stopped no agent, and Fettle's kill at about 16:45 came after its own turn ended. I then tried it live on a throwaway instance with fake agents (`.shots/wor-0242/restart-check.ps1`): two work agents mid-turn, then (a) a normal window close and (b) `Stop-Process -Force`, as Fettle did. Both times both agents came back and resumed their own sessions (WOR-0221, commit b3d3b8a, which the 16:22 build had). What probably looked stranded: HOM-0001 is still in In Progress on the home board with no agent. Its agent's turn ended at 12:53 after writing How to check, but it never moved the card to QA. So it was a finished agent that didn't move its card, not one a restart stopped.

**To try it.**
1. From a terminal with Darg closed: `pwsh -File tools/publish.ps1` (or ⟳ → Update and restart from the installed copy). The header should read `⟳ installed`. Hover it to see the tooltip.
2. Let any card merge into the repo (or touch a file under `src/`). Within a minute, without opening the menu, the header should read `⟳ installed •`.
3. `tools/live.ps1` should still read `⟳ live`.

![The installed label (a debug build with source.txt beside it, repo newer than its stamp)](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0242/wor-0242-installed-header.png)

**Look for:** whether the words and the width read well. `⟳ installed · update ready •` is the longest form.

**Tests.** `RestartHeaderTests` (4, integration). The test of the background check fails with the check disabled. Whole suite: 962 unit, 284 integration, all pass (DocsTests failed once on my first run because two docs named `⟳ Update`, which the header no longer says; the docs are fixed).
