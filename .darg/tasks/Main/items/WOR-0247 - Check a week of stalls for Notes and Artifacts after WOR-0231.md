# WOR-0247: Check a week of stalls for Notes and Artifacts after WOR-0231

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-27
- **Done:**
- **Moved:** 2026-11-27 12:05:39

## Description

The last Done when of WOR-0231, split off when that card closed (Philip's pick, 2026-11-27): a week of stalls.log from a build holding WOR-0231's fix (commit `4e76e82`) should show no `Notes.Reload` or `Artifacts.Refresh` stall over 200 ms.

It can't start yet. On 27 Nov the installed copy (`D:/Apps/Darg`, built 26 Nov 16:23) still predated the fix (19:11 the same day), and the installed and live copies write the same `stalls.log` in the `home` instance without saying which copy wrote a line. So the week counts from the day a published copy holding `4e76e82` (⟳ Update or `tools/publish.ps1`) first runs. The live copy alone carrying it isn't enough.

**Where:** the `home` instance's `stalls.log` and `perf.log`, read with `pwsh -File tools/perf/analyze.ps1 -Since <day the fixed build first ran>`; the result goes in `docs/performance.md` under "Hotspots 4 and 5, fixed (WOR-0231, 26 Nov)".

**Done when:**
- A published build with `4e76e82` has been the installed copy for 7 days, and `analyze.ps1` over those days is run.
- `docs/performance.md`'s WOR-0231 part says how many `Notes.Reload` and `Artifacts.Refresh` stalls over 200 ms the week had, the worst of each, and the dates covered.
- If any are over 200 ms, a card is made for what the stack under the stall shows (the sections now time only the showing, not the reading; a large notes wall is the known risk: at 400 notes the showing alone reached 150 to 490 ms in QA).
